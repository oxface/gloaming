namespace Gloaming.Samples.Wholesale.Sales.Contracts;

public sealed record CustomerProfile(
    Guid CustomerId,
    Guid AddressId,
    string Code,
    string DisplayName,
    string AddressLine,
    long Version
);

public sealed record CustomerProfileChange(
    Guid CustomerId,
    Guid AddressId,
    long ExpectedVersion,
    string DisplayName,
    string AddressLine
);

public abstract record ProfileChangeResult
{
    public sealed record Updated(CustomerProfile Profile) : ProfileChangeResult;

    public sealed record NotFound : ProfileChangeResult;

    public sealed record Conflict : ProfileChangeResult;
}
