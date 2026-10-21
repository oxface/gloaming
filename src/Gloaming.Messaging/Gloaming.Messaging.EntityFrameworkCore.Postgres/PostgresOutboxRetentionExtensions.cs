using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Gloaming.Messaging.EntityFrameworkCore.Postgres;

/// <summary>Explicitly authorized PostgreSQL maintenance for retained completed outbox work.</summary>
public static class PostgresOutboxRetentionExtensions
{
    /// <summary>Delete at most one bounded batch of completed work in an exact admitted route.</summary>
    /// <remarks>
    /// Requires a fresh independent context and batch size 1–1000. The cutoff must not be in the
    /// database's future. Native grants/RLS apply, not EF filters. Concurrent cleaners can wait
    /// and return fewer rows; zero is not an exhaustion guarantee. A missing commit response may
    /// leave deletion committed. Repetition can remove another batch and is consumer policy.
    /// </remarks>
    public static async Task<int> DeleteCompletedPostgresOutboxBatchAsync<TDbContext>(
        this TDbContext database,
        string routeKey,
        DateTimeOffset completedBefore,
        int batchSize,
        CancellationToken cancellationToken = default
    )
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(routeKey);
        if (batchSize is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(
                nameof(batchSize),
                "Use a batch size from 1 through 1000."
            );

        cancellationToken.ThrowIfCancellationRequested();
        var storage = new PostgresOutboxStorage(database);
        storage.RequireFreshContext("Outbox retention operations");
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken
        );
        completedBefore = completedBefore.ToUniversalTime();
        await using (var clock = storage.Command(transaction, "SELECT statement_timestamp()"))
        {
            var now = (DateTime)(await clock.ExecuteScalarAsync(cancellationToken))!;
            if (completedBefore > new DateTimeOffset(now))
                throw new ArgumentOutOfRangeException(
                    nameof(completedBefore),
                    "The cutoff must not be in the database's future."
                );
        }

        await using var command = storage.Command(
            transaction,
            $"""
            WITH candidates AS MATERIALIZED (
                SELECT message_id FROM {storage.Table}
                WHERE destination = @route AND dispatched_at < @cutoff
                    AND dead_lettered_at IS NULL AND lease_token IS NULL AND lease_until IS NULL
                ORDER BY dispatched_at, message_id LIMIT @batch
            )
            DELETE FROM {storage.Table} AS outgoing USING candidates
            WHERE outgoing.message_id = candidates.message_id AND outgoing.destination = @route
                AND outgoing.dispatched_at < @cutoff AND outgoing.dead_lettered_at IS NULL
                AND outgoing.lease_token IS NULL AND outgoing.lease_until IS NULL
            """
        );
        PostgresOutboxStorage.Parameter(command, "route", routeKey);
        PostgresOutboxStorage.Parameter(command, "cutoff", completedBefore);
        PostgresOutboxStorage.Parameter(command, "batch", batchSize);
        int changed = await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return changed;
    }
}
