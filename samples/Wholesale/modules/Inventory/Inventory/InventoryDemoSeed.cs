namespace Gloaming.Samples.Wholesale.Inventory;

// Stage demonstration rows; the caller establishes tenancy and owns save/commit.
public static class InventoryDemoSeed
{
    public static void Stage(InventoryDbContext database, int quantity)
    {
        ArgumentNullException.ThrowIfNull(database);
        database.Add(
            new StockRow
            {
                Id = Guid.NewGuid(),
                OrganizationKey = database.RequiredOrganizationKey,
                Sku = "DEMO-NOTEBOOK",
                AvailableQuantity = quantity,
            }
        );
    }
}
