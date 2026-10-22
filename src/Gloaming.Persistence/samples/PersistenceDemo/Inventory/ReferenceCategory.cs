namespace ModulithFoundry.Samples.Wholesale.PersistenceDemo.Inventory;

// Explicitly global reference data; application policy owns access to this table.
public sealed class ReferenceCategory
{
    public Guid Id { get; set; }
    public required string Name { get; set; }
}
