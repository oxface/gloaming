using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;

namespace Gloaming.Messaging.EntityFrameworkCore.Postgres;

/// <summary>Retains named contributions and one technical completion/timeout notice in caller-owned native PostgreSQL transactions.</summary>
/// <typeparam name="TContext">The context owning contribution sets, finite signals, outbox and selected consumer changes.</typeparam>
public sealed class PostgresContributionSets<TContext>
    where TContext : DbContext
{
    private readonly TContext database;
    private readonly ContributionStorage storage;
    private readonly ContributionScope scope;
    private readonly PostgresDueIntents<TContext> finite;

    /// <summary>Validates the provided contribution/finite/outbox model without opening a connection.</summary>
    public PostgresContributionSets(TContext database)
    {
        ArgumentNullException.ThrowIfNull(database);

        this.database = database;
        storage = new(database);
        scope = ContributionScope.For(database);
        finite = new(database);
    }

    /// <summary>Stages one immutable expected generation and optional finite timeout. Performs no I/O, save or commit.</summary>
    /// <remarks>Registering another generation does not change the consumer's current business relevance or deactivate another set.</remarks>
    public void Register(ContributionSetKey key, ContributionSetDefinition definition)
    {
        try
        {
            ArgumentNullException.ThrowIfNull(key);
            ArgumentNullException.ThrowIfNull(definition);
            scope.RequireTransaction(database);
            Untracked(key);

            var row = ContributionSetRecord.Create(key, definition);
            database.Add(row);
            if (definition.Timeout is { } timeout)
                finite.Stage(key.TimerKey, timeout.DueAt, timeout.Signal);

            scope.Record(database, row, inserted: true);
        }
        catch
        {
            scope.Failed = true;
            throw;
        }
    }

    /// <summary>Locks a generation and compares/stages one named contribution; the selected required count stages one original completion notice.</summary>
    /// <remarks>Each generation is used once per fresh operation context. Content equality uses its complete name/version/JSON values. No ambient deadline/admission/approval policy is evaluated.</remarks>
    public async Task<ContributionResult> AddAsync(
        ContributionSetKey key,
        string contributorId,
        ContributionValue value,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            ContributionSetDefinition.Contributor(contributorId);
            ArgumentNullException.ThrowIfNull(value);

            var row = await LockAsync(key, cancellationToken);
            var definition = row.Definition();
            if (!definition.Expected.Contains(contributorId, StringComparer.Ordinal))
                throw new ArgumentException(
                    "The contributor identity is outside the retained explicit expected set.",
                    nameof(contributorId)
                );

            var received = await database
                .Set<ContributionRecord>()
                .AsNoTracking()
                .Where(content =>
                    content.ScopeKey == key.ScopeKey
                    && content.WorkId == key.WorkId
                    && content.GenerationId == key.GenerationId
                )
                .Take(101)
                .ToArrayAsync(cancellationToken);
            if (
                received.Length > definition.Expected.Count
                || received.Any(content =>
                    !definition.Expected.Contains(content.ContributorId, StringComparer.Ordinal)
                )
                || (row.State == ContributionSetState.Completed)
                    != (received.Length >= definition.Required)
            )
                throw new InvalidDataException(
                    "Stored contributions do not match the retained expected set."
                );

            foreach (var content in received)
            {
                ContributionSetDefinition.Contributor(content.ContributorId);
                _ = content.Value();
            }

            var previous = received.SingleOrDefault(content =>
                content.ContributorId == contributorId
            );
            if (previous is not null)
            {
                if (!previous.Value().Same(value))
                    throw new ContributionConflictException();

                return ContributionResult.Duplicate;
            }

            if (row.State == ContributionSetState.Completed)
                return ContributionResult.AlreadyCompleted;

            if (row.State == ContributionSetState.TimedOut)
                return ContributionResult.AlreadyTimedOut;

            var added = ContributionRecord.Create(key, contributorId, value);
            database.Add(added);
            bool complete = received.Length + 1 == definition.Required;
            row.Advance(complete ? ContributionSetState.Completed : ContributionSetState.Open);
            if (complete)
            {
                Enqueue(definition.Completion);
                if (definition.Timeout is not null)
                    await SuppressTimerAsync(key, cancellationToken);
            }

            scope.Record(database, row, inserted: false);
            scope.Record(database, added, inserted: true);
            return complete ? ContributionResult.Completed : ContributionResult.Accepted;
        }
        catch
        {
            scope.Failed = true;
            throw;
        }
    }

    /// <summary>Locks a generation and stages timeout/outcome only when the explicit UTC observation is eligible and completion has not won.</summary>
    /// <remarks>A due finite signal does not itself close the set. Arrival/timeout order is the generation-lock winner, not a FIFO or strict business-deadline promise.</remarks>
    public async Task<ContributionTimeoutResult> TimeoutAsync(
        ContributionSetKey key,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken = default
    )
    {
        try
        {
            DueIntentStorage.RequireInstant(observedAt);

            var row = await LockAsync(key, cancellationToken);
            var definition = row.Definition();
            if (definition.Timeout is not { } timeout)
                throw new InvalidOperationException(
                    "This retained contribution set has no selected timeout."
                );

            if (row.State == ContributionSetState.Completed)
                return ContributionTimeoutResult.AlreadyCompleted;

            if (row.State == ContributionSetState.TimedOut)
                return ContributionTimeoutResult.AlreadyTimedOut;

            if (observedAt < timeout.DueAt)
                return ContributionTimeoutResult.NotDue;

            row.Advance(ContributionSetState.TimedOut);
            Enqueue(timeout.Outcome);
            await SuppressTimerAsync(key, cancellationToken);
            scope.Record(database, row, inserted: false);
            return ContributionTimeoutResult.TimedOut;
        }
        catch
        {
            scope.Failed = true;
            throw;
        }
    }

    private async Task SuppressTimerAsync(ContributionSetKey key, CancellationToken token)
    {
        if (await finite.SuppressAsync(key.TimerKey, token) == DueIntentSuppression.Missing)
            throw new InvalidDataException(
                "Retain the original finite timeout signal evidence for this contribution generation."
            );
    }

    private void Enqueue(OutgoingMessage message)
    {
        new EfOutbox<TContext>(database).Enqueue(message);
        var row = database
            .ChangeTracker.Entries<OutboxMessageRecord>()
            .Single(entry => entry.Entity.MessageId == message.MessageId)
            .Entity;
        scope.Record(database, row, inserted: true);
    }

    private async Task<ContributionSetRecord> LockAsync(
        ContributionSetKey key,
        CancellationToken token
    )
    {
        ArgumentNullException.ThrowIfNull(key);
        token.ThrowIfCancellationRequested();
        scope.RequireTransaction(database);
        Untracked(key);

        string sql =
            $"SELECT * FROM {storage.SetsTable} WHERE scope_key = {{0}} AND work_id = {{1}} AND generation_id = {{2}} FOR UPDATE";
        var rows = await database
            .Set<ContributionSetRecord>()
            .FromSql(
                FormattableStringFactory.Create(sql, key.ScopeKey, key.WorkId, key.GenerationId)
            )
            .ToArrayAsync(token);
        if (rows.Length != 1)
            throw new InvalidOperationException(
                "The retained contribution generation does not exist."
            );

        return rows[0];
    }

    private void Untracked(ContributionSetKey key)
    {
        if (
            database
                .ChangeTracker.Entries<ContributionSetRecord>()
                .Any(entry =>
                    entry.Entity.ScopeKey == key.ScopeKey
                    && entry.Entity.WorkId == key.WorkId
                    && entry.Entity.GenerationId == key.GenerationId
                )
            || database
                .ChangeTracker.Entries<ContributionRecord>()
                .Any(entry =>
                    entry.Entity.ScopeKey == key.ScopeKey
                    && entry.Entity.WorkId == key.WorkId
                    && entry.Entity.GenerationId == key.GenerationId
                )
        )
            throw new InvalidOperationException(
                "Use a fresh stored observation once per complete contribution generation."
            );
    }
}
