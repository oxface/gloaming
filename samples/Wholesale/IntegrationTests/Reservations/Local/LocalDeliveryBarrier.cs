using ModulithFoundry.Samples.Wholesale.Worker.Local;
using Npgsql;
using static ModulithFoundry.Samples.Wholesale.IntegrationTests.Reservations.Local.ProcessRecoveryTests;

namespace ModulithFoundry.Samples.Wholesale.IntegrationTests.Reservations.Local;

// Disposable-database triggers expose real transaction cuts without production fault switches.
internal sealed class LocalDeliveryBarrier(NpgsqlConnection owner, string database)
    : IAsyncDisposable
{
    private const int LockKey = 231015;

    internal static async Task<LocalDeliveryBarrier> HoldAsync(
        LocalWorkflowProof proof,
        CrashCut cut,
        CancellationToken token
    )
    {
        bool beforeIntake = cut == CrashCut.BeforeInventoryIntakeCommit;
        bool reply = cut == CrashCut.AfterSalesReplyIntakeBeforeReplyAcknowledgment;
        string database = beforeIntake || reply ? proof.Inventory : proof.Sales;
        string target = beforeIntake
            ? "AFTER INSERT ON inventory.inbox_messages"
            : $"BEFORE UPDATE ON {(reply ? "inventory" : "sales")}.outbox_messages";
        string condition = beforeIntake ? "" : "WHEN (NEW.dispatched_at IS NOT NULL)";
        var owner = new NpgsqlConnection(database);
        await owner.OpenAsync(token);
        try
        {
            await using var command = new NpgsqlCommand(
                $"""
                SELECT pg_advisory_lock({LockKey});
                CREATE FUNCTION public.local_delivery_barrier() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    PERFORM pg_advisory_xact_lock({LockKey});
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER local_delivery_barrier {target} FOR EACH ROW {condition}
                EXECUTE FUNCTION public.local_delivery_barrier();
                """,
                owner
            );
            await command.ExecuteNonQueryAsync(token);
            return new(owner, database);
        }
        catch
        {
            await owner.DisposeAsync();
            throw;
        }
    }

    internal Task WaitAsync(bool blocked, CancellationToken token) =>
        LocalWorkflowProof.EventuallyAsync(
            async () =>
                (
                    await LocalWorkflowProof.CountAsync(
                        database,
                        """
                        SELECT count(*) FROM pg_locks locks JOIN pg_stat_activity activity ON activity.pid = locks.pid
                        WHERE locks.locktype = 'advisory' AND NOT locks.granted AND activity.datname = current_database()
                        """,
                        token
                    ) > 0
                ) == blocked,
            token
        );

    internal async Task ReleaseAsync(CancellationToken token)
    {
        await using var command = new NpgsqlCommand($"SELECT pg_advisory_unlock({LockKey})", owner);
        await command.ExecuteNonQueryAsync(token);
    }

    public ValueTask DisposeAsync() => owner.DisposeAsync();
}
