using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Gloaming.EventSourcing.EntityFrameworkCore;

internal static class InlineStateMetadata
{
    internal static IForeignKey StreamForeignKey(
        IReadOnlyModel model,
        Type streamType,
        Type rowType
    )
    {
        var stream =
            model.FindEntityType(streamType)
            ?? throw new InvalidOperationException(
                "Map the stream before configuring inline state."
            );
        var row =
            model.FindEntityType(rowType)
            ?? throw new InvalidOperationException("Map the inline row before configuring it.");
        if (
            stream.BaseType is not null
            || row.BaseType is not null
            || row.IsOwned()
            || row.GetDerivedTypes().Any()
            || row.GetMappingFragments().Any()
            || row.GetViewName() is not null
        )
            throw new InvalidOperationException("Use ordinary independent inline row types.");
        var key = stream.FindPrimaryKey();
        var streamForeignKey =
            row.GetForeignKeys().SingleOrDefault(foreign => foreign.PrincipalKey == key)
            ?? throw new InvalidOperationException(
                "Inline state must reference the complete stream primary key."
            );
        if (!streamForeignKey.Properties.SequenceEqual(row.FindPrimaryKey()?.Properties ?? []))
            throw new InvalidOperationException("Use one inline row per complete stream key.");
        if (
            streamForeignKey.Properties.Any(property =>
                property.IsShadowProperty()
                || property.PropertyInfo is null
                || property.GetValueConverter() is not null
                || property.GetProviderClrType() is not null
            )
        )
            throw new InvalidOperationException("Use mapped unconverted inline keys.");
        if (
            row.FindProperty(nameof(IInlineStateRecord.Version))
                is not {
                    IsConcurrencyToken: true,
                    ValueGenerated: ValueGenerated.Never,
                    ClrType: var versionType
                }
            || versionType != typeof(long)
            || row.FindProperty(nameof(IInlineStateRecord.RecordedAt))?.ClrType
                != typeof(DateTimeOffset)
        )
            throw new InvalidOperationException(
                "Map inline version as a native concurrency token and recorded time."
            );
        return (IForeignKey)streamForeignKey;
    }

    internal static object?[] Values(
        EntityEntry entry,
        IReadOnlyList<IProperty> properties,
        bool original = false
    ) =>
        properties
            .Select(property =>
                original
                    ? entry.Property(property.Name).OriginalValue
                    : entry.Property(property.Name).CurrentValue
            )
            .ToArray();
}
