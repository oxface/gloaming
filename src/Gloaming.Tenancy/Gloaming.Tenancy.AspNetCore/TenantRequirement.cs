namespace Gloaming.Tenancy.AspNetCore;

/// <summary>Distinguishes operations requiring an admitted tenant from those permitting explicit tenantless execution.</summary>
public enum TenantRequirement
{
    /// <summary>Admission must resolve a selected tenant before the endpoint executes.</summary>
    Required = 1,

    /// <summary>Consumer resolution may explicitly choose tenantless execution; it grants no unrestricted access.</summary>
    TenantlessAllowed = 2,
}
