namespace Gloaming.Tenancy;

/// <summary>An immutable, explicitly selected tenant or deliberate tenantless operation.</summary>
public sealed record TenantContext
{
    private TenantContext(TenantId? tenant) => Tenant = tenant;

    /// <summary>Null means deliberate tenantless execution in an established context.</summary>
    public TenantId? Tenant { get; }

    public static TenantContext ForTenant(TenantId tenant)
    {
        ArgumentNullException.ThrowIfNull(tenant);
        return new TenantContext(tenant);
    }

    public static TenantContext Tenantless() => new(tenant: null);
}
