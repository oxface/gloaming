using Gloaming.ActorIdentity;
using Gloaming.Samples.Wholesale.Inventory;
using Gloaming.Samples.Wholesale.Sales;
using Gloaming.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gloaming.Samples.Wholesale.IntegrationTests.Scenarios.Cqrs;

public static class CqrsComposition
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
        services.AddStockPositionQueries();
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
            .Initialize(new ActorContext(Actor.Human(new ActorId("cqrs-demo-human"))));
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
        await using (
            var transaction = await sales.Database.BeginTransactionAsync(cancellationToken)
        )
        {
            SalesDemoSeed.Stage(sales);
            await sales.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        await using (
            var transaction = await inventory.Database.BeginTransactionAsync(cancellationToken)
        )
        {
            InventoryDemoSeed.Stage(inventory, 42);
            CqrsDemoData.StageStock(inventory);
            await inventory.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }
}
