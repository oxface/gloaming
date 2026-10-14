namespace Gloaming.Tenancy.AspNetCore;

/// <summary>Overrides the HTTP tenant default on a controller, action or endpoint.</summary>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Method,
    AllowMultiple = false,
    Inherited = true
)]
public sealed class TenantRequirementAttribute : Attribute
{
    public TenantRequirementAttribute(TenantRequirement requirement)
    {
        if (!Enum.IsDefined(requirement))
            throw new ArgumentOutOfRangeException(nameof(requirement));
        Requirement = requirement;
    }

    public TenantRequirement Requirement { get; }
}
