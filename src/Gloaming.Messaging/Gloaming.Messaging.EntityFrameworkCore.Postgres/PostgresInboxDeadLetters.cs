using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Gloaming.Messaging.EntityFrameworkCore.Postgres;

internal sealed class PostgresInboxDeadLetters<TDbContext>(TDbContext database)
    : IInboxDeadLetters<TDbContext>
    where TDbContext : DbContext
{
    private readonly PostgresInboxStorage storage = new(database);
    private static readonly Expression<Func<InboxMessageRecord, InboxDeadLetter>> Metadata =
        row => new InboxDeadLetter(
            row.SubscriptionKey,
            row.ProducerKey,
            row.MessageId,
            row.MessageName,
            row.SchemaVersion,
            row.Revision,
            row.FailureCount,
            row.FailuresSinceRedrive,
            row.ReceivedAt,
            row.DeadLetteredAt!.Value,
            row.LastFailureCode!,
            row.LastFailedAt!.Value,
            row.RedriveCount,
            row.LastRedrivenAt
        );

    public async Task<InboxDeadLetterPage> ListAsync(
        string subscriptionKey,
        string producerKey,
        InboxDeadLetterQuery query,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriptionKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(producerKey);
        ArgumentNullException.ThrowIfNull(query);
        ArgumentOutOfRangeException.ThrowIfLessThan(query.PageSize, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(query.PageSize, 100);
        if (query.FailureCode is { } code && (string.IsNullOrWhiteSpace(code) || code.Length > 128))
            throw new ArgumentException(
                "Use a nonblank failure code of at most 128 characters.",
                nameof(query)
            );

        string scope = Convert.ToHexString(
            SHA256.HashData(
                JsonSerializer.SerializeToUtf8Bytes(
                    new[] { subscriptionKey, producerKey, query.FailureCode }
                )
            )
        );
        var cursor = query.After is null ? null : DecodeCursor(query.After, scope);
        cancellationToken.ThrowIfCancellationRequested();
        storage.RequireFreshContext();

        await using var transaction = await database.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.ReadCommitted,
            cancellationToken
        );
        var rows = DeadLetters(subscriptionKey, producerKey);
        if (query.FailureCode is not null)
            rows = rows.Where(row => row.LastFailureCode == query.FailureCode);
        if (cursor is not null)
            rows = rows.Where(row =>
                EF.Functions.GreaterThan(
                    ValueTuple.Create(row.DeadLetteredAt!.Value, row.MessageId),
                    ValueTuple.Create(cursor.DeadLetteredAt, cursor.MessageId)
                )
            );

        var observed = await rows.OrderBy(row => row.DeadLetteredAt)
            .ThenBy(row => row.MessageId)
            .Select(Metadata)
            .Take(query.PageSize + 1)
            .ToArrayAsync(cancellationToken);
        var items = observed.Take(query.PageSize).ToArray();
        string? next =
            observed.Length > query.PageSize
                ? EncodeCursor(new(1, scope, items[^1].DeadLetteredAt, items[^1].MessageId))
                : null;
        await transaction.CommitAsync(cancellationToken);
        return new(items, next);
    }

    public async Task<InboxDeadLetter?> GetAsync(
        string subscriptionKey,
        string producerKey,
        Guid messageId,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriptionKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(producerKey);
        ArgumentOutOfRangeException.ThrowIfEqual(messageId, Guid.Empty);
        cancellationToken.ThrowIfCancellationRequested();
        storage.RequireFreshContext();

        await using var transaction = await database.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.ReadCommitted,
            cancellationToken
        );
        var item = await DeadLetters(subscriptionKey, producerKey)
            .Where(row => row.MessageId == messageId)
            .Select(Metadata)
            .SingleOrDefaultAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return item;
    }

    private IQueryable<InboxMessageRecord> DeadLetters(
        string subscriptionKey,
        string producerKey
    ) =>
        database
            .Set<InboxMessageRecord>()
            .AsNoTracking()
            .Where(row =>
                row.SubscriptionKey == subscriptionKey
                && row.ProducerKey == producerKey
                && row.ProcessedAt == null
                && row.DeadLetteredAt != null
            );

    private sealed record Cursor(
        int Version,
        string Scope,
        DateTimeOffset DeadLetteredAt,
        Guid MessageId
    );

    private static string EncodeCursor(Cursor cursor) =>
        Convert
            .ToBase64String(JsonSerializer.SerializeToUtf8Bytes(cursor))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static Cursor DecodeCursor(string token, string scope)
    {
        if (string.IsNullOrWhiteSpace(token) || token.Length > 1024)
            throw new ArgumentException(
                "Use a continuation from this exact dead-letter query.",
                nameof(token)
            );

        try
        {
            string base64 = token.Replace('-', '+').Replace('_', '/');
            base64 = base64.PadRight((base64.Length + 3) / 4 * 4, '=');
            var cursor = JsonSerializer.Deserialize<Cursor>(Convert.FromBase64String(base64));
            if (
                cursor is null
                || cursor.Version != 1
                || cursor.Scope != scope
                || cursor.MessageId == Guid.Empty
                || cursor.DeadLetteredAt.Offset != TimeSpan.Zero
            )
                throw new FormatException(
                    "The continuation has an invalid version, scope or position."
                );

            return cursor;
        }
        catch (Exception failure) when (failure is FormatException or JsonException)
        {
            throw new ArgumentException(
                "Use a continuation from this exact dead-letter query.",
                nameof(token),
                failure
            );
        }
    }

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
