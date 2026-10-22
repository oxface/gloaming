using ModulithFoundry.Samples.Wholesale.Worker.Broker;
using Npgsql;

namespace ModulithFoundry.Samples.Wholesale.IntegrationTests.Reservations.Broker;

// Disposable-database trigger: observe a writer inside its native transaction before
// allowing it to commit. This supplies deterministic races and death boundaries.
internal sealed class WorkflowBarrier(NpgsqlConnection owner, string database) : IAsyncDisposable
{
    private const int LockKey = 182914;

    internal static async Task<WorkflowBarrier> HoldAsync(
        string database,
        bool insert,
        CancellationToken token,
        string table = "sales.stock_issue_requests",
        string? condition = null
    )
    {
        var owner = new NpgsqlConnection(database);
        await owner.OpenAsync(token);
        try
        {
            await using var command = new NpgsqlCommand(
                $"""
                SELECT pg_advisory_lock({LockKey});
                CREATE FUNCTION public.workflow_proof_barrier() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    PERFORM pg_advisory_xact_lock({LockKey});
                    RETURN NEW;
                END;
                $$;
                """
                    + $"CREATE TRIGGER workflow_proof_barrier BEFORE {(insert ? "INSERT" : "UPDATE")} ON {table} FOR EACH ROW "
                    + (condition is null ? "" : $"WHEN ({condition}) ")
                    + "EXECUTE FUNCTION public.workflow_proof_barrier();",
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

    internal Task WaitForAdvisoryAsync(int count, CancellationToken token) =>
        WaitAsync("advisory", count, token);

    internal Task WaitForRowWriterAsync(CancellationToken token) =>
        WaitAsync("transactionid", 1, token);

    private Task WaitAsync(string lockType, int count, CancellationToken token) =>
        WorkflowProof.EventuallyAsync(
            async () =>
                await WorkflowProof.CountAsync(
                    database,
                    $"""
                    SELECT count(*) FROM pg_locks locks JOIN pg_stat_activity activity ON activity.pid = locks.pid
                    WHERE locks.locktype = '{lockType}' AND NOT locks.granted AND activity.datname = current_database()
                    """,
                    token
                ) >= count,
            token
        );

    internal async Task ReleaseAsync(CancellationToken token)
    {
        await using var command = new NpgsqlCommand($"SELECT pg_advisory_unlock({LockKey})", owner);
        await command.ExecuteNonQueryAsync(token);
    }

    public async ValueTask DisposeAsync() => await owner.DisposeAsync();
}
