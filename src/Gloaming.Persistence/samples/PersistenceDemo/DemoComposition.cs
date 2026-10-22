using Gloaming.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.PersistenceDemo.Inventory;
using ModulithFoundry.Samples.Wholesale.PersistenceDemo.Sales;

namespace ModulithFoundry.Samples.Wholesale.PersistenceDemo;

public static class DemoComposition
{
    public static ServiceCollection CreateServices(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var services = new ServiceCollection();
        services.AddScoped<TenantContextAccessor>();
        services.AddScoped<ITenantContextAccessor>(provider =>
            provider.GetRequiredService<TenantContextAccessor>()
        );
        services.AddScoped<ITenantContextInitializer>(provider =>
            provider.GetRequiredService<TenantContextAccessor>()
        );
        services.AddDbContext<InventoryDbContext>(options =>
            InventoryDatabase.Configure(options, connectionString)
        );
        services.AddDbContext<SalesDbContext>(options =>
            SalesDatabase.Configure(options, connectionString)
        );
        return services;
    }
}
