using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Gloaming.EventSourcing.EntityFrameworkCore.Postgres;

// Keep selected outputs and their native evidence together. Tracker acceptance is not storage evidence.
internal sealed class ProjectionWrites
{
    private readonly SelectedWrite[] required;

    internal ProjectionWrites(DbContext database)
    {
        database.ChangeTracker.DetectChanges();
        required = database
            .ChangeTracker.Entries()
            .Where(entry =>
                entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
            )
            .Select(Capture)
            .ToArray();
    }

    internal async Task RequireStoredAsync(DbContext database, CancellationToken token)
    {
        database.ChangeTracker.DetectChanges();
        if (database.ChangeTracker.HasChanges())
            throw new InvalidOperationException(
                "The owned accepting projection save left unsaved changes."
            );

        foreach (var item in required)
        {
            if (item.State != EntityState.Deleted && item.Entry.State != EntityState.Unchanged)
                throw new InvalidOperationException(
                    "A selected projection write was detached or suppressed."
                );

            // Accepting a deletion detaches it. Its retained key must still identify the selected row.
            foreach (var value in item.Values.Where(value => value.Property.IsPrimaryKey()))
                if (
                    !value.Generated
                    && !Same(value.Property, value.Value, item.Entry.CurrentValues[value.Property])
                )
                    throw new InvalidOperationException(
                        "A selected projection key changed during save."
                    );

            var stored = await item.Entry.GetDatabaseValuesAsync(token);
            if (item.State == EntityState.Deleted)
            {
                if (stored is not null)
                    throw new InvalidOperationException(
                        "A selected projection deletion was not stored."
                    );

                continue;
            }

            if (stored is null)
                throw new InvalidOperationException("A selected projection row was not stored.");

            foreach (var value in item.Values)
            {
                var property = value.Property;
                object? current = item.Entry.CurrentValues[property];
                if (
                    (!value.Generated && !Same(property, value.Value, current))
                    || !Same(property, current, stored[property])
                )
                    throw new InvalidOperationException(
                        "Selected projection values differ from the native stored row."
                    );
            }
        }
    }

    private static bool Same(IProperty property, object? expected, object? actual)
    {
        if (expected is byte[] firstBytes && actual is byte[] secondBytes)
            return firstBytes.AsSpan().SequenceEqual(secondBytes);

        if (expected is JsonElement left && actual is JsonElement right)
            return JsonElement.DeepEquals(left, right);

        if (property.GetColumnType() == "jsonb" && expected is string a && actual is string b)
        {
            using var first = JsonDocument.Parse(a);
            using var second = JsonDocument.Parse(b);
            return JsonElement.DeepEquals(first.RootElement, second.RootElement);
        }

        return Equals(expected, actual);
    }

    private static SelectedWrite Capture(EntityEntry entry)
    {
        if (
            entry.Metadata.IsOwned()
            || entry.Metadata.BaseType is not null
            || entry.Metadata.GetDerivedTypes().Any()
            || entry.Metadata.GetComplexProperties().Any()
            || entry.Metadata.GetTableName() is null
            || entry.Metadata.GetViewName() is not null
            || entry.Metadata.GetSqlQuery() is not null
            || entry.Metadata.GetFunctionName() is not null
            || entry.Metadata.GetMappingFragments(StoreObjectType.Table).Any()
            || entry
                .Context.Model.GetEntityTypes()
                .Count(type =>
                    type.GetTableName() == entry.Metadata.GetTableName()
                    && type.GetSchema() == entry.Metadata.GetSchema()
                ) != 1
        )
            throw new InvalidOperationException(
                "Projection output requires ordinary single-table scalar entities without owned, complex, inherited or split mappings."
            );

        var values = entry
            .CurrentValues.Properties.Select(property =>
            {
                object? value = entry.CurrentValues[property];
                Type type = Nullable.GetUnderlyingType(property.ClrType) ?? property.ClrType;
                if (
                    !(
                        type.IsPrimitive
                        || type.IsEnum
                        || type == typeof(string)
                        || type == typeof(byte[])
                        || type == typeof(decimal)
                        || type == typeof(Guid)
                        || type == typeof(DateTime)
                        || type == typeof(DateTimeOffset)
                        || type == typeof(DateOnly)
                        || type == typeof(TimeOnly)
                        || type == typeof(TimeSpan)
                        || type == typeof(JsonElement)
                    )
                )
                    throw new InvalidOperationException(
                        "Projection output supports native scalar CLR values, strings, bytea and JsonElement; custom objects and other arrays require a separately proved adapter."
                    );

                if (
                    property.ValueGenerated
                    is not (
                        ValueGenerated.Never
                        or ValueGenerated.OnAdd
                        or ValueGenerated.OnUpdate
                        or ValueGenerated.OnAddOrUpdate
                    )
                )
                    throw new InvalidOperationException(
                        "This projection value-generation mode is unsupported."
                    );

                object? snapshot = value switch
                {
                    byte[] bytes => bytes.ToArray(),
                    JsonElement json => json.Clone(),
                    _ => value,
                };
                var member = entry.Property(property.Name);
                bool generated =
                    entry.State == EntityState.Added
                        && property.ValueGenerated
                            is ValueGenerated.OnAdd
                                or ValueGenerated.OnAddOrUpdate
                        && (
                            member.IsTemporary
                            || property.GetBeforeSaveBehavior() == PropertySaveBehavior.Ignore
                            || Same(property, value, property.Sentinel)
                        )
                    || entry.State == EntityState.Modified
                        && property.ValueGenerated
                            is ValueGenerated.OnUpdate
                                or ValueGenerated.OnAddOrUpdate
                        && (
                            property.GetAfterSaveBehavior() == PropertySaveBehavior.Ignore
                            || !member.IsModified
                        );
                return new SelectedValue(property, snapshot, generated);
            })
            .ToArray();

        return new(entry, entry.State, values);
    }

    private sealed record SelectedWrite(
        EntityEntry Entry,
        EntityState State,
        SelectedValue[] Values
    );

    private sealed record SelectedValue(IProperty Property, object? Value, bool Generated);
}
