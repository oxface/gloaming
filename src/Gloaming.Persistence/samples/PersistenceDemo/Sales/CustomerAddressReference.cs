namespace ModulithFoundry.Samples.Wholesale.PersistenceDemo.Sales;

public sealed class CustomerAddressReference
{
    public Guid Id { get; set; }
    public required string OrganizationKey { get; set; }
    public Guid CustomerId { get; set; }
    public required string AddressLine { get; set; }
}
