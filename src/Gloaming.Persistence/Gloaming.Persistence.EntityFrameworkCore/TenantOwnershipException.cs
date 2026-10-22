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

    /// <summary>The ownership rule that rejected the native proposal.</summary>
    public TenantOwnershipFailure Reason { get; }

    /// <summary>The native mapped entity type associated with the rejected proposal.</summary>
    public string EntityTypeName { get; }

    /// <summary>The mapped ownership property associated with the failure.</summary>
    public string? PropertyName { get; }
}
