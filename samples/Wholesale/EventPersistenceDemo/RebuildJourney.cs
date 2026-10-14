using Gloaming.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Inventory;
using ModulithFoundry.Samples.Wholesale.Inventory.Contracts;
using ModulithFoundry.Samples.Wholesale.Purchasing;
using ModulithFoundry.Samples.Wholesale.Purchasing.Contracts;

namespace ModulithFoundry.Samples.Wholesale.EventPersistenceDemo;

// Trusted local maintenance harness. Admission/authorization and final save/commit remain host-owned.
public static class RebuildJourney
{
    private static readonly Guid Id = Guid.Parse("e5020000-0000-0000-0000-000000000001");

    public static async Task RunAsync(
        string connection,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        var services = DemoComposition.CreateServices(connection);
        services.AddStockPositionRebuilding();
        services.AddPurchaseOrderRebuilding();
        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
        );
        foreach (bool inventory in new[] { true, false })
        {
            await using (var create = Scope(provider))
            {
                DbContext database = Database(create, inventory);
                await database.Database.MigrateAsync(cancellationToken);
                await using var transaction = await database.Database.BeginTransactionAsync(
                    cancellationToken
                );
                if (inventory)
                {
                    var current = await create
                        .ServiceProvider.GetRequiredService<IStockPositionQueries>()
                        .ReadCurrentAsync(Id, cancellationToken);
                    if (current is null)
                        await create
                            .ServiceProvider.WithClock(FixtureHistories.OpenedAt)
                            .GetRequiredService<IStockPositionCommands>()
                            .OpenAsync(
                                new OpenStockPosition(Id, Guid.NewGuid(), Guid.NewGuid(), "EA"),
                                cancellationToken
                            );
                }
                else
                {
                    var current = await create
                        .ServiceProvider.GetRequiredService<IPurchaseOrderQueries>()
                        .ReadCurrentAsync(Id, cancellationToken);
                    if (current is null)
                        await create
                            .ServiceProvider.WithClock(FixtureHistories.OpenedAt)
                            .GetRequiredService<IPurchaseOrderCommands>()
                            .DraftAsync(
                                new DraftPurchaseOrder(Id, "REBUILD-1", "SUP-1", "EUR"),
                                cancellationToken
                            );
                }
                await database.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            await using (var repair = Scope(provider))
            {
                var database = Database(repair, inventory);
                await using var transaction = await database.Database.BeginTransactionAsync(
                    cancellationToken
                );
                long version;
                if (inventory)
                    version = (
                        await repair
                            .ServiceProvider.GetRequiredService<IStockPositionRebuilding>()
                            .RebuildAsync(Id, cancellationToken)
                            as StockPositionRebuildResult.Changed
                        ?? throw new InvalidOperationException("Missing stock stream.")
                    ).Version;
                else
                    version = (
                        await repair
                            .ServiceProvider.GetRequiredService<IPurchaseOrderRebuilding>()
                            .RebuildAsync(Id, cancellationToken)
                            as PurchaseOrderRebuildResult.Changed
                        ?? throw new InvalidOperationException("Missing order stream.")
                    ).Version;
                await database.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                output.WriteLine(
                    $"{(inventory ? "inventory" : "purchasing")} rebuilt: version={version}"
                );
            }
            await using (var next = Scope(provider))
            {
                var database = Database(next, inventory);
                await using var transaction = await database.Database.BeginTransactionAsync(
                    cancellationToken
                );
                if (inventory)
                {
                    var current = (
                        await next
                            .ServiceProvider.GetRequiredService<IStockPositionQueries>()
                            .ReadCurrentAsync(Id, cancellationToken)
                    )!;
                    await next
                        .ServiceProvider.WithClock(FixtureHistories.LastStockChangeAt)
                        .GetRequiredService<IStockPositionCommands>()
                        .ReceiveAsync(
                            new ReceiveStock(Id, current.Version, [new StockReceipt(1)]),
                            cancellationToken
                        );
                }
                else
                {
                    var current = (
                        await next
                            .ServiceProvider.GetRequiredService<IPurchaseOrderQueries>()
                            .ReadCurrentAsync(Id, cancellationToken)
                    )!;
                    await next
                        .ServiceProvider.WithClock(FixtureHistories.LastStockChangeAt)
                        .GetRequiredService<IPurchaseOrderCommands>()
                        .ChangeLinesAsync(
                            new ChangePurchaseOrderLines(
                                Id,
                                current.Version,
                                [new PurchaseOrderLine("ITEM-1", 2, 3)]
                            ),
                            cancellationToken
                        );
                }
                await database.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
        }
    }

    private static DbContext Database(AsyncServiceScope scope, bool inventory) =>
        inventory
            ? scope.ServiceProvider.GetRequiredService<InventoryDbContext>()
            : scope.ServiceProvider.GetRequiredService<PurchasingDbContext>();

    private static AsyncServiceScope Scope(ServiceProvider provider)
    {
        var scope = provider.CreateAsyncScope();
        scope
            .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
            .Initialize(TenantContext.ForTenant(new TenantId("rebuild-demo")));
        return scope;
    }
}
