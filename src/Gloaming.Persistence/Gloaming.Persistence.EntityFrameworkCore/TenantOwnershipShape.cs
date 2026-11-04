using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Gloaming.Persistence.EntityFrameworkCore;

// Shared with the optional PostgreSQL adapter so registration and runtime checks cannot drift.
internal static class TenantOwnershipShape
{
    internal static bool IsSupported(IReadOnlyEntityType entity)
    {
        string? table = entity.GetTableName();
        return table is not null
            && entity.GetViewName() is null
            && entity.BaseType is null
            && !entity.GetDerivedTypes().Any()
            && !entity.IsOwned()
            && !entity.GetReferencingForeignKeys().Any(relationship => relationship.IsOwnership)
            && !entity.GetMappingFragments().Any()
            && ComplexValuesUseTable(entity, StoreObjectIdentifier.Table(table, entity.GetSchema()))
            && !entity
                .Model.GetEntityTypes()
                .Any(other =>
                    other != entity
                    && other.GetTableName() == table
                    && other.GetSchema() == entity.GetSchema()
                );
    }

    private static bool ComplexValuesUseTable(
        IReadOnlyTypeBase type,
        StoreObjectIdentifier table
    ) =>
        type.GetComplexProperties()
            .All(value =>
                value.ComplexType.IsMappedToJson()
                || (
                    !value.IsCollection
                    && value
                        .ComplexType.GetProperties()
                        .All(leaf => leaf.GetColumnName(table) is not null)
                    && ComplexValuesUseTable(value.ComplexType, table)
                )
            );
}
