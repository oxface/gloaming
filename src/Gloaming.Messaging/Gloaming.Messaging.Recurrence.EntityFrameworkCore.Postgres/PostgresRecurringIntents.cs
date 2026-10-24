using System.Runtime.CompilerServices;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;

namespace Gloaming.Messaging.Recurrence.EntityFrameworkCore.Postgres;

/// <summary>Retains versioned recurrence progress and stages finite occurrences in the caller's native transaction.</summary>
/// <typeparam name="TContext">The context owning recurrence, finite intents, outbox and any selected consumer changes.</typeparam>
public sealed class PostgresRecurringIntents<TContext>
    where TContext : DbContext
{
    private readonly TContext database;
    private readonly RecurrenceStorage storage;
    private readonly RecurrenceScope scope;
    private readonly PostgresDueIntents<TContext> finite;

    /// <summary>Validates the fixed recurrence/finite/outbox model without opening a connection.</summary>
    public PostgresRecurringIntents(TContext database)
    {
        ArgumentNullException.ThrowIfNull(database);

        this.database = database;
        storage = new(database);
        scope = RecurrenceScope.For(database);
        finite = new(database);
    }

    /// <summary>Stages a new retained generation. Performs no I/O, save, commit or initial planning. Identities cannot be reused.</summary>
    /// <remarks>Consumers stop a superseded generation and select current relevance in their own business row. Other generations do not implicitly deactivate it.</remarks>
    public void Register(RecurringIntentKey key, RecurrenceDefinition definition)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(definition);
            scope.RequireTransaction(database);
            Untracked(key);

            var row = RecurringIntentRecord.Create(key, definition);
            database.Add(row);
            scope.Record(database, row, inserted: true);
        }
        catch
        {
            scope.Failed = true;
            throw;
        }
    }

    /// <summary>Locks one generation, checks its exact definition and stages bounded progress plus immutable finite occurrences.</summary>
    /// <remarks>Use each generation once per fresh operation context. observedAt is explicit UTC; regression fails closed. The envelope factory must be pure and return the supplied delivery ID. No save or commit is performed.</remarks>
    public async Task<RecurrencePlan> PlanAsync(
        RecurringIntentKey key,
        RecurrenceDefinition definition,
        DateTimeOffset observedAt,
        int maximumExamined,
        Func<RecurringOccurrence, OutgoingMessage> envelope,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            ArgumentNullException.ThrowIfNull(definition);
            ArgumentNullException.ThrowIfNull(envelope);
            RecurrenceDefinition.RequireInstant(observedAt);
            ArgumentOutOfRangeException.ThrowIfLessThan(maximumExamined, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(maximumExamined, 4096);

            var row = await LockAsync(key, cancellationToken);
            if (!string.Equals(row.Definition, definition.Descriptor, StringComparison.Ordinal))
                throw new InvalidOperationException(
                    "The retained recurrence definition, evaluator, runtime or rule version differs. Stop it and explicitly select a new generation."
                );

            var cursor = row.Cursor();
            if (cursor.Observed is { } previous && observedAt < previous)
                throw new InvalidOperationException(
                    "The recurrence clock regressed. Wait for the retained observation or explicitly replace the generation."
                );

            if (row.State != RecurringIntentState.Active)
                return new(0, 0, false);

            int staged = 0;
            int examined = 0;
            cursor = cursor with { Observed = observedAt };
            if (cursor.Armed is { } armed && armed <= observedAt)
                cursor = cursor with { Armed = null };

            if (cursor.Armed is null)
            {
                if (
                    definition.Missed.Kind == 1
                    && cursor.Next is { } skipped
                    && skipped <= observedAt
                )
                    cursor = cursor with { Next = definition.Next(observedAt) };

                if (definition.Missed.Kind == 2)
                {
                    if (cursor.Window is null && cursor.Next is { } first && first <= observedAt)
                        cursor = cursor with { Window = observedAt };

                    if (cursor.Window is { } cutoff)
                    {
                        while (
                            cursor.Next is { } next && next <= cutoff && examined < maximumExamined
                        )
                        {
                            cancellationToken.ThrowIfCancellationRequested();
                            examined++;
                            cursor = cursor with { Candidate = next, Next = definition.Next(next) };
                        }

                        if (cursor.Next is null || cursor.Next > cutoff)
                        {
                            if (cursor.Candidate is { } candidate)
                                Stage(candidate);

                            cursor = cursor with { Window = null, Candidate = null };
                        }
                    }
                }
                else if (definition.Missed.Kind == 3)
                {
                    while (
                        cursor.Next is { } next
                        && next <= observedAt
                        && staged < definition.Missed.Maximum
                        && examined < maximumExamined
                    )
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        examined++;
                        Stage(next);
                        cursor = cursor with { Next = definition.Next(next) };
                    }
                }

                // One future occurrence is armed only after this backlog window completes.
                // Repeated early polls preserve that occurrence instead of walking into the future.
                if (
                    cursor.Window is null
                    && cursor.Next is { } future
                    && future > observedAt
                    && staged < definition.Missed.Maximum
                )
                {
                    Stage(future);
                    cursor = cursor with { Armed = future, Next = definition.Next(future) };
                }
            }

            row.Advance(cursor, RecurringIntentState.Active);
            scope.Record(database, row, inserted: false);
            return new(
                staged,
                examined,
                cursor.Window is not null || cursor.Armed is null && cursor.Next is not null
            );

            void Stage(DateTimeOffset dueAt)
            {
                var occurrence = new RecurringOccurrence(
                    key.Identity("delivery", dueAt),
                    key.Identity("occurrence", dueAt),
                    dueAt
                );
                var message = envelope(occurrence);
                if (message is null || message.MessageId != occurrence.MessageId)
                    throw new InvalidOperationException(
                        "The pure envelope factory must retain the supplied recurring delivery identity."
                    );

                finite.Stage(
                    new(key.ScopeKey, key.Identity("finite-schedule"), occurrence.OccurrenceId),
                    dueAt,
                    message
                );
                staged++;
            }
        }
        catch
        {
            scope.Failed = true;
            throw;
        }
    }

    /// <summary>Disables planning and suppresses at most 1..100 pending finite occurrences. Repeat with fresh contexts until MoreWork is false.</summary>
    /// <remarks>Generation and occurrence proposals commit together. Released work is irrevocable; the receiver checks current consumer-owned generation/relevance.</remarks>
    public async Task<RecurrenceStop> StopAsync(
        RecurringIntentKey key,
        int maximum,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(maximum, 1);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(maximum, 100);

            var row = await LockAsync(key, cancellationToken);
            if (row.State == RecurringIntentState.Stopped)
                return new(0, false);

            Guid schedule = key.Identity("finite-schedule");
            var pending = await database
                .Set<DueIntentRecord>()
                .AsNoTracking()
                .Where(value =>
                    value.ScopeKey == key.ScopeKey
                    && value.ScheduleId == schedule
                    && value.State == DueIntentState.Pending
                )
                .OrderBy(value => value.DueAt)
                .ThenBy(value => value.OccurrenceId)
                .Select(value => value.OccurrenceId)
                .Take(maximum + 1)
                .ToArrayAsync(cancellationToken);
            int suppressed = 0;
            foreach (var occurrence in pending.Take(maximum))
                if (
                    await finite.SuppressAsync(
                        new(key.ScopeKey, schedule, occurrence),
                        cancellationToken
                    ) == DueIntentSuppression.Suppressed
                )
                    suppressed++;

            bool more = pending.Length > maximum;
            row.Stop(more ? RecurringIntentState.Stopping : RecurringIntentState.Stopped);
            scope.Record(database, row, inserted: false);
            return new(suppressed, more);
        }
        catch
        {
            scope.Failed = true;
            throw;
        }
    }

    private async Task<RecurringIntentRecord> LockAsync(
        RecurringIntentKey key,
        CancellationToken token
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        token.ThrowIfCancellationRequested();
        scope.RequireTransaction(database);
        Untracked(key);

        string sql =
            $"SELECT * FROM {storage.Table} WHERE scope_key = {{0}} AND schedule_id = {{1}} AND generation_id = {{2}} FOR UPDATE";
        var rows = await database
            .Set<RecurringIntentRecord>()
            .FromSql(
                FormattableStringFactory.Create(sql, key.ScopeKey, key.ScheduleId, key.GenerationId)
            )
            .ToArrayAsync(token);
        if (rows.Length != 1)
            throw new InvalidOperationException(
                "The retained recurrence generation does not exist."
            );

        if (rows[0].Revision < 1 || !Enum.IsDefined(rows[0].State))
            throw new InvalidDataException("Invalid retained recurrence lifecycle or revision.");

        return rows[0];
    }

    private void Untracked(RecurringIntentKey key)
    {
        if (
            database
                .ChangeTracker.Entries<RecurringIntentRecord>()
                .Any(entry =>
                    entry.Entity.ScopeKey == key.ScopeKey
                    && entry.Entity.ScheduleId == key.ScheduleId
                    && entry.Entity.GenerationId == key.GenerationId
                )
        )
            throw new InvalidOperationException(
                "Use a fresh stored observation once per complete recurrence generation."
            );
    }
}
