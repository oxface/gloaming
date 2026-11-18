using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

// Test-only Kafka plaintext frame proxy. It never decodes records or serves application traffic.
// Kafka request API key 0 is Produce; response correlation IDs identify exactly its replies.
sealed class BrokerProxy(string upstream, int port) : IAsyncDisposable
{
    private readonly TcpListener listener = new(IPAddress.Loopback, port);
    private readonly CancellationTokenSource stopping = new();
    private readonly ConcurrentBag<Task> sessions = [];
    private Task accept = Task.CompletedTask;
    private int dropping;
    private int dropped;
    internal bool DropProduceResponses
    {
        set => Volatile.Write(ref dropping, value ? 1 : 0);
    }

    internal int DroppedResponses => Volatile.Read(ref dropped);

    internal void Start()
    {
        listener.Start();
        accept = AcceptAsync();
    }

    private async Task AcceptAsync()
    {
        try
        {
            while (!stopping.IsCancellationRequested)
            {
                var client = await listener.AcceptTcpClientAsync(stopping.Token);
                sessions.Add(RelayAsync(client));
            }
        }
        catch (OperationCanceledException) when (stopping.IsCancellationRequested) { }
        catch (SocketException) when (stopping.IsCancellationRequested) { }
    }

    private async Task RelayAsync(TcpClient client)
    {
        using (client)
        using (var server = new TcpClient())
        using (var pair = CancellationTokenSource.CreateLinkedTokenSource(stopping.Token))
        {
            try
            {
                var target = new Uri("tcp://" + upstream);
                await server.ConnectAsync(target.Host, target.Port, pair.Token);
                // A forwarding task can observe a reset before the reverse task starts.
                // Acquire both streams before either task can change socket state.
                var clientStream = client.GetStream();
                var serverStream = server.GetStream();
                var requests = new ConcurrentDictionary<int, short>();
                var send = ForwardAsync(clientStream, serverStream, requests, true, pair.Token);
                var receive = ForwardAsync(serverStream, clientStream, requests, false, pair.Token);
                try
                {
                    await Task.WhenAny(send, receive);
                }
                finally
                {
                    await pair.CancelAsync();
                    client.Close();
                    server.Close();
                    await Task.WhenAll(send, receive);
                }
            }
            catch (Exception error)
                when (error
                        is OperationCanceledException
                            or IOException
                            or SocketException
                            or ObjectDisposedException
                ) { }
        }
    }

    private async Task ForwardAsync(
        NetworkStream input,
        NetworkStream output,
        ConcurrentDictionary<int, short> requests,
        bool request,
        CancellationToken token
    )
    {
        byte[] size = new byte[4];
        while (!token.IsCancellationRequested)
        {
            await input.ReadExactlyAsync(size, token);
            int length = BinaryPrimitives.ReadInt32BigEndian(size);
            if (length < (request ? 8 : 4) || length > 16 * 1024 * 1024)
                throw new InvalidDataException("Unexpected test protocol frame size.");

            byte[] frame = new byte[length];
            await input.ReadExactlyAsync(frame, token);
            if (request)
                requests[BinaryPrimitives.ReadInt32BigEndian(frame.AsSpan(4, 4))] =
                    BinaryPrimitives.ReadInt16BigEndian(frame);
            else if (
                requests.TryRemove(BinaryPrimitives.ReadInt32BigEndian(frame), out short api)
                && api == 0
                && Volatile.Read(ref dropping) != 0
            )
            {
                Interlocked.Increment(ref dropped);
                continue;
            }

            await output.WriteAsync(size, token);
            await output.WriteAsync(frame, token);
        }
    }

    public async ValueTask DisposeAsync()
    {
        await stopping.CancelAsync();
        listener.Stop();
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        try
        {
            // Relays own their sockets through connect, forwarding and cancellation.
            // Drain acceptance before taking the complete set of sessions to await.
            await accept.WaitAsync(cleanup.Token);
            await Task.WhenAll(sessions).WaitAsync(cleanup.Token);
        }
        finally
        {
            stopping.Dispose();
        }
    }
}
