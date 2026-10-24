using System.Globalization;
using System.Runtime.InteropServices;
using Npgsql;

namespace Gloaming.EventSourcing.FeedEarlier;

// Compiled without any Gloaming reference. This writer knows only its original native journal.
public static class Application
{
    public static async Task Main(string[] args)
    {
        string connection =
            Environment.GetEnvironmentVariable("FEED_CONNECTION")
            ?? throw new InvalidOperationException("Select a disposable proof database.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        using var signal = PosixSignalRegistration.Create(
            PosixSignal.SIGTERM,
            context =>
            {
                context.Cancel = true;
                deadline.Cancel();
            }
        );
        try
        {
            await using var session = new NpgsqlConnection(connection);
            await session.OpenAsync(deadline.Token);
            Console.WriteLine("PID:" + session.ProcessID.ToString(CultureInfo.InvariantCulture));
            await using var transaction = await session.BeginTransactionAsync(deadline.Token);
            await using var insert = new NpgsqlCommand(
                """
                INSERT INTO app.ledger VALUES(@id, '00000000-0000-0000-0000-000000000082', @version,
                    'ledger.adjusted', 1, '{"amount":10}')
                """,
                session,
                transaction
            );
            insert.Parameters.AddWithValue("id", Guid.NewGuid());
            insert.Parameters.AddWithValue(
                "version",
                long.Parse(args[1], CultureInfo.InvariantCulture)
            );
            await insert.ExecuteNonQueryAsync(deadline.Token);
            Console.WriteLine("CAPTURED");
            if (args[0] == "captured")
                await Task.Delay(Timeout.InfiniteTimeSpan, deadline.Token);
            else if (args[0] == "gate")
            {
                await using var gate = new NpgsqlCommand(
                    "SELECT pg_advisory_xact_lock(918273645::bigint)",
                    session,
                    transaction
                );
                await gate.ExecuteNonQueryAsync(deadline.Token);
            }

            await transaction.CommitAsync(deadline.Token);
            Console.WriteLine("COMMITTED");
            if (args[0] == "committed")
                await Task.Delay(Timeout.InfiniteTimeSpan, deadline.Token);
        }
        catch (OperationCanceledException) when (deadline.IsCancellationRequested)
        {
            Console.WriteLine("CANCELLED");
        }
    }
}
