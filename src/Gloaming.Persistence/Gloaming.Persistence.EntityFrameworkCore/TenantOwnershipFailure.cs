namespace Gloaming.Persistence.EntityFrameworkCore;

/// <summary>Identifies missing, foreign or changed ownership, or unsupported ownership model configuration.</summary>
public enum TenantOwnershipFailure
{
    /// <summary>The proposed tenant-scoped row has no valid owner value.</summary>
    MissingOwner = 1,

    /// <summary>The row owner does not match the established operation owner.</summary>
    ForeignOwner = 2,

    /// <summary>A tracked existing row attempts to change its original owner.</summary>
    OwnershipChanged = 3,

    /// <summary>The native model cannot provide the supported ownership contract.</summary>
    InvalidConfiguration = 4,
}
