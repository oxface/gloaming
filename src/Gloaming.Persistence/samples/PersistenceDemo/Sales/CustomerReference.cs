namespace ModulithFoundry.Samples.Wholesale.PersistenceDemo.Sales;

public sealed class CustomerReference
{
    public Guid Id { get; set; }
    public required string OrganizationKey { get; set; }
    public required string Code { get; set; }
    public required string DisplayName { get; set; }
    public required long Version { get; set; }
}
