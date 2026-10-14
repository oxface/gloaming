namespace Gloaming.Persistence.EntityFrameworkCore;

/// <summary>Ownership validation failed; it conveys no membership or permission verdict.</summary>
public sealed class TenantOwnershipException : InvalidOperationException
{
    internal TenantOwnershipException(
        TenantOwnershipFailure reason,
        string entityTypeName,
        string? propertyName,
        string detail
    )
        : base($"Tenant ownership validation failed for '{entityTypeName}': {detail}")
    {
        Reason = reason;
        EntityTypeName = entityTypeName;
        PropertyName = propertyName;
    }

    public TenantOwnershipFailure Reason { get; }

    public string EntityTypeName { get; }

    public string? PropertyName { get; }
}
