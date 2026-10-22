using Gloaming.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Inventory;
using ModulithFoundry.Samples.Wholesale.Purchasing;

namespace ModulithFoundry.Samples.Wholesale.IntegrationTests.Scenarios.EventPersistence;

public static class DemoComposition
{
    public static ServiceCollection CreateServices(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        var services = new ServiceCollection();
        services.AddScoped<DemoClock>();
        services.AddScoped<TimeProvider>(provider => provider.GetRequiredService<DemoClock>());
        services.AddScoped<TenantContextAccessor>();
        services.AddScoped<ITenantContextAccessor>(provider =>
            provider.GetRequiredService<TenantContextAccessor>()
        );
        services.AddScoped<ITenantContextInitializer>(provider =>
            provider.GetRequiredService<TenantContextAccessor>()
        );
        services.AddInventoryDatabase(connectionString);
        services.AddPurchasingDatabase(connectionString);
        services.AddStockPositionHistory();
        services.AddStockPositionCommands();
        services.AddStockPositionQueries();
        services.AddPurchaseOrderHistory();
        services.AddPurchaseOrderCommands();
        services.AddPurchaseOrderQueries();
        return services;
    }
}
