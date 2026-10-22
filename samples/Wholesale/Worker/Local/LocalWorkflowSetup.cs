using Gloaming.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Inventory;
using ModulithFoundry.Samples.Wholesale.Inventory.Contracts;
using ModulithFoundry.Samples.Wholesale.Sales;

namespace ModulithFoundry.Samples.Wholesale.Worker.Local;

// Privileged finite setup, separate from ordinary worker startup and public module handoff.
public static class LocalWorkflowSetup
{
    public static async Task MigrateAsync(IServiceProvider services, CancellationToken token)
    {
        await using var scope = services.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<SalesDbContext>()
            .Database.MigrateAsync(token);
        await scope
            .ServiceProvider.GetRequiredService<InventoryDbContext>()
            .Database.MigrateAsync(token);
    }

    public static async Task RequireStorageAsync(IServiceProvider services, CancellationToken token)
    {
        await using var scope = services.CreateAsyncScope();
        var sales = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
        var inventory = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        if (
            (await sales.Database.GetPendingMigrationsAsync(token)).Any()
            || (await inventory.Database.GetPendingMigrationsAsync(token)).Any()
        )
            throw new InvalidOperationException(
                "Run --role setup before starting local reservation workers."
            );
    }

    public static async Task SeedAsync(
        IServiceProvider services,
        Guid stock,
        string organization,
        CancellationToken token
    )
    {
        for (int step = 1; step <= 2; step++)
        {
            await using var scope = services.CreateAsyncScope();
            scope
                .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
                .Initialize(TenantContext.ForTenant(new TenantId(organization)));
            var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(token);
            var commands = scope.ServiceProvider.GetRequiredService<IStockPositionCommands>();
            var result =
                step == 1
                    ? await commands.OpenAsync(
                        new(stock, Guid.NewGuid(), Guid.NewGuid(), "EA"),
                        token
                    )
                    : await commands.ReceiveAsync(new(stock, 1, [new StockReceipt(5)]), token);
            if (result is not StockPositionChangeResult.Changed)
                throw new InvalidOperationException("Seed requires a fresh stock identity.");

            await database.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
        }
    }
}
