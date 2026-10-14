using Gloaming.ActorIdentity;
using Gloaming.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.ContextDemo.Inventory;
using ModulithFoundry.Samples.Wholesale.ContextDemo.Inventory.Contracts;
using ModulithFoundry.Samples.Wholesale.ContextDemo.Sales;
using ModulithFoundry.Samples.Wholesale.ContextDemo.Sales.Contracts;

namespace ModulithFoundry.Samples.Wholesale.ContextDemo;

public static class DemoComposition
{
    public static ServiceCollection CreateActorServices()
    {
        var services = new ServiceCollection();
        services.AddScoped<ActorContextAccessor>();
        services.AddScoped<IActorContextAccessor>(provider =>
            provider.GetRequiredService<ActorContextAccessor>()
        );
        services.AddScoped<IActorContextInitializer>(provider =>
            provider.GetRequiredService<ActorContextAccessor>()
        );
        return services;
    }

    public static ServiceCollection CreateTenantServices()
    {
        var services = new ServiceCollection();
        AddTenancyAndInventory(services);
        return services;
    }

    public static ServiceCollection CreateServices()
    {
        ServiceCollection services = CreateActorServices();
        AddTenancyAndInventory(services);
        services.AddScoped<IDraftOrderPreview, DraftOrderPreview>();
        return services;
    }

    private static void AddTenancyAndInventory(ServiceCollection services)
    {
        services.AddScoped<TenantContextAccessor>();
        services.AddScoped<ITenantContextAccessor>(provider =>
            provider.GetRequiredService<TenantContextAccessor>()
        );
        services.AddScoped<ITenantContextInitializer>(provider =>
            provider.GetRequiredService<TenantContextAccessor>()
        );
        services.AddSingleton<StockFixture>();
        services.AddScoped<IStockAvailability, StockAvailability>();
    }
}
