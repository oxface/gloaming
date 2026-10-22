using ConsumerRoot.Catalog.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ConsumerRoot.Catalog;

public static class CatalogRegistration
{
    public static IServiceCollection AddCatalog(
        this IServiceCollection services,
        string connectionString
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        services.AddDbContext<CatalogDbContext>(options =>
            options.UseNpgsql(
                connectionString,
                postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "catalog")
            )
        );
        services.AddScoped<ICatalogCommands, CatalogCommands>();
        return services.AddScoped<ICatalogQueries, CatalogQueries>();
    }
}
