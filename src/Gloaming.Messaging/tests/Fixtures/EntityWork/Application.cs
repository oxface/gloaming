using System.Buffers.Binary;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using Npgsql;

namespace Gloaming.Messaging.EntityWorkProof;

// Native consumer composition, deliberately not a Gloaming runtime or public lock wrapper.
public static class Application
{
    public static async Task Main(string[] args)
    {
        if (!OperatingSystem.IsLinux())
            throw new PlatformNotSupportedException("The process fixture uses Linux SIGTERM.");

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        using var termination = PosixSignalRegistration.Create(
            PosixSignal.SIGTERM,
            signal =>
            {
                signal.Cancel = true;
                deadline.Cancel();
            }
        );
        var token = deadline.Token;
        string connection =
            Environment.GetEnvironmentVariable("ENTITY_WORK_CONNECTION")
            ?? throw new InvalidOperationException("Select a disposable entity-work database.");
        string module = args[0];
        string scope = args[1];
        string entity = args[2];
        string mode = args[3];
        // An application protocol shared by all cooperating workers, with unambiguous UTF-8
        // framing and a domain prefix. A 64-bit collision adds contention, never admission.
        string[] fields = ["consumer.entity-work.v1", module, scope, entity];
        foreach (string field in fields)
            ArgumentException.ThrowIfNullOrWhiteSpace(field);

        string framed = string.Concat(
            fields.Select(field =>
                FormattableString.Invariant($"{Encoding.UTF8.GetByteCount(field)}:{field}")
            )
        );
        long advisory = BinaryPrimitives.ReadInt64BigEndian(
            SHA256.HashData(Encoding.UTF8.GetBytes(framed))
        );
        Console.WriteLine(
            "LOCK_KEY:" + advisory.ToString(System.Globalization.CultureInfo.InvariantCulture)
        );
        try
        {
            await using var database = new NpgsqlConnection(connection);
            await database.OpenAsync(token);
            await using var transaction = await database.BeginTransactionAsync(
                System.Data.IsolationLevel.ReadCommitted,
                token
            );
            if (mode == "savepoint-hold")
                await transaction.SaveAsync("before_lock", token);

            await using (
                var acquire = new NpgsqlCommand(
                    "SELECT pg_advisory_xact_lock(@key)",
                    database,
                    transaction
                )
            )
            {
                acquire.Parameters.AddWithValue("key", advisory);
                await acquire.ExecuteNonQueryAsync(token);
            }

            Console.WriteLine("ACQUIRED");
            await using (
                var insert = new NpgsqlCommand(
                    "INSERT INTO entity_work.counters (module, scope, entity, value) VALUES (@module, @scope, @entity, 0) ON CONFLICT DO NOTHING",
                    database,
                    transaction
                )
            )
            {
                Parameters(insert);
                await insert.ExecuteNonQueryAsync(token);
            }

            int previous;
            await using (
                var read = new NpgsqlCommand(
                    "SELECT value FROM entity_work.counters WHERE module = @module AND scope = @scope AND entity = @entity",
                    database,
                    transaction
                )
            )
            {
                Parameters(read);
                previous = (int)(await read.ExecuteScalarAsync(token))!;
            }

            await using (
                var update = new NpgsqlCommand(
                    "UPDATE entity_work.counters SET value = @next WHERE module = @module AND scope = @scope AND entity = @entity",
                    database,
                    transaction
                )
            )
            {
                Parameters(update);
                update.Parameters.AddWithValue("next", checked(previous + 1));
                await update.ExecuteNonQueryAsync(token);
            }

            Console.WriteLine("SQL_SAVED");
            if (mode == "savepoint-hold")
            {
                await transaction.RollbackAsync("before_lock", token);
                Console.WriteLine("SAVEPOINT_REVERTED");
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            }

            if (mode == "hold")
            {
                string release = Environment.GetEnvironmentVariable("ENTITY_WORK_RELEASE")!;
                while (!File.Exists(release))
                    await Task.Delay(25, token);
            }

            if (mode == "rollback")
            {
                await transaction.RollbackAsync(token);
                Console.WriteLine("ROLLED_BACK");
                return;
            }

            await transaction.CommitAsync(token);
            Console.WriteLine("COMMITTED");
            if (mode == "committed-hold")
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested)
        {
            Console.WriteLine("CANCELLED");
        }

        void Parameters(NpgsqlCommand command)
        {
            command.Parameters.AddWithValue("module", module);
            command.Parameters.AddWithValue("scope", scope);
            command.Parameters.AddWithValue("entity", entity);
        }
    }
}
