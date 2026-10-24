using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace Gloaming.EventSourcing.EntityFrameworkCore;

// Only provided replay may replace inline state without appending facts. Keep its native
// association, immutable proposal and stored participation together; consumers still own commit.
internal static class MaintenanceWriteRegistry
{
    private static readonly ConditionalWeakTable<DbContext, MaintenanceWrite> Writes = new();

    internal static bool HasWrite(DbContext database) => Writes.TryGetValue(database, out _);

    internal static void Record(
        DbContext database,
        IEventStreamRecord header,
        IInlineStateRecord state,
        IDbContextTransaction transaction
    )
    {
        database.ChangeTracker.DetectChanges();
        Writes.Add(database, new(database.Entry(header), database.Entry(state), transaction));
    }

    internal static async Task RequireAsync(
        DbContext database,
        Guid id,
        IDbContextTransaction transaction,
        CancellationToken cancellationToken
    )
    {
        if (
            !Writes.TryGetValue(database, out var repair)
            || !ReferenceEquals(repair.Transaction, transaction)
            || repair.Header.Entity is not IEventStreamRecord header
            || header.Id != id
        )
            throw new InvalidOperationException(
                "The provided rebuilder must stage this target in the selected native context/transaction."
            );

        database.ChangeTracker.DetectChanges();
        await ValidateAsync(
            database,
            database.ChangeTracker.Entries().ToArray(),
            cancellationToken
        );
    }

    internal static bool IsConflict(
        DbContext database,
        IDbContextTransaction transaction,
        DbUpdateConcurrencyException failure
    ) =>
        Writes.TryGetValue(database, out var repair)
        && ReferenceEquals(repair.Transaction, transaction)
        && failure.Entries.Count > 0
        && failure.Entries.All(entry =>
            ReferenceEquals(entry.Context, database)
            && (
                ReferenceEquals(entry.Entity, repair.Header.Entity)
                || ReferenceEquals(entry.Entity, repair.State.Entity)
            )
        );

    internal static bool Validate(DbContext database, EntityEntry[] entries)
    {
        var repair = ValidateTrackedChanges(database, entries);
        if (repair is null)
            return false;

        foreach (var entry in entries.Where(entry => entry.State == EntityState.Unchanged))
            ValidateValues(entry.GetDatabaseValues(), repair.Values(entry));

        return true;
    }

    internal static async Task<bool> ValidateAsync(
        DbContext database,
        EntityEntry[] entries,
        CancellationToken cancellationToken
    )
    {
        cancellationToken.ThrowIfCancellationRequested();

        var repair = ValidateTrackedChanges(database, entries);
        if (repair is null)
            return false;

        foreach (var entry in entries.Where(entry => entry.State == EntityState.Unchanged))
            ValidateValues(
                await entry.GetDatabaseValuesAsync(cancellationToken),
                repair.Values(entry)
            );

        return true;
    }

    private static MaintenanceWrite? ValidateTrackedChanges(
        DbContext database,
        EntityEntry[] entries
    )
    {
        if (!Writes.TryGetValue(database, out var repair))
            return null;

        if (
            !ReferenceEquals(repair.Transaction, database.Database.CurrentTransaction)
            || entries.Length != 2
            || !entries.Any(entry => ReferenceEquals(entry.Entity, repair.Header.Entity))
            || !entries.Any(entry => ReferenceEquals(entry.Entity, repair.State.Entity))
        )
            throw new InvalidOperationException(
                "Save the exact maintenance header/state in its original transaction."
            );

        foreach (var entry in entries)
        {
            if (
                entry.State
                is not (EntityState.Added or EntityState.Modified or EntityState.Unchanged)
            )
                throw new InvalidOperationException(
                    "The maintenance replacement must remain tracked."
                );

            ValidateValues(entry.CurrentValues, repair.Values(entry));
        }

        foreach (var entry in entries.Where(entry => entry.State == EntityState.Modified))
            ValidateValues(entry.OriginalValues, repair.OriginalValues(entry));

        if (
            repair.Header.State == EntityState.Modified
            && repair.Header.Properties.Any(property =>
                property.IsModified
                && property.Metadata.Name != nameof(IEventStreamRecord.ConcurrencyStamp)
            )
        )
            throw new InvalidOperationException(
                "Repair changes only the concurrency stamp, preserving its observed original values."
            );

        return repair;
    }

    private static void ValidateValues(PropertyValues? actual, PropertyValues expected)
    {
        // Unchanged may be a healthy observed row, an acknowledged save, premature acceptance,
        // or a save undone by a savepoint. Only native stored values establish participation.
        if (
            actual is null
            || expected.Properties.Any(property =>
                !Equal(property, actual[property], expected[property])
            )
        )
            throw new InvalidOperationException(
                "Maintenance participants must retain their reconstructed values; unchanged participants must already be stored in the original transaction."
            );
    }

    private static bool Equal(IProperty property, object? actual, object? expected) =>
        expected is DateTimeOffset time
            ? actual is DateTimeOffset currentTime && time.EqualsExact(currentTime)
        : expected is JsonElement json
            ? actual is JsonElement currentJson && JsonElement.DeepEquals(json, currentJson)
        : property.GetValueComparer()?.Equals(actual, expected) ?? Equals(actual, expected);

    private sealed class MaintenanceWrite(
        EntityEntry header,
        EntityEntry state,
        IDbContextTransaction transaction
    )
    {
        internal EntityEntry Header { get; } = header;
        internal EntityEntry State { get; } = state;
        private PropertyValues HeaderValues { get; } = header.CurrentValues.Clone();
        private PropertyValues StateValues { get; } = state.CurrentValues.Clone();
        private PropertyValues HeaderOriginalValues { get; } = header.OriginalValues.Clone();
        private PropertyValues StateOriginalValues { get; } = state.OriginalValues.Clone();
        internal IDbContextTransaction Transaction { get; } = transaction;

        internal PropertyValues Values(EntityEntry entry) =>
            ReferenceEquals(entry.Entity, Header.Entity) ? HeaderValues : StateValues;

        internal PropertyValues OriginalValues(EntityEntry entry) =>
            ReferenceEquals(entry.Entity, Header.Entity)
                ? HeaderOriginalValues
                : StateOriginalValues;
    }
}
