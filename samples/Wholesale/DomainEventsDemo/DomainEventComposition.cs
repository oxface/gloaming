using Gloaming.ActorIdentity;
using Gloaming.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Inventory;
using ModulithFoundry.Samples.Wholesale.Sales;

namespace ModulithFoundry.Samples.Wholesale.EventsDemo;

public static class DomainEventComposition
{
    public static ServiceCollection CreateServices(string connection)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connection);

        var services = new ServiceCollection();
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
        services.AddSalesDatabase(connection);
        services.AddInventoryDatabase(connection);
        services.AddInventoryQueries();
        services.AddCustomerProfiles();
        services.AddDraftOrderCommands();
        services.AddDraftOrderQueries();
        services.AddCustomerOrderAllowances();
        return services;
    }

    // Finite sample identities; real callers supply their own trusted admission/authorization.
    public static AsyncServiceScope CreateScope(
        ServiceProvider provider,
        string owner = "wholesale-alpha"
    )
    {
        ArgumentNullException.ThrowIfNull(provider);

        var scope = provider.CreateAsyncScope();
        scope
            .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
            .Initialize(TenantContext.ForTenant(new TenantId(owner)));
        scope
            .ServiceProvider.GetRequiredService<IActorContextInitializer>()
            .Initialize(new ActorContext(Actor.Human(new ActorId("draft-demo-human"))));
        return scope;
    }

    public static async Task SetupAsync(
        ServiceProvider provider,
        CancellationToken cancellationToken
    )
    {
        await using var setup = CreateScope(provider);
        var sales = setup.ServiceProvider.GetRequiredService<SalesDbContext>();
        var inventory = setup.ServiceProvider.GetRequiredService<InventoryDbContext>();
        await sales.Database.MigrateAsync(cancellationToken);
        await inventory.Database.MigrateAsync(cancellationToken);
        SalesDemoSeed.Stage(sales);
        InventoryDemoSeed.Stage(inventory, 42);
        await sales.SaveChangesAsync(cancellationToken);
        await inventory.SaveChangesAsync(cancellationToken);
    }
}
