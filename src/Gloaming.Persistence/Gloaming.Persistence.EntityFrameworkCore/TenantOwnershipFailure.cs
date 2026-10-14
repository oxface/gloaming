namespace Gloaming.Persistence.EntityFrameworkCore;

public enum TenantOwnershipFailure
{
    MissingOwner = 1,
    ForeignOwner = 2,
    OwnershipChanged = 3,
    InvalidConfiguration = 4,
}
