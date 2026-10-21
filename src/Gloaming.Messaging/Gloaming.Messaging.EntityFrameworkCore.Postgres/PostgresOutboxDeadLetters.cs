using System.Linq.Expressions;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Gloaming.Messaging.EntityFrameworkCore.Postgres;

internal sealed class PostgresOutboxDeadLetters<TDbContext>(TDbContext database)
    : IOutboxDeadLetters<TDbContext>
    where TDbContext : DbContext
{
    private readonly PostgresOutboxStorage storage = new(database);
    private static readonly Expression<Func<OutboxMessageRecord, OutboxDeadLetter>> Metadata =
        row => new OutboxDeadLetter(
            row.MessageId,
            row.RouteKey,
            row.MessageName,
            row.SchemaVersion,
            row.Revision,
            row.Attempts,
            row.FailureCount,
            row.FailuresSinceRedrive,
            row.QueuedAt,
            row.DeadLetteredAt!.Value,
            row.LastFailureCode!,
            row.LastFailedAt!.Value,
            row.RedriveCount,
            row.LastRedrivenAt
        );

    public async Task<OutboxDeadLetterPage> ListAsync(
        string routeKey,
        OutboxDeadLetterQuery query,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeKey);
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
                    new[]
                    {
                        "outbox",
                        storage.Schema,
                        storage.TableName,
                        routeKey,
                        query.FailureCode,
                    }
                )
            )
        );
        var cursor = query.After is null ? null : DeadLetterCursor.Decode(query.After, scope);
        cancellationToken.ThrowIfCancellationRequested();
        storage.RequireFreshContext();

        await using var transaction = await database.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.ReadCommitted,
            cancellationToken
        );
        var rows = DeadLetters(routeKey);
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
                ? new DeadLetterCursor(
                    1,
                    scope,
                    items[^1].DeadLetteredAt,
                    items[^1].MessageId
                ).Encode()
                : null;
        await transaction.CommitAsync(cancellationToken);
        return new(items, next);
    }

    public async Task<OutboxDeadLetter?> GetAsync(
        string routeKey,
        Guid messageId,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeKey);
        ArgumentOutOfRangeException.ThrowIfEqual(messageId, Guid.Empty);
        cancellationToken.ThrowIfCancellationRequested();
        storage.RequireFreshContext();

        await using var transaction = await database.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.ReadCommitted,
            cancellationToken
        );
        var item = await DeadLetters(routeKey)
            .Where(row => row.MessageId == messageId)
            .Select(Metadata)
            .SingleOrDefaultAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return item;
    }

    private IQueryable<OutboxMessageRecord> DeadLetters(string routeKey) =>
        database
            .Set<OutboxMessageRecord>()
            .AsNoTracking()
            .Where(row =>
                row.RouteKey == routeKey && row.DispatchedAt == null && row.DeadLetteredAt != null
            );

    public async Task<OutboxRetryResult> RetryAsync(
        string routeKey,
        Guid messageId,
        long expectedRevision,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(routeKey);
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
            SET dead_lettered_at = NULL, available_at = clock_timestamp(), failures_since_redrive = 0,
                revision = revision + 1, redrive_count = redrive_count + 1, last_redriven_at = clock_timestamp()
            WHERE destination = @route AND message_id = @id AND revision = @revision
                AND dispatched_at IS NULL AND dead_lettered_at IS NOT NULL
                AND lease_token IS NULL AND lease_until IS NULL
            """
        );
        PostgresOutboxStorage.Parameter(command, "route", routeKey);
        PostgresOutboxStorage.Parameter(command, "id", messageId);
        PostgresOutboxStorage.Parameter(command, "revision", expectedRevision);
        int changed = await command.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return changed == 1 ? OutboxRetryResult.Retried : OutboxRetryResult.NotApplied;
    }
}
