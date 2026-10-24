using System.Threading.Channels;
using Npgsql;

namespace Gloaming.Messaging.WakeupProof;

// Consumer-owned native composition, deliberately not a new Gloaming runtime/interface.
public sealed class NativeWakeups
{
    private readonly string endpoint;
    private readonly TimeSpan reconnectDelay;
    private readonly Channel<bool> incoming = Hint();
    private readonly Channel<bool> outgoing = Hint();
    private int pid;
    private int registrations;
    private int failures;

    public NativeWakeups(string endpoint, TimeSpan reconnectDelay)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(reconnectDelay, TimeSpan.Zero);

        this.endpoint = new NpgsqlConnectionStringBuilder(endpoint)
        {
            Pooling = false,
            Multiplexing = false,
            Enlist = false,
            Timeout = 2,
            CommandTimeout = 3,
        }.ConnectionString;
        this.reconnectDelay = reconnectDelay;
    }

    public ChannelReader<bool> Inbox => incoming.Reader;
    public ChannelReader<bool> Outbox => outgoing.Reader;
    public int ListenerPid => Volatile.Read(ref pid);
    public int Registrations => Volatile.Read(ref registrations);
    public int Failures => Volatile.Read(ref failures);

    // The host supervises this task independently from workers and cancels/awaits it before exit.
    public async Task RunAsync(CancellationToken token)
    {
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    await using var connection = new NpgsqlConnection(endpoint);
                    // A notification can be consumed by setup/ordinary commands, before WaitAsync.
                    connection.Notification += (_, notification) =>
                    {
                        if (notification.Channel == "proof_inbox")
                            incoming.Writer.TryWrite(true);
                        else if (notification.Channel == "proof_outbox")
                            outgoing.Writer.TryWrite(true);
                    };
                    await connection.OpenAsync(token);
                    await using (
                        var register = new NpgsqlCommand(
                            "LISTEN proof_inbox; LISTEN proof_outbox",
                            connection
                        )
                    )
                        await register.ExecuteNonQueryAsync(token);

                    Volatile.Write(ref pid, connection.ProcessID);
                    Interlocked.Increment(ref registrations);
                    // Registration committed; every new session forces a fresh durable-state poll.
                    incoming.Writer.TryWrite(true);
                    outgoing.Writer.TryWrite(true);
                    while (true)
                        await connection.WaitAsync(1000, token);
                }
                catch (NpgsqlException)
                {
                    Interlocked.Increment(ref failures);
                }
                catch (IOException)
                {
                    Interlocked.Increment(ref failures);
                }
                finally
                {
                    Volatile.Write(ref pid, 0);
                }

                // Worker fallback timers continue independently while this listener is unavailable.
                await Task.Delay(reconnectDelay, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    public static async Task WaitOrPollAsync(
        ChannelReader<bool> hint,
        TimeSpan fallback,
        CancellationToken token
    )
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(fallback, TimeSpan.Zero);
        using var wait = CancellationTokenSource.CreateLinkedTokenSource(token);
        wait.CancelAfter(fallback);
        try
        {
            _ = await hint.ReadAsync(wait.Token);
        }
        catch (OperationCanceledException) when (!token.IsCancellationRequested) { }
        // The losing read has completed cancellation; it cannot steal a future hint.
    }

    private static Channel<bool> Hint() =>
        Channel.CreateBounded<bool>(
            new BoundedChannelOptions(1)
            {
                FullMode = BoundedChannelFullMode.DropWrite,
                SingleReader = true,
                SingleWriter = true,
            }
        );
}
