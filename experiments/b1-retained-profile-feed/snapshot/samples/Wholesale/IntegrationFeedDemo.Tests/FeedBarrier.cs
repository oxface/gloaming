using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace ModulithFoundry.Samples.Wholesale.IntegrationFeedDemo.Tests;

internal sealed class FeedBarrier(NpgsqlConnection owner, string database) : IAsyncDisposable
{
    private const int LockKey = 182915;

    internal static async Task<FeedBarrier> HoldAsync(
        string database,
        string table,
        string operation,
        CancellationToken token
    )
    {
        // Table/operation are constants supplied by this proof suite, never external input.
        var owner = new NpgsqlConnection(database);
        await owner.OpenAsync(token);
        try
        {
            await using var command = new NpgsqlCommand(
                $"""
                SELECT pg_advisory_lock({LockKey});
                CREATE FUNCTION public.feed_proof_barrier() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    PERFORM pg_advisory_xact_lock({LockKey});
                    IF TG_OP = 'DELETE' THEN RETURN OLD; END IF;
                    RETURN NEW;
                END;
                $$;
                CREATE TRIGGER feed_proof_barrier BEFORE {operation} ON {table}
                FOR EACH ROW EXECUTE FUNCTION public.feed_proof_barrier();
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

    internal async Task WaitAsync(string lockType, int count, CancellationToken token)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(token);
        deadline.CancelAfter(TimeSpan.FromSeconds(10));
        while (
            await FeedProof.ScalarAsync(
                database,
                $"""
                SELECT count(*) FROM pg_locks locks JOIN pg_stat_activity activity ON activity.pid = locks.pid
                WHERE locks.locktype = '{lockType}' AND NOT locks.granted AND activity.datname = current_database()
                """,
                deadline.Token
            ) < count
        )
            await Task.Delay(25, deadline.Token);
    }

    internal async Task ReleaseAsync(CancellationToken token)
    {
        await using var command = new NpgsqlCommand($"SELECT pg_advisory_unlock({LockKey})", owner);
        await command.ExecuteNonQueryAsync(token);
    }

    public async ValueTask DisposeAsync() => await owner.DisposeAsync();
}

internal sealed class ReadPause(Func<string, bool> matches) : DbCommandInterceptor
{
    private readonly TaskCompletionSource reached = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private readonly TaskCompletionSource release = new(
        TaskCreationOptions.RunContinuationsAsynchronously
    );
    private int used;
    internal bool Enabled { get; set; } = true;
    internal Task Reached => reached.Task;

    internal void Release() => release.TrySetResult();

    public override async ValueTask<DbDataReader> ReaderExecutedAsync(
        DbCommand command,
        CommandExecutedEventData eventData,
        DbDataReader result,
        CancellationToken cancellationToken = default
    )
    {
        if (
            Enabled
            && matches(command.CommandText)
            && Interlocked.CompareExchange(ref used, 1, 0) == 0
        )
        {
            reached.TrySetResult();
            await release.Task.WaitAsync(cancellationToken);
        }

        return result;
    }
}

internal sealed class RejectJournalSave : SaveChangesInterceptor
{
    internal bool Enabled { get; set; }

    public override ValueTask<int> SavedChangesAsync(
        SaveChangesCompletedEventData eventData,
        int result,
        CancellationToken cancellationToken = default
    )
    {
        if (Enabled)
            throw new InvalidOperationException(
                "Proof interruption after receipts/checkpoint save and before commit."
            );

        return ValueTask.FromResult(result);
    }
}
