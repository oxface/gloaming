using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace Gloaming.Release.Tests;

public sealed class BrokerProxyTests
{
    private const int ScenarioTimeout = 60_000;
    private static readonly byte[] ProduceRequest = [0, 0, 0, 8, 0, 0, 0, 0, 0, 0, 0, 1];

    [Fact(Timeout = ScenarioTimeout)]
    public async Task DrainsAfterUpstreamResetDuringRelayStartup()
    {
        var cancellation = TestContext.Current.CancellationToken;
        using var upstream = StartListener();

        // Buffer a request before the connect continuation, then reset the upstream socket.
        // Repeat the native race so the relay can observe the reset during stream setup.
        for (int iteration = 0; iteration < 32; iteration++)
        {
            using var scheduler = new RelayScheduler();
            var proxy = CreateProxy(upstream);
            scheduler.Run(proxy.Start);

            try
            {
                using var client = new TcpClient();
                await client.ConnectAsync(IPAddress.Loopback, proxy.Port, cancellation);
                await client.GetStream().WriteAsync(ProduceRequest, cancellation);
                var connection = upstream.AcceptTcpClientAsync(cancellation).AsTask();
                scheduler.Step(cancellation);
                using (var peer = await connection)
                    peer.LingerState = new LingerOption(true, 0);

                await Task.Delay(20, cancellation);
                scheduler.Step(cancellation);
            }
            finally
            {
                await scheduler.DisposeProxyAsync(proxy);
            }
        }
    }

    [Fact(Timeout = ScenarioTimeout)]
    public async Task DrainsWhenDisposedDuringConnectionSetup()
    {
        var cancellation = TestContext.Current.CancellationToken;
        using var upstream = StartListener();

        for (int iteration = 0; iteration < 32; iteration++)
        {
            using var scheduler = new RelayScheduler();
            using var client = new TcpClient();
            var proxy = CreateProxy(upstream);
            scheduler.Run(proxy.Start);
            TcpClient? peer = null;

            try
            {
                await client.ConnectAsync(IPAddress.Loopback, proxy.Port, cancellation);
                var connection = upstream.AcceptTcpClientAsync(cancellation).AsTask();
                scheduler.Step(cancellation);
                peer = await connection;
            }
            finally
            {
                // Begin disposal while the connect continuation is still queued.
                using (peer)
                    await scheduler.DisposeProxyAsync(proxy);
            }
        }
    }

    [Fact(Timeout = ScenarioTimeout)]
    public async Task DrainsDuringConcurrentConnectionAttempts()
    {
        var cancellation = TestContext.Current.CancellationToken;
        using var upstream = StartListener();
        using var resets = CancellationTokenSource.CreateLinkedTokenSource(cancellation);
        var resetter = Task.Run(
            async () =>
            {
                try
                {
                    while (!resets.IsCancellationRequested)
                    {
                        using var peer = await upstream.AcceptTcpClientAsync(resets.Token);
                        peer.LingerState = new LingerOption(true, 0);
                    }
                }
                catch (OperationCanceledException) when (resets.IsCancellationRequested) { }
            },
            cancellation
        );

        try
        {
            for (int iteration = 0; iteration < 128; iteration++)
            {
                await using var proxy = CreateProxy(upstream);
                proxy.Start();
                await Task.WhenAll(
                    Enumerable
                        .Range(0, 8)
                        .Select(async _ =>
                        {
                            using var client = new TcpClient();
                            await client.ConnectAsync(IPAddress.Loopback, proxy.Port, cancellation);
                            client.LingerState = new LingerOption(true, 0);
                        })
                );
                await Task.Delay(1, cancellation);
            }
        }
        finally
        {
            await resets.CancelAsync();
            await resetter;
        }
    }

    [Fact(Timeout = ScenarioTimeout)]
    public async Task ReportsUnexpectedRelayFaultOnDisposal()
    {
        var cancellation = TestContext.Current.CancellationToken;
        using var upstream = StartListener();
        using var scheduler = new RelayScheduler();
        using var client = new TcpClient();
        var proxy = CreateProxy(upstream);
        scheduler.Run(proxy.Start);
        TcpClient? peer = null;

        try
        {
            await client.ConnectAsync(IPAddress.Loopback, proxy.Port, cancellation);
            await client.GetStream().WriteAsync(new byte[] { 0, 0, 0, 1 }, cancellation);
            var connection = upstream.AcceptTcpClientAsync(cancellation).AsTask();
            scheduler.Step(cancellation);
            peer = await connection;
            scheduler.Step(cancellation);
        }
        catch
        {
            using (peer)
                await scheduler.DisposeProxyAsync(proxy);
            throw;
        }

        using (peer)
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                scheduler.DisposeProxyAsync(proxy)
            );
    }

    [Fact(Timeout = ScenarioTimeout)]
    public async Task ForwardsThroughTheSameListenerAfterUpstreamRemapping()
    {
        var cancellation = TestContext.Current.CancellationToken;
        using var upstream = StartListener();
        using var restarted = StartListener();
        await using var proxy = CreateProxy(upstream);
        proxy.Start();
        int port = proxy.Port;
        proxy.SetUpstream(null);
        proxy.SetUpstream(restarted.LocalEndpoint.ToString());

        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, cancellation);
        using var peer = await restarted.AcceptTcpClientAsync(cancellation);
        await client.GetStream().WriteAsync(ProduceRequest, cancellation);
        var received = new byte[ProduceRequest.Length];
        await peer.GetStream().ReadExactlyAsync(received, cancellation);
        Assert.Equal(ProduceRequest, received);
    }

    private static TcpListener StartListener()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return listener;
    }

    private static BrokerProxy CreateProxy(TcpListener upstream) =>
        new(upstream.LocalEndpoint.ToString()!, new(IPAddress.Loopback, 0));

    // Control only async scheduling. All connection, reset and frame operations use native TCP.
    private sealed class RelayScheduler : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> work = [];

        public override void Post(SendOrPostCallback callback, object? state) =>
            work.Add((callback, state));

        internal void Run(Action action)
        {
            var previous = Current;
            SetSynchronizationContext(this);
            try
            {
                action();
            }
            finally
            {
                SetSynchronizationContext(previous);
            }
        }

        internal void Step(CancellationToken cancellation)
        {
            if (work.TryTake(out var next, 100, cancellation))
                Run(() => next.Callback(next.State));
        }

        internal async Task DisposeProxyAsync(BrokerProxy proxy)
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var task = proxy.DisposeAsync().AsTask();
            while (!task.IsCompleted)
                Step(cleanup.Token);

            await task;
        }

        public void Dispose() => work.Dispose();
    }
}
