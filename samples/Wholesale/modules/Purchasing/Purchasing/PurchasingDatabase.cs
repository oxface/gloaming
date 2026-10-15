using Gloaming.EntityFrameworkCore;
using Gloaming.EventSourcing.EntityFrameworkCore;
using Gloaming.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ModulithFoundry.Samples.Wholesale.Purchasing;

public static class PurchasingDatabase
{
    public static IServiceCollection AddPurchasingDatabase(
        this IServiceCollection services,
        string connection
    )
    {
        services.AddTenantOwnershipValidation<PurchasingDbContext, string>(context =>
            context.RequiredOrganizationKey
        );
        services.AddEventStreamValidation<PurchasingDbContext>();
        return services.AddDbContext<PurchasingDbContext>(
            (provider, options) =>
            {
                Configure(options, connection);
                options.ConfigureGloamingCapabilities<PurchasingDbContext>(provider);
            }
        );
    }

    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(
            connectionString,
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "purchasing")
        );
}
