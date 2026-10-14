namespace Gloaming.Tenancy;

public static class TenantContextRequirements
{
    public static TenantId RequireTenant(this TenantContext context)
    {
        ArgumentNullException.ThrowIfNull(context);
        return context.Tenant ?? throw new TenantRequiredException();
    }
}
