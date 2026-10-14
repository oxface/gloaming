using Gloaming.Tenancy;
using ModulithFoundry.Samples.Wholesale.ContextDemo.Inventory.Contracts;

namespace ModulithFoundry.Samples.Wholesale.ContextDemo.Inventory;

internal sealed class StockAvailability(ITenantContextAccessor context, StockFixture stock)
    : IStockAvailability
{
    public int GetAvailableQuantity(string sku)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sku);
        // Sample policy permits anonymous reads but always requires a selected tenant.
        TenantId tenant = context.Current.RequireTenant();
        return stock.GetQuantity(tenant, sku);
    }
}
