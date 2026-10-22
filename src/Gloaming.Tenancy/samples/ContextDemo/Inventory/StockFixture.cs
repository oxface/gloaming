using System.Collections.Frozen;
using Gloaming.Tenancy;

namespace ModulithFoundry.Samples.Wholesale.ContextDemo.Inventory;

// Consumer-owned demonstration data; this is not durable inventory persistence.
internal sealed class StockFixture
{
    private readonly FrozenDictionary<(string Tenant, string Sku), int> _quantities =
        new Dictionary<(string Tenant, string Sku), int>
        {
            [("wholesale-alpha", "WIDGET")] = 42,
            [("wholesale-beta", "WIDGET")] = 7,
        }.ToFrozenDictionary();

    public int GetQuantity(TenantId tenant, string sku) =>
        _quantities.GetValueOrDefault((tenant.Value, sku));
}
