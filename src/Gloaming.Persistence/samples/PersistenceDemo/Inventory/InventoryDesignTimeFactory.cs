using Gloaming.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Gloaming.Samples.Wholesale.PersistenceDemo.Inventory;

public sealed class InventoryDesignTimeFactory : IDesignTimeDbContextFactory<InventoryDbContext>
{
    public InventoryDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>();
        InventoryDatabase.Configure(
            options,
            Environment.GetEnvironmentVariable("WHOLESALE_DEMO_CONNECTION_STRING")
                ?? "Host=localhost;Database=not_opened;Username=not_used;Password=not_used"
        );
        return new InventoryDbContext(options.Options, new TenantContextAccessor());
    }
}
