using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Gloaming.Messaging.EntityFrameworkCore.Postgres;

internal sealed class ContributionScope
{
    private static readonly ConditionalWeakTable<DbContext, ContributionScope> Scopes = new();
    private readonly List<Proposal> proposals = [];
    private DbContextId contextId;
    private IDbContextTransaction? transaction;
    internal bool Failed { get; set; }

    internal static ContributionScope For(DbContext database)
    {
        var scope = Scopes.GetOrCreateValue(database);
        if (scope.contextId != database.ContextId)
        {
            scope = new ContributionScope { contextId = database.ContextId };
            Scopes.Remove(database);
            Scopes.Add(database, scope);
        }

        return scope;
    }

    internal void RequireTransaction(DbContext database)
    {
        if (Failed || contextId != database.ContextId)
            throw new InvalidOperationException(
                "Dispose failed or replaced contribution operation state."
            );

        var current = DueIntentStorage.RequireTransaction(database);
        if (transaction is not null && !ReferenceEquals(transaction, current))
            throw new InvalidOperationException(
                "Save contribution proposals in their original native transaction."
            );

        transaction = current;
    }

    internal void Record(DbContext database, object entity, bool inserted)
    {
        database.ChangeTracker.DetectChanges();
        proposals.Add(
            new(
                entity,
                inserted,
                database.Entry(entity).CurrentValues.Clone(),
                inserted ? null : database.Entry(entity).OriginalValues.Clone()
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
        _ = new ContributionStorage(database);
        database.ChangeTracker.DetectChanges();
        if (Failed)
            throw new InvalidOperationException(
                "Dispose failed contribution operation state before recovery."
            );

        foreach (
            var entry in database
                .ChangeTracker.Entries()
                .Where(entry => entry.Entity is ContributionSetRecord or ContributionRecord)
        )
            if (
                entry.State != EntityState.Unchanged
                && !proposals.Any(proposal => ReferenceEquals(proposal.Entity, entry.Entity))
            )
                throw new InvalidOperationException(
                    "Contribution writes must originate in the selected set operations."
                );

        List<EntityEntry> unchanged = [];
        if (proposals.Count == 0)
            return unchanged;

        RequireTransaction(database);
        foreach (var proposal in proposals)
        {
            var entry = database.Entry(proposal.Entity);
            if (
                !Matches(
                    entry,
                    entry.CurrentValues,
                    proposal.Values,
                    ignoreGenerated: proposal.Entity is OutboxMessageRecord
                )
                || proposal.Inserted
                    && entry.State is not (EntityState.Added or EntityState.Unchanged)
                || !proposal.Inserted
                    && entry.State is not (EntityState.Modified or EntityState.Unchanged)
            )
                throw new InvalidOperationException(
                    "Keep every exact contribution, generation and outgoing proposal tracked in the same native save."
                );

            if (
                !proposal.Inserted
                && entry.State == EntityState.Modified
                && (
                    !Matches(entry, entry.OriginalValues, proposal.Original!)
                    || (long)entry.Property(nameof(ContributionSetRecord.Revision)).OriginalValue!
                        != ((ContributionSetRecord)proposal.Entity).Revision - 1
                    || entry.Properties.Any(property =>
                        property.IsModified
                        && property.Metadata.Name
                            is not (
                                nameof(ContributionSetRecord.Revision)
                                or nameof(ContributionSetRecord.State)
                            )
                    )
                )
            )
                throw new InvalidOperationException(
                    "Only locked contribution-set lifecycle and its original native revision may advance."
                );

            if (entry.State == EntityState.Unchanged)
                unchanged.Add(entry);
        }

        return unchanged;
    }

    private static bool Matches(
        EntityEntry entry,
        PropertyValues actual,
        PropertyValues expected,
        bool ignoreGenerated = false
    ) =>
        expected.Properties.All(property =>
            ignoreGenerated && property.ValueGenerated != ValueGenerated.Never
            || Equal(
                entry.Metadata.FindProperty(property.Name)!,
                actual[property.Name],
                expected[property.Name]
            )
        );

    private static bool Equal(IProperty property, object? left, object? right) =>
        (left, right) switch
        {
            (JsonElement a, JsonElement b) => a.ValueKind != JsonValueKind.Undefined
                && b.ValueKind != JsonValueKind.Undefined
                && JsonElement.DeepEquals(a, b),
            (DateTimeOffset a, DateTimeOffset b) => a.EqualsExact(b),
            (string a, string b) when property.GetColumnType() == "jsonb" => JsonTextEquals(a, b),
            _ => property.GetValueComparer()?.Equals(left, right) ?? Equals(left, right),
        };

    private static bool JsonTextEquals(string left, string right)
    {
        using var actual = JsonDocument.Parse(left);
        using var expected = JsonDocument.Parse(right);
        return JsonElement.DeepEquals(actual.RootElement, expected.RootElement);
    }

    private static void Stored(EntityEntry entry, PropertyValues? stored)
    {
        if (stored is null || !Matches(entry, stored, entry.CurrentValues))
            throw new InvalidOperationException(
                "An unchanged contribution participant must already match stored values in the original transaction."
            );
    }

    private sealed record Proposal(
        object Entity,
        bool Inserted,
        PropertyValues Values,
        PropertyValues? Original
    );
}
