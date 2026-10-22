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
        AddServices(services, connectionString);
        return services;
    }

    public static void AddServices(IServiceCollection services, string connectionString)
    {
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
    }
}
