using System.Data;
using Microsoft.EntityFrameworkCore;

namespace Gloaming.Messaging.EntityFrameworkCore.Postgres;

/// <summary>Explicit PostgreSQL maintenance that expires retained processed-receipt evidence.</summary>
public static class PostgresInboxRetentionExtensions
{
    /// <summary>Delete one bounded batch of processed receipts in an exact subscription/producer lane.</summary>
    /// <remarks>
    /// Requires a fresh independent context, a nonfuture database-clock cutoff and batch size 1–1000.
    /// Pending/dead-lettered work is preserved. After deletion an old delivery can queue and execute again.
    /// Native grants/RLS apply, not EF filters. Concurrent calls can wait and return fewer rows; zero
    /// is not an exhaustion guarantee. A lost commit response can leave deletion committed.
    /// </remarks>
    public static async Task<int> DeleteProcessedPostgresInboxBatchAsync<TDbContext>(
        this TDbContext database,
        string subscriptionKey,
        string producerKey,
        DateTimeOffset processedBefore,
        int batchSize,
        CancellationToken cancellationToken = default
    )
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriptionKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(producerKey);
        if (batchSize is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(
                nameof(batchSize),
                "Use a batch size from 1 through 1000."
            );

        cancellationToken.ThrowIfCancellationRequested();
        var storage = new PostgresInboxStorage(database);
        storage.RequireFreshContext();
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.ReadCommitted,
            cancellationToken
        );
        processedBefore = processedBefore.ToUniversalTime();
        await using (var clock = storage.Command(transaction, "SELECT statement_timestamp()"))
        {
            var now = (DateTime)(await clock.ExecuteScalarAsync(cancellationToken))!;
            if (processedBefore > new DateTimeOffset(now))
                throw new ArgumentOutOfRangeException(
                    nameof(processedBefore),
                    "The cutoff must not be in the database's future."
                );
        }

        await using var command = storage.Command(
            transaction,
            $"""
            WITH candidates AS MATERIALIZED (
                SELECT subscription_key, producer_key, message_id, receipt_id FROM {storage.Table}
                WHERE subscription_key = @subscription AND producer_key = @producer
                    AND processed_at < @cutoff AND quarantined_at IS NULL
                ORDER BY processed_at, message_id LIMIT @batch
            )
            DELETE FROM {storage.Table} AS incoming USING candidates
            WHERE incoming.subscription_key = candidates.subscription_key
                AND incoming.producer_key = candidates.producer_key AND incoming.message_id = candidates.message_id
                AND incoming.receipt_id = candidates.receipt_id
                AND incoming.subscription_key = @subscription AND incoming.producer_key = @producer
                AND incoming.processed_at < @cutoff AND incoming.quarantined_at IS NULL
            """
        );
        PostgresInboxStorage.Parameter(command, "subscription", subscriptionKey);
        PostgresInboxStorage.Parameter(command, "producer", producerKey);
        PostgresInboxStorage.Parameter(command, "cutoff", processedBefore);
        PostgresInboxStorage.Parameter(command, "batch", batchSize);
        int changed = await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return changed;
    }
}
