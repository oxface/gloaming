namespace ModulithFoundry.Samples.Wholesale.Sales;

internal sealed class CustomerRow
{
    public Guid Id { get; set; }
    public required string OrganizationKey { get; set; }
    public required string Code { get; set; }
    public required string DisplayName { get; set; }
    public long Version { get; set; } = 1;
    public bool DeliveryListed { get; set; } = true;
}

internal sealed class AddressRow
{
    public Guid Id { get; set; }
    public Guid CustomerId { get; set; }
    public required string OrganizationKey { get; set; }
    public required string AddressLine { get; set; }
}
