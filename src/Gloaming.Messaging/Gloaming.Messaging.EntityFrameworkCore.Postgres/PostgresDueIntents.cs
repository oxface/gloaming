using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

namespace Gloaming.Messaging.EntityFrameworkCore.Postgres;

/// <summary>Stages finite occurrences, suppression and bounded locked outbox handoff in caller-owned native transactions.</summary>
/// <typeparam name="TContext">The context owning both occurrences and the outbox, together with selected business changes.</typeparam>
public sealed class PostgresDueIntents<TContext>
    where TContext : DbContext
{
    private readonly TContext database;
    private readonly DueIntentStorage storage;
    private readonly DueIntentScope scope;

    /// <summary>Validates the fixed PostgreSQL occurrence/outbox model without opening a connection.</summary>
    public PostgresDueIntents(TContext database)
    {
        ArgumentNullException.ThrowIfNull(database);

        this.database = database;
        storage = new(database);
        scope = DueIntentScope.For(database);
    }

    /// <summary>Stages one immutable occurrence; performs no I/O, save or commit. A retained key or delivery ID cannot be reused.</summary>
    /// <remarks>Payload objects require unique property names recursively; ambiguous JSON is rejected before native staging.</remarks>
    public void Stage(DueIntentKey key, DateTimeOffset dueAt, OutgoingMessage message)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(message);
            DueIntentStorage.RequireInstant(dueAt);
            PostgresJsonValues.RequireUniqueProperties(message.Payload);
            scope.RequireTransaction(database);
            RequireUntracked(key);

            var row = DueIntentRecord.Create(key, dueAt, message);
            database.Add(row);
            scope.Record(database, row, inserted: true);
        }
        catch
        {
            scope.Failed = true;
            throw;
        }
    }

    /// <summary>Locks one stored occurrence and stages Pending to Cancelled. Results are proposals until commit; released work cannot be recalled.</summary>
    /// <remarks>Use each complete key once per fresh operation context. A matching tracked occurrence, including an unsaved Stage, is rejected.</remarks>
    public async Task<DueIntentSuppression> SuppressAsync(
        DueIntentKey key,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            ArgumentNullException.ThrowIfNull(key);
            cancellationToken.ThrowIfCancellationRequested();
            scope.RequireTransaction(database);
            RequireUntracked(key);

            string sql =
                $"SELECT * FROM {storage.Table} WHERE scope_key = {{0}} AND schedule_id = {{1}} AND occurrence_id = {{2}} FOR UPDATE";
            var rows = await database
                .Set<DueIntentRecord>()
                .FromSql(
                    FormattableStringFactory.Create(
                        sql,
                        key.ScopeKey,
                        key.ScheduleId,
                        key.OccurrenceId
                    )
                )
                .ToArrayAsync(cancellationToken);
            if (rows.Length == 0)
                return DueIntentSuppression.Missing;

            var row = rows[0];
            DueIntentStorage.ValidateStored(row);
            if (row.State == DueIntentState.Released)
                return DueIntentSuppression.AlreadyReleased;

            if (row.State == DueIntentState.Cancelled)
                return DueIntentSuppression.AlreadyCancelled;

            row.Transition(DueIntentState.Cancelled);
            scope.Record(database, row, inserted: false);
            return DueIntentSuppression.Suppressed;
        }
        catch
        {
            scope.Failed = true;
            throw;
        }
    }

    /// <summary>Locks at most 1..100 eligible occurrences and stages their original envelopes in the same outbox. The caller saves and commits both.</summary>
    /// <remarks>Cutoff is explicit UTC at microsecond precision. SKIP LOCKED gives no FIFO, fairness or empty-backlog guarantee. No publisher executes here.</remarks>
    public async Task<IReadOnlyList<Guid>> ReleaseAsync(
        string scopeKey,
        DateTimeOffset cutoff,
        int maximum,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(scopeKey);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(scopeKey.Length, 256);
            ArgumentOutOfRangeException.ThrowIfLessThan(maximum, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(maximum, 100);
            cancellationToken.ThrowIfCancellationRequested();
            DueIntentStorage.RequireInstant(cutoff);
            scope.RequireTransaction(database);
            if (
                database
                    .ChangeTracker.Entries<DueIntentRecord>()
                    .Any(entry => entry.Entity.ScopeKey == scopeKey)
            )
                throw new InvalidOperationException(
                    "Release each scope in a fresh context without tracked occurrences for that scope."
                );

            string sql =
                $"SELECT * FROM {storage.Table} WHERE scope_key = {{0}} AND state = 1 AND due_at <= {{1}} ORDER BY due_at, schedule_id, occurrence_id LIMIT {{2}} FOR UPDATE SKIP LOCKED";
            var rows = await database
                .Set<DueIntentRecord>()
                .FromSql(FormattableStringFactory.Create(sql, scopeKey, cutoff, maximum))
                .ToArrayAsync(cancellationToken);
            var outbox = new EfOutbox<TContext>(database);
            foreach (var row in rows)
            {
                cancellationToken.ThrowIfCancellationRequested();
                DueIntentStorage.ValidateStored(row);
                outbox.Enqueue(row.Message());
                var outgoing = database
                    .ChangeTracker.Entries<OutboxMessageRecord>()
                    .Single(entry => entry.Entity.MessageId == row.MessageId)
                    .Entity;
                row.Transition(DueIntentState.Released);
                scope.Record(database, row, inserted: false, outgoing);
            }

            return rows.Select(row => row.MessageId).ToArray();
        }
        catch
        {
            scope.Failed = true;
            throw;
        }
    }

    private void RequireUntracked(DueIntentKey key)
    {
        if (
            database
                .ChangeTracker.Entries<DueIntentRecord>()
                .Any(entry =>
                    entry.Entity.ScopeKey == key.ScopeKey
                    && entry.Entity.ScheduleId == key.ScheduleId
                    && entry.Entity.OccurrenceId == key.OccurrenceId
                )
        )
            throw new InvalidOperationException(
                "Use a fresh stored observation once per complete occurrence key."
            );
    }
}
