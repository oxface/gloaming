using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Gloaming.Messaging.Recurrence.EntityFrameworkCore.Postgres;

internal sealed class RecurrenceScope
{
    private static readonly ConditionalWeakTable<DbContext, RecurrenceScope> Scopes = new();
    private readonly List<Proposal> proposals = [];
    private DbContextId contextId;
    private IDbContextTransaction? transaction;
    internal bool Failed { get; set; }

    internal static RecurrenceScope For(DbContext database)
    {
        var scope = Scopes.GetOrCreateValue(database);
        if (scope.contextId != database.ContextId)
        {
            scope = new RecurrenceScope { contextId = database.ContextId };
            Scopes.Remove(database);
            Scopes.Add(database, scope);
        }

        return scope;
    }

    internal void RequireTransaction(DbContext database)
    {
        if (Failed || contextId != database.ContextId)
            throw new InvalidOperationException(
                "Dispose failed or replaced recurrence operation state."
            );

        var current = RecurrenceStorage.Transaction(database);
        if (transaction is not null && !ReferenceEquals(transaction, current))
            throw new InvalidOperationException(
                "Save recurrence proposals in their original native transaction."
            );

        transaction = current;
    }

    internal void Record(DbContext database, RecurringIntentRecord row, bool inserted)
    {
        database.ChangeTracker.DetectChanges();
        proposals.Add(
            new(
                row,
                inserted,
                database.Entry(row).CurrentValues.Clone(),
                inserted ? null : database.Entry(row).OriginalValues.Clone()
            )
        );
    }

    internal void Validate(DbContext database)
    {
        try
        {
            foreach (var entry in Tracked(database))
                Stored(entry, entry.GetDatabaseValues());
        }
        catch
        {
            Failed = true;
            throw;
        }
    }

    internal async Task ValidateAsync(DbContext database, CancellationToken token)
    {
        try
        {
            token.ThrowIfCancellationRequested();

            foreach (var entry in Tracked(database))
                Stored(entry, await entry.GetDatabaseValuesAsync(token));
        }
        catch
        {
            Failed = true;
            throw;
        }
    }

    private List<EntityEntry> Tracked(DbContext database)
    {
        _ = new RecurrenceStorage(database);
        database.ChangeTracker.DetectChanges();
        if (Failed)
            throw new InvalidOperationException(
                "Dispose failed recurrence operation state before recovery."
            );

        foreach (var entry in database.ChangeTracker.Entries<RecurringIntentRecord>())
            if (
                entry.State != EntityState.Unchanged
                && !proposals.Any(proposal => ReferenceEquals(proposal.Row, entry.Entity))
            )
                throw new InvalidOperationException(
                    "Generation writes must originate in recurrence operations."
                );

        List<EntityEntry> unchanged = [];
        if (proposals.Count == 0)
            return unchanged;

        RequireTransaction(database);
        foreach (var proposal in proposals)
        {
            var entry = database.Entry(proposal.Row);
            if (
                !Matches(entry.CurrentValues, proposal.Values)
                || proposal.Inserted
                    && entry.State is not (EntityState.Added or EntityState.Unchanged)
                || !proposal.Inserted
                    && entry.State is not (EntityState.Modified or EntityState.Unchanged)
            )
                throw new InvalidOperationException(
                    "Keep each exact recurrence progress proposal tracked with its finite participants."
                );

            if (
                !proposal.Inserted
                && entry.State == EntityState.Modified
                && (
                    !Matches(entry.OriginalValues, proposal.Original!)
                    || (long)entry.Property(nameof(RecurringIntentRecord.Revision)).OriginalValue!
                        != proposal.Row.Revision - 1
                    || entry.Properties.Any(property =>
                        property.IsModified
                        && property.Metadata.Name
                            is not (
                                nameof(RecurringIntentRecord.Revision)
                                or nameof(RecurringIntentRecord.Progress)
                                or nameof(RecurringIntentRecord.State)
                            )
                    )
                )
            )
                throw new InvalidOperationException(
                    "Only locked recurrence progress, lifecycle and its original revision may change."
                );

            if (entry.State == EntityState.Unchanged)
                unchanged.Add(entry);
        }

        return unchanged;
    }

    private static bool Matches(PropertyValues actual, PropertyValues expected) =>
        expected.Properties.All(property => Equals(actual[property.Name], expected[property.Name]));

    private static void Stored(EntityEntry entry, PropertyValues? stored)
    {
        if (stored is null || !Matches(stored, entry.CurrentValues))
            throw new InvalidOperationException(
                "An unchanged recurrence participant must already match stored values in the original transaction."
            );
    }

    private sealed record Proposal(
        RecurringIntentRecord Row,
        bool Inserted,
        PropertyValues Values,
        PropertyValues? Original
    );
}
