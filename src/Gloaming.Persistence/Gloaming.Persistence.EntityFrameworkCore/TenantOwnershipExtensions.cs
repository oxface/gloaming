using System.Linq.Expressions;
using System.Reflection;
using Gloaming.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gloaming.Persistence.EntityFrameworkCore;

public static class TenantOwnershipExtensions
{
    /// <summary>
    /// Configures consumer-populated ownership, a named equality filter and an ownership
    /// concurrency token. The key expression must read a member on the DbContext instance.
    /// </summary>
    public static EntityTypeBuilder<TEntity> HasTenantOwnership<TEntity, TTenant>(
        this EntityTypeBuilder<TEntity> entity,
        Expression<Func<TEntity, TTenant>> tenantProperty,
        Expression<Func<TTenant>> requiredTenant,
        string filterName
    )
        where TEntity : class
        where TTenant : notnull
    {
        ArgumentNullException.ThrowIfNull(entity);
        ArgumentNullException.ThrowIfNull(tenantProperty);
        ArgumentNullException.ThrowIfNull(requiredTenant);
        ArgumentException.ThrowIfNullOrWhiteSpace(filterName);
        if (
            tenantProperty.Body is not MemberExpression { Member: PropertyInfo member } ownership
            || ownership.Expression != tenantProperty.Parameters[0]
            || entity.Metadata.FindProperty(member.Name)?.PropertyInfo != member
        )
        {
            throw TenantOwnershipMetadata.Invalid(
                entity.Metadata,
                null,
                "Select a directly mapped CLR property."
            );
        }

        TenantOwnershipMetadata.ValidateTable(entity.Metadata, member.Name);
        if (!TenantOwnershipMetadata.IsContextMember(requiredTenant.Body))
        {
            throw TenantOwnershipMetadata.Invalid(
                entity.Metadata,
                member.Name,
                "The key expression must read a member of the DbContext instance."
            );
        }
        if (
            entity.Metadata.FindAnnotation(TenantOwnershipMetadata.PropertyAnnotation) is not null
            || entity.Metadata.GetDeclaredQueryFilters().Any(filter => filter.Key == filterName)
            || entity.Metadata.FindProperty(member.Name)!.GetValueConverter() is not null
            || entity.Metadata.FindProperty(member.Name)!.GetProviderClrType() is not null
        )
        {
            throw TenantOwnershipMetadata.Invalid(
                entity.Metadata,
                member.Name,
                "Ownership/filter registration is duplicated or uses an unsupported converter."
            );
        }

        entity
            .Property(tenantProperty)
            .IsRequired()
            .IsConcurrencyToken()
            .ValueGeneratedNever()
            .Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Save);
        var predicate = Expression.Lambda<Func<TEntity, bool>>(
            Expression.Equal(tenantProperty.Body, requiredTenant.Body),
            tenantProperty.Parameters
        );
        entity.HasQueryFilter(filterName, predicate);
        entity.Metadata.Model.RequireGloamingCapability("tenant-ownership");
        entity.HasAnnotation(TenantOwnershipMetadata.PropertyAnnotation, member.Name);
        entity.HasAnnotation(TenantOwnershipMetadata.FilterAnnotation, filterName);
        entity.HasAnnotation(
            TenantOwnershipMetadata.ContextMemberAnnotation,
            TenantOwnershipMetadata.MemberIdentity(((MemberExpression)requiredTenant.Body).Member)
        );
        return entity;
    }

    /// <summary>
    /// Detects changes and validates registered tenant-owned tracked writes before the caller
    /// saves. It does not fill values, query stored ownership, save, commit or retry.
    /// </summary>
    public static void ValidateTenantChanges<TTenant>(
        this DbContext context,
        Func<TTenant> requireTenant
    )
        where TTenant : notnull
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(requireTenant);
        context.ChangeTracker.DetectChanges();
        bool resolved = false;
        TTenant tenant = default!;
        foreach (EntityEntry entry in context.ChangeTracker.Entries())
        {
            if (
                entry.State
                is not (EntityState.Added or EntityState.Modified or EntityState.Deleted)
            )
            {
                continue;
            }
            if (
                entry.Metadata.FindAnnotation(TenantOwnershipMetadata.PropertyAnnotation)?.Value
                is not string propertyName
            )
            {
                continue;
            }

            IProperty property = TenantOwnershipMetadata.Validate<TTenant>(
                entry.Metadata,
                propertyName
            );
            if (!resolved)
            {
                tenant = requireTenant();
                if (tenant is null)
                {
                    throw TenantOwnershipMetadata.Invalid(
                        entry.Metadata,
                        propertyName,
                        "The required operation tenant is null."
                    );
                }
                resolved = true;
            }

            PropertyEntry owner = entry.Property(propertyName);
            object? current = owner.CurrentValue;
            object? original = owner.OriginalValue;
            if (current is null || (entry.State != EntityState.Added && original is null))
            {
                throw Failure(TenantOwnershipFailure.MissingOwner, "Supply the owner explicitly.");
            }
            if (
                entry.State != EntityState.Added
                && !property.GetValueComparer().Equals(current, original)
            )
            {
                throw Failure(
                    TenantOwnershipFailure.OwnershipChanged,
                    "Ordinary writes cannot change ownership."
                );
            }
            if (
                !property.GetValueComparer().Equals(current, tenant)
                || (
                    entry.State != EntityState.Added
                    && !property.GetValueComparer().Equals(original, tenant)
                )
            )
            {
                throw Failure(
                    TenantOwnershipFailure.ForeignOwner,
                    "The entry does not belong to the operation tenant."
                );
            }

            TenantOwnershipException Failure(TenantOwnershipFailure reason, string detail) =>
                new(reason, entry.Metadata.Name, propertyName, detail);
        }
    }
}
