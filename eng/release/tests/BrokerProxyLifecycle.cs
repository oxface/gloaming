using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(60));
using var upstream = new TcpListener(IPAddress.Loopback, 0);
upstream.Start();

// Buffer a valid request, then reset the upstream before the relay's connect continuation.
// The first forwarding operation can observe the reset synchronously, before the reverse
// operation starts. Repeating this native race makes the original GetStream fault observable.
for (int iteration = 0; iteration < 32; iteration++)
{
    using var scheduler = new RelayScheduler();
    var port = FreePort();
    var proxy = new BrokerProxy(upstream.LocalEndpoint.ToString()!, port);
    scheduler.Run(proxy.Start);
    using var client = new TcpClient();
    await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
    byte[] request = [0, 0, 0, 8, 0, 0, 0, 0, 0, 0, 0, 1];
    await client.GetStream().WriteAsync(request, timeout.Token);
    var connection = upstream.AcceptTcpClientAsync(timeout.Token).AsTask();
    scheduler.Step(timeout.Token);
    using (var peer = await connection)
        peer.LingerState = new LingerOption(true, 0);

    await Task.Delay(20, timeout.Token);
    scheduler.Step(timeout.Token);
    await scheduler.FinishAsync(proxy.DisposeAsync().AsTask(), timeout.Token);
}

Console.WriteLine("PASS reset during relay startup: 32");

for (int iteration = 0; iteration < 32; iteration++)
{
    using var scheduler = new RelayScheduler();
    var port = FreePort();
    var proxy = new BrokerProxy(upstream.LocalEndpoint.ToString()!, port);
    scheduler.Run(proxy.Start);
    using var client = new TcpClient();
    await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
    var connection = upstream.AcceptTcpClientAsync(timeout.Token).AsTask();
    scheduler.Step(timeout.Token);
    using var peer = await connection;
    // Disposal begins while the relay's connect continuation is still queued.
    await scheduler.FinishAsync(proxy.DisposeAsync().AsTask(), timeout.Token);
}

Console.WriteLine("PASS stop during connection setup: 32");

using (var resets = CancellationTokenSource.CreateLinkedTokenSource(timeout.Token))
{
    var resetter = Task.Run(async () =>
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
    });
    try
    {
        for (int iteration = 0; iteration < 128; iteration++)
        {
            var port = FreePort();
            var proxy = new BrokerProxy(upstream.LocalEndpoint.ToString()!, port);
            proxy.Start();
            await Task.WhenAll(
                Enumerable
                    .Range(0, 8)
                    .Select(async _ =>
                    {
                        using var client = new TcpClient();
                        await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
                        client.LingerState = new LingerOption(true, 0);
                    })
            );
            await Task.Delay(1, timeout.Token);
            await proxy.DisposeAsync();
        }
    }
    finally
    {
        await resets.CancelAsync();
        await resetter;
    }
}

Console.WriteLine("PASS stop during concurrent connection attempts: 128");

// A broken proxy protocol must still fail disposal; connection cleanup must not hide it.
using (var scheduler = new RelayScheduler())
{
    var port = FreePort();
    var proxy = new BrokerProxy(upstream.LocalEndpoint.ToString()!, port);
    scheduler.Run(proxy.Start);
    using var client = new TcpClient();
    await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
    await client.GetStream().WriteAsync(new byte[] { 0, 0, 0, 1 }, timeout.Token);
    var connection = upstream.AcceptTcpClientAsync(timeout.Token).AsTask();
    scheduler.Step(timeout.Token);
    using var peer = await connection;
    scheduler.Step(timeout.Token);
    try
    {
        await scheduler.FinishAsync(proxy.DisposeAsync().AsTask(), timeout.Token);
        throw new InvalidOperationException("An invalid frame was silently accepted.");
    }
    catch (InvalidDataException)
    {
        Console.WriteLine("PASS unexpected relay fault remains observable");
    }
}

static int FreePort()
{
    using var reservation = new TcpListener(IPAddress.Loopback, 0);
    reservation.Start();
    return ((IPEndPoint)reservation.LocalEndpoint).Port;
}

// Control only async scheduling; all connection, reset and frame operations use native TCP.
sealed class RelayScheduler : SynchronizationContext, IDisposable
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

    internal void Step(CancellationToken token)
    {
        if (work.TryTake(out var next, 100, token))
            Run(() => next.Callback(next.State));
    }

    internal async Task FinishAsync(Task task, CancellationToken token)
    {
        while (!task.IsCompleted)
            Step(token);

        await task;
    }

    public void Dispose() => work.Dispose();
}
