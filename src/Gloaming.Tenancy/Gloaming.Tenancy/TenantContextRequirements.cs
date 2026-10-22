namespace Gloaming.Tenancy;

/// <summary>Checks selected operation tenancy without supplying membership or resource authorization.</summary>
public static class TenantContextRequirements
{
    /// <summary>Returns the selected tenant or rejects an explicitly tenantless context.</summary>
    public static TenantId RequireTenant(this TenantContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Tenant ?? throw new TenantRequiredException();
    }
}
