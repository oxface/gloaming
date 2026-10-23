using Gloaming.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Gloaming.Samples.Wholesale.Sales;

public sealed class SalesDesignTimeFactory : IDesignTimeDbContextFactory<SalesDbContext>
{
    public SalesDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<SalesDbContext>();
        SalesDatabase.Configure(
            options,
            Environment.GetEnvironmentVariable("ConnectionStrings__Access")
                ?? "Host=localhost;Database=not_opened;Username=not_used;Password=not_used"
        );
        return new SalesDbContext(options.Options, new TenantContextAccessor());
    }
}
