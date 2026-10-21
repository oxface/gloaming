using Microsoft.EntityFrameworkCore;

namespace Gloaming.Messaging.EntityFrameworkCore.Postgres;

internal sealed class PostgresInboxRecovery<TDbContext>(TDbContext database)
    : IInboxRecovery<TDbContext>
    where TDbContext : DbContext
{
    private readonly PostgresInboxStorage storage = new(database);

    public async Task<InboxRetryResult> RetryAsync(
        string subscriptionKey,
        string producerKey,
        Guid messageId,
        long expectedRevision,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriptionKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(producerKey);
        ArgumentOutOfRangeException.ThrowIfEqual(messageId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfNegative(expectedRevision);
        cancellationToken.ThrowIfCancellationRequested();

        storage.RequireFreshContext();
        await using var transaction = await database.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.ReadCommitted,
            cancellationToken
        );
        await using var command = storage.Command(
            transaction,
            $"""
            UPDATE {storage.Table}
            SET quarantined_at = NULL, available_at = clock_timestamp(), failures_since_redrive = 0,
                revision = revision + 1, redrive_count = redrive_count + 1, last_redriven_at = clock_timestamp()
            WHERE subscription_key = @subscription AND producer_key = @producer AND message_id = @id
                AND revision = @revision AND processed_at IS NULL AND quarantined_at IS NOT NULL
            """
        );
        PostgresInboxStorage.Parameter(command, "subscription", subscriptionKey);
        PostgresInboxStorage.Parameter(command, "producer", producerKey);
        PostgresInboxStorage.Parameter(command, "id", messageId);
        PostgresInboxStorage.Parameter(command, "revision", expectedRevision);
        int changed = await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return changed == 1 ? InboxRetryResult.Retried : InboxRetryResult.NotApplied;
    }
}
