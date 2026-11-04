using System.Linq.Expressions;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Gloaming.Persistence.EntityFrameworkCore;

internal static class TenantOwnershipMetadata
{
    private static readonly ConditionalWeakTable<IModel, object> ValidatedModels = new();
    internal const string PropertyAnnotation = "Gloaming:TenantOwnershipProperty";
    internal const string FilterAnnotation = "Gloaming:TenantOwnershipFilter";
    internal const string ContextMemberAnnotation = "Gloaming:TenantContextMember";

    internal static TenantOwnershipException Invalid(
        IReadOnlyEntityType entity,
        string? property,
        string detail
    ) => new(TenantOwnershipFailure.InvalidConfiguration, entity.Name, property, detail);

    internal static string MemberIdentity(MemberInfo member) =>
        $"{member.DeclaringType?.AssemblyQualifiedName}|{member.MemberType}|{member.Name}";

    internal static bool IsContextMember(Expression expression) =>
        expression is MemberExpression { Expression: ConstantExpression { Value: DbContext } };

    internal static void ValidateTable(IReadOnlyEntityType entity, string property)
    {
        if (!TenantOwnershipShape.IsSupported(entity))
        {
            throw Invalid(
                entity,
                property,
                "Ownership requires an ordinary, unshared single-table entity."
            );
        }
    }

    internal static void ValidateModelShape(IModel model) =>
        ValidatedModels.GetValue(
            model,
            static value =>
            {
                foreach (var entity in value.GetEntityTypes())
                {
                    if (entity.FindAnnotation(PropertyAnnotation)?.Value is string property)
                        ValidateTable(entity, property);
                }

                return new object();
            }
        );

    internal static IProperty Validate<TEntityKey>(IEntityType entity, string propertyName)
        where TEntityKey : notnull
    {
        IProperty property =
            entity.FindProperty(propertyName)
            ?? throw Invalid(entity, propertyName, "The ownership property is not mapped.");
        if (
            property.ClrType != typeof(TEntityKey)
            || property.IsNullable
            || !property.IsConcurrencyToken
            || property.ValueGenerated != ValueGenerated.Never
            || property.GetBeforeSaveBehavior() != PropertySaveBehavior.Save
            || property.GetTypeMapping().Converter is not null
            || property.PropertyInfo is null
        )
        {
            throw Invalid(
                entity,
                propertyName,
                "Ownership property type, generation or concurrency configuration is unsupported."
            );
        }

        string? filterName = entity.FindAnnotation(FilterAnnotation)?.Value as string;
        IQueryFilter? filter = entity
            .GetDeclaredQueryFilters()
            .SingleOrDefault(f => f.Key == filterName);
        LambdaExpression? predicate = filter?.Expression;
        if (
            filterName is null
            || predicate is null
            || predicate.Body
                is not BinaryExpression
                {
                    NodeType: ExpressionType.Equal,
                    Left: MemberExpression left,
                    Right: MemberExpression right
                }
            || left.Expression != predicate.Parameters[0]
            || left.Member != property.PropertyInfo
            || right.Type != typeof(TEntityKey)
            || !IsContextMember(right)
            || MemberIdentity(right.Member)
                != entity.FindAnnotation(ContextMemberAnnotation)?.Value as string
        )
        {
            throw Invalid(
                entity,
                propertyName,
                "The configured ownership filter was removed or replaced."
            );
        }

        return property;
    }
}
