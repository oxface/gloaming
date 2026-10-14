using ConsumerRoot.Catalog;
using Gloaming.ActorIdentity;
using Gloaming.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace ConsumerRoot.Host;

public static class Composition
{
    public static ServiceCollection CreateServices(string connectionString)
    {
        var services = new ServiceCollection();
        services.AddScoped<ActorContextAccessor>();
        services.AddScoped<IActorContextAccessor>(provider =>
            provider.GetRequiredService<ActorContextAccessor>()
        );
        services.AddScoped<IActorContextInitializer>(provider =>
            provider.GetRequiredService<ActorContextAccessor>()
        );
        services.AddScoped<TenantContextAccessor>();
        services.AddScoped<ITenantContextAccessor>(provider =>
            provider.GetRequiredService<TenantContextAccessor>()
        );
        services.AddScoped<ITenantContextInitializer>(provider =>
            provider.GetRequiredService<TenantContextAccessor>()
        );
        services.AddCatalog(connectionString);
        return services;
    }
}
