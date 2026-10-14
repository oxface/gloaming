using ConsumerRoot.Catalog;
using Gloaming.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace ConsumerRoot.Host;

public sealed class CatalogDesignTimeFactory : IDesignTimeDbContextFactory<CatalogDbContext>
{
    public CatalogDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<CatalogDbContext>();
        options.UseNpgsql(
            Environment.GetEnvironmentVariable("CATALOG_CONNECTION_STRING")
                ?? "Host=localhost;Database=design_time_only",
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "catalog")
        );
        // Scaffolding/migration does not read tenant data or establish an operation identity.
        return new CatalogDbContext(options.Options, new TenantContextAccessor());
    }
}
