using Gloaming.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ModulithFoundry.Samples.Wholesale.Inventory;

public sealed class InventoryDesignTimeFactory : IDesignTimeDbContextFactory<InventoryDbContext>
{
    public InventoryDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>();
        InventoryDatabase.Configure(
            options,
            Environment.GetEnvironmentVariable("ConnectionStrings__Access")
                ?? "Host=localhost;Database=not_opened;Username=not_used;Password=not_used"
        );
        return new InventoryDbContext(options.Options, new TenantContextAccessor());
    }
}
