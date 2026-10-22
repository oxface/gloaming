namespace ModulithFoundry.Samples.Wholesale.PersistenceDemo.Sales;

public sealed record CustomerProfileChange(
    Guid CustomerId,
    Guid AddressId,
    long ExpectedVersion,
    string DisplayName,
    string AddressLine
);
