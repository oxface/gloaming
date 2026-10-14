using Gloaming.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Inventory;
using ModulithFoundry.Samples.Wholesale.Inventory.Contracts;
using ModulithFoundry.Samples.Wholesale.Purchasing;
using ModulithFoundry.Samples.Wholesale.Purchasing.Contracts;

namespace ModulithFoundry.Samples.Wholesale.EventPersistenceDemo;

public static class DemoJourneys
{
    public static async Task RunAsync(
        string connection,
        string fixtures,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        await using var provider = DemoComposition
            .CreateServices(connection)
            .BuildServiceProvider(
                new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
            );
        await using (var setup = provider.CreateAsyncScope())
        {
            await setup
                .ServiceProvider.GetRequiredService<InventoryDbContext>()
                .Database.MigrateAsync(cancellationToken);
            await setup
                .ServiceProvider.GetRequiredService<PurchasingDbContext>()
                .Database.MigrateAsync(cancellationToken);
        }
        foreach (
            var (owner, multiplier) in new[] { ("wholesale-alpha", 1m), ("wholesale-beta", 2m) }
        )
        {
            await using var scope = provider.CreateAsyncScope();
            scope
                .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
                .Initialize(TenantContext.ForTenant(new TenantId(owner)));
            var stock = scope.ServiceProvider.GetRequiredService<IStockPositionHistory>();
            var orders = scope.ServiceProvider.GetRequiredService<IPurchaseOrderHistory>();
            if (await stock.ReadCurrentAsync(FixtureHistories.StreamId, cancellationToken) is null)
            {
                var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
                await using var transaction = await database.Database.BeginTransactionAsync(
                    cancellationToken
                );
                InventoryHistorySeed.Add(
                    database,
                    FixtureHistories.StreamId,
                    FixtureHistories.Inventory(fixtures, multiplier)
                );
                await database.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            if (await orders.ReadCurrentAsync(FixtureHistories.StreamId, cancellationToken) is null)
            {
                var database = scope.ServiceProvider.GetRequiredService<PurchasingDbContext>();
                await using var transaction = await database.Database.BeginTransactionAsync(
                    cancellationToken
                );
                PurchasingHistorySeed.Add(
                    database,
                    FixtureHistories.StreamId,
                    FixtureHistories.Purchasing(fixtures, multiplier)
                );
                await database.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
            var currentStock = (
                await stock.ReadCurrentAsync(FixtureHistories.StreamId, cancellationToken)
            )!;
            var earlierStock = (
                await stock.ReadAtVersionAsync(FixtureHistories.StreamId, 2, cancellationToken)
            )!;
            var timedStock = (
                await stock.ReadAsOfAsync(
                    FixtureHistories.StreamId,
                    FixtureHistories.FirstChangeAt,
                    cancellationToken
                )
            )!;
            var beforeStock = await stock.ReadAsOfAsync(
                FixtureHistories.StreamId,
                FixtureHistories.OpenedAt.AddTicks(-1),
                cancellationToken
            );
            output.WriteLine(
                FormattableString.Invariant(
                    $"{owner}: stock current={currentStock.OnHand:F3}, version-2={earlierStock.OnHand:F3}, cutoff={timedStock.OnHand:F3}, before-open={(beforeStock is null ? "none" : "present")}"
                )
            );
            var currentOrder = (
                await orders.ReadCurrentAsync(FixtureHistories.StreamId, cancellationToken)
            )!;
            var earlierOrder = (
                await orders.ReadAtVersionAsync(FixtureHistories.StreamId, 2, cancellationToken)
            )!;
            var timedOrder = (
                await orders.ReadAsOfAsync(
                    FixtureHistories.StreamId,
                    FixtureHistories.FirstChangeAt.ToOffset(TimeSpan.FromHours(2)),
                    cancellationToken
                )
            )!;
            var beforeOrder = await orders.ReadAsOfAsync(
                FixtureHistories.StreamId,
                FixtureHistories.OpenedAt.AddTicks(-1),
                cancellationToken
            );
            output.WriteLine(
                FormattableString.Invariant(
                    $"{owner}: order current={currentOrder.Total:F2}, version-2={earlierOrder.Total:F2}, cutoff={timedOrder.Total:F2}, before-draft={(beforeOrder is null ? "none" : "present")}"
                )
            );
        }
    }
}
