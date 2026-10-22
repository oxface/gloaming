namespace Gloaming.Tenancy.AspNetCore;

/// <summary>Overrides the HTTP tenant default on a controller, action or endpoint.</summary>
[AttributeUsage(
    AttributeTargets.Class | AttributeTargets.Method,
    AllowMultiple = false,
    Inherited = true
)]
public sealed class TenantRequirementAttribute : Attribute
{
    /// <summary>Declares one explicit endpoint tenancy requirement; native authorization remains independent.</summary>
    public TenantRequirementAttribute(TenantRequirement requirement)
    {
        if (!Enum.IsDefined(requirement))
            throw new ArgumentOutOfRangeException(nameof(requirement));
        Requirement = requirement;
    }

    /// <summary>The endpoint tenancy requirement selected by this metadata.</summary>
    public TenantRequirement Requirement { get; }
}
