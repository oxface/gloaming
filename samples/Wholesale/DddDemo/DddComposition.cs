using Gloaming.ActorIdentity;
using Gloaming.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Purchasing;
using ModulithFoundry.Samples.Wholesale.Sales;

namespace ModulithFoundry.Samples.Wholesale.DddDemo;

public static class DddComposition
{
    public static ServiceCollection CreateServices(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);

        var services = new ServiceCollection();
        services.AddSingleton(TimeProvider.System);
        services.AddScoped<TenantContextAccessor>();
        services.AddScoped<ITenantContextAccessor>(provider =>
            provider.GetRequiredService<TenantContextAccessor>()
        );
        services.AddScoped<ITenantContextInitializer>(provider =>
            provider.GetRequiredService<TenantContextAccessor>()
        );
        services.AddScoped<ActorContextAccessor>();
        services.AddScoped<IActorContextAccessor>(provider =>
            provider.GetRequiredService<ActorContextAccessor>()
        );
        services.AddScoped<IActorContextInitializer>(provider =>
            provider.GetRequiredService<ActorContextAccessor>()
        );
        services.AddSalesDatabase(connectionString);
        services.AddPurchasingDatabase(connectionString);
        services.AddCustomerProfiles();
        services.AddPurchaseOrderCommands();
        services.AddPurchaseOrderQueries();
        services.AddPurchaseOrderHistory();
        return services;
    }

    // Finite sample admission only. The host supplies trusted identities; they grant no permissions.
    public static AsyncServiceScope CreateScope(ServiceProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);

        var scope = provider.CreateAsyncScope();
        scope
            .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
            .Initialize(TenantContext.ForTenant(new TenantId("wholesale-alpha")));
        scope
            .ServiceProvider.GetRequiredService<IActorContextInitializer>()
            .Initialize(new ActorContext(Actor.Human(new ActorId("ddd-demo-human"))));
        return scope;
    }
}
