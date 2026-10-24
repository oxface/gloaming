using Gloaming.Samples.Wholesale.Inventory.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gloaming.Samples.Wholesale.Inventory;

internal sealed class StockCatalog(InventoryDbContext database) : IStockCatalog
{
    public Task<StockAvailability?> ReadAsync(string sku, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);
        _ = database.RequiredOrganizationKey;
        return database
            .Stock.AsNoTracking()
            .Where(row => row.Sku == sku)
            .Select(row => new StockAvailability(row.Sku, row.AvailableQuantity))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
