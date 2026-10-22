namespace Gloaming.Tenancy;

/// <summary>An immutable, explicitly selected tenant or deliberate tenantless operation.</summary>
public sealed record TenantContext
{
    private TenantContext(TenantId? tenant) => Tenant = tenant;

    /// <summary>Null means deliberate tenantless execution in an established context.</summary>
    public TenantId? Tenant { get; }

    /// <summary>Selects one already admitted opaque tenant identity; selection itself grants no permission.</summary>
    public static TenantContext ForTenant(TenantId tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        return new TenantContext(tenant);
    }

    /// <summary>Creates an explicit tenantless operation, distinct from uninitialized context and unrestricted access.</summary>
    public static TenantContext Tenantless() => new(tenant: null);
}
