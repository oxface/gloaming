using Gloaming.ActorIdentity;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Gloaming.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using ModulithFoundry.Samples.Wholesale.Dispatch;
using ModulithFoundry.Samples.Wholesale.Dispatch.Contracts;
using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;
using ModulithFoundry.Samples.Wholesale.Worker.Directory.Delivery;

namespace ModulithFoundry.Samples.Wholesale.Worker.Directory;

public static class DirectoryComposition
{
    public static IServiceCollection AddDeliveryDirectory(
        this IServiceCollection services,
        string sales,
        string dispatch
    )
    {
        services.AddDirectorySource(sales);
        services.AddDirectoryReceiver(dispatch);
        return services.AddPostgresOutboxDispatcher<SalesDbContext, DirectoryPublisher>(
            new(TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(50))
            {
                RouteKeys = [DispatchRegistration.DirectorySubscription],
            }
        );
    }

    public static IServiceCollection AddDirectorySource(
        this IServiceCollection services,
        string sales
    )
    {
        AddContexts(services);
        services.AddSalesDatabase(sales);
        return services.AddCustomerProfiles();
    }

    public static IServiceCollection AddDirectoryReceiver(
        this IServiceCollection services,
        string dispatch
    )
    {
        AddContexts(services);
        return services.AddDispatchDirectory(dispatch);
    }

    private static void AddContexts(IServiceCollection services)
    {
        services.TryAddScoped<TenantContextAccessor>();
        services.TryAddScoped<ITenantContextAccessor>(provider =>
            provider.GetRequiredService<TenantContextAccessor>()
        );
        services.TryAddScoped<ITenantContextInitializer>(provider =>
            provider.GetRequiredService<TenantContextAccessor>()
        );
        services.TryAddScoped<ActorContextAccessor>();
        services.TryAddScoped<IActorContextAccessor>(provider =>
            provider.GetRequiredService<ActorContextAccessor>()
        );
        services.TryAddScoped<IActorContextInitializer>(provider =>
            provider.GetRequiredService<ActorContextAccessor>()
        );
    }

    public static void Establish(IServiceProvider services, string organization)
    {
        services
            .GetRequiredService<ITenantContextInitializer>()
            .Initialize(TenantContext.ForTenant(new TenantId(organization)));
        services
            .GetRequiredService<IActorContextInitializer>()
            .Initialize(new ActorContext(Actor.System(new ActorId("dispatch.directory"))));
    }

    public static async Task<bool> CopyNextAsync(
        IServiceProvider services,
        string organization,
        CancellationToken token
    )
    {
        await using var scope = services.CreateAsyncScope();
        Establish(scope.ServiceProvider, organization);
        var directory = scope.ServiceProvider.GetRequiredService<ICustomerDirectory>();
        var progress = await directory.BeginAsync(token);
        if (progress.Ready)
            return true;

        var page = await scope
            .ServiceProvider.GetRequiredService<ICustomerDeliveryDirectory>()
            .ReadPageAsync(progress.After, 100, token);
        // No source I/O occurs under a receiver database lock or transaction.
        await directory.ApplyPageAsync(progress.PopulationId, progress.After, page, token);
        return page.Complete;
    }
}
