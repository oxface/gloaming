namespace ModulithFoundry.Samples.Wholesale.PersistenceDemo.Inventory;

// Consumer-owned reference data, not a stock aggregate or reservation workflow.
public sealed class StockReference
{
    public Guid Id { get; set; }
    public required string OrganizationKey { get; set; }
    public required string Sku { get; set; }
    public int Quantity { get; set; }
    public bool IsDeleted { get; set; }
}
