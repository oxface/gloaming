using Gloaming.Auditing.EntityFrameworkCore;
using Gloaming.EntityFrameworkCore;
using Gloaming.EventSourcing.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gloaming.Samples.Wholesale.Inventory;

public static class InventoryDatabase
{
    public static IServiceCollection AddInventoryDatabase(
        this IServiceCollection services,
        string connection
    )
    {
        services.AddTenantOwnershipValidation<InventoryDbContext, string>(context =>
            context.RequiredOrganizationKey
        );
        services.AddAuditValidation<InventoryDbContext>();
        services.AddEventStreamValidation<InventoryDbContext>();
        services.AddOutboxValidation<InventoryDbContext>();
        services.AddInboxValidation<InventoryDbContext>();
        return services.AddDbContext<InventoryDbContext>(
            (provider, options) =>
            {
                Configure(options, connection);
                options.ConfigureGloamingCapabilities<InventoryDbContext>(provider);
            }
        );
    }

    public static void Configure(DbContextOptionsBuilder options, string connectionString) =>
        options.UseNpgsql(
            connectionString,
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "inventory")
        );
}
