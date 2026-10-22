namespace Gloaming.Tenancy.AspNetCore;

/// <summary>Default tenancy requirement used when an endpoint has no explicit tenancy metadata.</summary>
public sealed class HttpTenantContextOptions
{
    /// <summary>The fallback endpoint requirement; selected tenancy is required by default.</summary>
    public TenantRequirement DefaultRequirement { get; set; } = TenantRequirement.Required;
}
