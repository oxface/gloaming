using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Gloaming.Messaging.EntityFrameworkCore.Postgres;

// Evidence belongs to the exact context lease and native transaction, not to a durable key.
internal sealed class DueIntentScope
{
    private static readonly ConditionalWeakTable<DbContext, DueIntentScope> Scopes = new();
    private readonly List<Proposal> proposals = [];
    private DbContextId contextId;
    private IDbContextTransaction? transaction;
    internal bool Failed { get; set; }

    internal static DueIntentScope For(DbContext database)
    {
        var scope = Scopes.GetOrCreateValue(database);
        if (scope.contextId != database.ContextId)
        {
            scope = new DueIntentScope { contextId = database.ContextId };
            Scopes.Remove(database);
            Scopes.Add(database, scope);
        }

        return scope;
    }

    internal void RequireTransaction(DbContext database)
    {
        if (Failed || contextId != database.ContextId)
            throw new InvalidOperationException(
                "Dispose the failed or replaced due-intent operation scope."
            );

        var current = DueIntentStorage.RequireTransaction(database);
        if (transaction is not null && !ReferenceEquals(transaction, current))
            throw new InvalidOperationException(
                "Save due-intent proposals in their original native transaction."
            );

        transaction = current;
    }

    internal void Record(
        DbContext database,
        DueIntentRecord row,
        bool inserted,
        OutboxMessageRecord? outgoing = null
    )
    {
        database.ChangeTracker.DetectChanges();
        proposals.Add(
            new(
                row,
                inserted,
                database.Entry(row).CurrentValues.Clone(),
                outgoing,
                outgoing is null ? null : database.Entry(outgoing).CurrentValues.Clone()
            )
        );
    }

    internal void Validate(DbContext database, bool includeOutbox = true)
    {
        try
        {
            foreach (var entry in ValidateTracked(database, includeOutbox))
                RequireStored(entry, entry.GetDatabaseValues());
        }
        catch
        {
            Failed = true;
            throw;
        }
    }

    internal async Task ValidateAsync(
        DbContext database,
        CancellationToken token,
        bool includeOutbox = true
    )
    {
        try
        {
            token.ThrowIfCancellationRequested();

            foreach (var entry in ValidateTracked(database, includeOutbox))
                RequireStored(entry, await entry.GetDatabaseValuesAsync(token));
        }
        catch
        {
            Failed = true;
            throw;
        }
    }

    private List<EntityEntry> ValidateTracked(DbContext database, bool includeOutbox)
    {
        _ = new DueIntentStorage(database);
        database.ChangeTracker.DetectChanges();
        if (includeOutbox)
            database.ValidateOutboxChanges();

        if (Failed)
            throw new InvalidOperationException(
                "Dispose failed due-intent operation state before recovery."
            );

        foreach (var entry in database.ChangeTracker.Entries<DueIntentRecord>())
        {
            if (
                entry.State != EntityState.Unchanged
                && !proposals.Any(proposal => ReferenceEquals(proposal.Row, entry.Entity))
            )
                throw new InvalidOperationException(
                    "Occurrence writes must originate in the selected due-intent operations."
                );
        }

        List<EntityEntry> unchanged = [];
        if (proposals.Count == 0)
            return unchanged;

        RequireTransaction(database);
        foreach (var proposal in proposals)
        {
            var entry = database.Entry(proposal.Row);
            if (
                !Matches(entry, entry.CurrentValues, proposal.Values)
                || (
                    proposal.Inserted
                    && entry.State is not (EntityState.Added or EntityState.Unchanged)
                )
                || (
                    !proposal.Inserted
                    && entry.State is not (EntityState.Modified or EntityState.Unchanged)
                )
            )
                throw new InvalidOperationException(
                    "Keep each immutable occurrence proposal tracked with its original values."
                );

            if (
                !proposal.Inserted
                && entry.State == EntityState.Modified
                && (
                    entry.Property(nameof(DueIntentRecord.State)).OriginalValue
                        is not DueIntentState.Pending
                    || entry.Properties.Any(property =>
                        property.IsModified
                        && property.Metadata.Name != nameof(DueIntentRecord.State)
                    )
                )
            )
                throw new InvalidOperationException(
                    "Only the locked pending occurrence may change its lifecycle."
                );

            if (entry.State == EntityState.Unchanged)
                unchanged.Add(entry);

            if (proposal.Outgoing is not null)
            {
                var outgoing = database.Entry(proposal.Outgoing);
                if (
                    outgoing.State is not (EntityState.Added or EntityState.Unchanged)
                    || !Matches(
                        outgoing,
                        outgoing.CurrentValues,
                        proposal.OutgoingValues!,
                        ignoreGenerated: true
                    )
                )
                    throw new InvalidOperationException(
                        "Keep the original released outbox proposal tracked with its occurrence."
                    );

                if (outgoing.State == EntityState.Unchanged)
                    unchanged.Add(outgoing);
            }
        }

        return unchanged;
    }

    private static void RequireStored(EntityEntry entry, PropertyValues? stored)
    {
        // Unchanged may be genuine saved evidence, premature acceptance, or a rolled-back savepoint.
        if (stored is null || !Matches(entry, stored, entry.CurrentValues))
            throw new InvalidOperationException(
                "An unchanged due-intent handoff participant must already match native stored values in the original transaction."
            );
    }

    private static bool Matches(
        EntityEntry entry,
        PropertyValues actual,
        PropertyValues expected,
        bool ignoreGenerated = false
    ) =>
        expected.Properties.All(property =>
            (ignoreGenerated && property.ValueGenerated != ValueGenerated.Never)
            || Equal(
                entry.Metadata.FindProperty(property.Name)!,
                actual[property.Name],
                expected[property.Name]
            )
        );

    private static bool Equal(IProperty property, object? actual, object? expected) =>
        (actual, expected) switch
        {
            (JsonElement left, JsonElement right) => left.ValueKind != JsonValueKind.Undefined
                && right.ValueKind != JsonValueKind.Undefined
                && JsonElement.DeepEquals(left, right),
            (DateTimeOffset left, DateTimeOffset right) => left.EqualsExact(right),
            _ => property.GetValueComparer()?.Equals(actual, expected) ?? Equals(actual, expected),
        };

    private sealed record Proposal(
        DueIntentRecord Row,
        bool Inserted,
        PropertyValues Values,
        OutboxMessageRecord? Outgoing,
        PropertyValues? OutgoingValues
    );
}
