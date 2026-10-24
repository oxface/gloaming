using Gloaming.Samples.Wholesale.Inventory;
using Gloaming.Samples.Wholesale.Inventory.Contracts;
using Gloaming.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gloaming.Samples.Wholesale.IntegrationTests.Scenarios.EventPersistence;

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
        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
        );
        await using (var create = Scope(provider))
        {
            var database = Database(create);
            await database.Database.MigrateAsync(cancellationToken);
            await using var transaction = await database.Database.BeginTransactionAsync(
                cancellationToken
            );
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

            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        await using (var repair = Scope(provider))
        {
            var database = Database(repair);
            await using var transaction = await database.Database.BeginTransactionAsync(
                cancellationToken
            );
            long version = (
                await repair
                    .ServiceProvider.GetRequiredService<IStockPositionRebuilding>()
                    .RebuildAsync(Id, cancellationToken) as StockPositionRebuildResult.Changed
                ?? throw new InvalidOperationException("Missing stock stream.")
            ).Version;
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            output.WriteLine($"inventory rebuilt: version={version}");
        }

        await using (var next = Scope(provider))
        {
            var database = Database(next);
            await using var transaction = await database.Database.BeginTransactionAsync(
                cancellationToken
            );
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

            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
    }

    private static InventoryDbContext Database(AsyncServiceScope scope) =>
        scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

    private static AsyncServiceScope Scope(ServiceProvider provider)
    {
        var scope = provider.CreateAsyncScope();
        scope
            .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
            .Initialize(TenantContext.ForTenant(new TenantId("rebuild-demo")));
        return scope;
    }
}
