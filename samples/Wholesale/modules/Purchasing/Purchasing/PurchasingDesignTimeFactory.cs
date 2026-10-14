using Gloaming.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ModulithFoundry.Samples.Wholesale.Purchasing;

public sealed class PurchasingDesignTimeFactory : IDesignTimeDbContextFactory<PurchasingDbContext>
{
    public PurchasingDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<PurchasingDbContext>();
        PurchasingDatabase.Configure(
            options,
            Environment.GetEnvironmentVariable("ConnectionStrings__Access")
                ?? "Host=localhost;Database=not_opened;Username=not_used;Password=not_used"
        );
        return new PurchasingDbContext(options.Options, new TenantContextAccessor());
    }
}
