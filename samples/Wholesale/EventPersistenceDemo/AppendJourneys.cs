using Gloaming.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Inventory;
using ModulithFoundry.Samples.Wholesale.Inventory.Contracts;
using ModulithFoundry.Samples.Wholesale.Purchasing;
using ModulithFoundry.Samples.Wholesale.Purchasing.Contracts;

namespace ModulithFoundry.Samples.Wholesale.EventPersistenceDemo;

public static class AppendJourneys
{
    private static readonly Guid CommandStreamId = Guid.Parse(
        "e5060000-0000-0000-0000-000000000002"
    );

    public static async Task RunAsync(
        string connection,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        await using var provider = DemoComposition
            .CreateServices(connection)
            .BuildServiceProvider(
                new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
            );
        // Opening/drafting and appending use separate operation contexts. All saving is visible here.
        await using (var scope = Scope(provider))
        {
            var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var history = scope.ServiceProvider.GetRequiredService<IStockPositionHistory>();
            if (await history.ReadCurrentAsync(CommandStreamId, cancellationToken) is null)
            {
                await using var transaction = await database.Database.BeginTransactionAsync(
                    cancellationToken
                );
                try
                {
                    var commands =
                        scope.ServiceProvider.GetRequiredService<IStockPositionCommands>();
                    var result = await commands.OpenAsync(
                        new OpenStockPosition(
                            CommandStreamId,
                            Guid.Parse("11111111-1111-1111-1111-111111111111"),
                            Guid.Parse("22222222-2222-2222-2222-222222222222"),
                            "EA"
                        ),
                        cancellationToken
                    );
                    if (result is not StockPositionChangeResult.Changed)
                        throw new InvalidOperationException("Opening did not stage.");
                    await database.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                }
                catch
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    throw;
                }
            }
        }
        await using (var scope = Scope(provider))
        {
            var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            var current = (
                await scope
                    .ServiceProvider.GetRequiredService<IStockPositionHistory>()
                    .ReadCurrentAsync(CommandStreamId, cancellationToken)
            )!;
            if (current.Version == 1)
            {
                await using var transaction = await database.Database.BeginTransactionAsync(
                    cancellationToken
                );
                try
                {
                    var result = await scope
                        .ServiceProvider.WithClock(FixtureHistories.FirstChangeAt)
                        .GetRequiredService<IStockPositionCommands>()
                        .ReceiveAsync(
                            new ReceiveStock(
                                CommandStreamId,
                                1,
                                [new StockReceipt(10.125m), new StockReceipt(2.875m, "DELIVERY-2")]
                            ),
                            cancellationToken
                        );
                    if (result is not StockPositionChangeResult.Changed)
                        throw new InvalidOperationException("Receipts did not stage.");
                    await database.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                }
                catch
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    throw;
                }
            }
        }
        await using (var scope = Scope(provider))
        {
            var database = scope.ServiceProvider.GetRequiredService<PurchasingDbContext>();
            if (
                await scope
                    .ServiceProvider.GetRequiredService<IPurchaseOrderHistory>()
                    .ReadCurrentAsync(CommandStreamId, cancellationToken)
                is null
            )
            {
                await using var transaction = await database.Database.BeginTransactionAsync(
                    cancellationToken
                );
                try
                {
                    var result = await scope
                        .ServiceProvider.WithClock(FixtureHistories.OpenedAt)
                        .GetRequiredService<IPurchaseOrderCommands>()
                        .DraftAsync(
                            new DraftPurchaseOrder(CommandStreamId, "COMMAND-1", "SUP-1", "EUR"),
                            cancellationToken
                        );
                    if (result is not PurchaseOrderChangeResult.Changed)
                        throw new InvalidOperationException("Draft did not stage.");
                    await database.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                }
                catch
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    throw;
                }
            }
        }
        await using (var scope = Scope(provider))
        {
            var database = scope.ServiceProvider.GetRequiredService<PurchasingDbContext>();
            var current = (
                await scope
                    .ServiceProvider.GetRequiredService<IPurchaseOrderHistory>()
                    .ReadCurrentAsync(CommandStreamId, cancellationToken)
            )!;
            if (current.Version == 1)
            {
                await using var transaction = await database.Database.BeginTransactionAsync(
                    cancellationToken
                );
                try
                {
                    var result = await scope
                        .ServiceProvider.WithClock(FixtureHistories.FirstChangeAt)
                        .GetRequiredService<IPurchaseOrderCommands>()
                        .ChangeLinesAsync(
                            new ChangePurchaseOrderLines(
                                CommandStreamId,
                                1,
                                [
                                    new PurchaseOrderLine("ITEM-1", 2.5m, 12.5m),
                                    new PurchaseOrderLine("ITEM-1", 5m, 12.5m),
                                ]
                            ),
                            cancellationToken
                        );
                    if (result is not PurchaseOrderChangeResult.Changed)
                        throw new InvalidOperationException("Lines did not stage.");
                    await database.SaveChangesAsync(cancellationToken);
                    await transaction.CommitAsync(cancellationToken);
                }
                catch
                {
                    await transaction.RollbackAsync(CancellationToken.None);
                    throw;
                }
            }
        }
        await using var read = Scope(provider);
        var stock = (
            await read
                .ServiceProvider.GetRequiredService<IStockPositionHistory>()
                .ReadCurrentAsync(CommandStreamId, cancellationToken)
        )!;
        var order = (
            await read
                .ServiceProvider.GetRequiredService<IPurchaseOrderHistory>()
                .ReadCurrentAsync(CommandStreamId, cancellationToken)
        )!;
        output.WriteLine(
            FormattableString.Invariant(
                $"inventory append: committed-version={stock.Version}, on-hand={stock.OnHand:F3}"
            )
        );
        output.WriteLine(
            FormattableString.Invariant(
                $"purchasing append: committed-version={order.Version}, total={order.Total:F2}"
            )
        );
        var inlineStock = (
            await read
                .ServiceProvider.GetRequiredService<IStockPositionQueries>()
                .ReadCurrentAsync(CommandStreamId, cancellationToken)
        )!;
        var orderQueries = read.ServiceProvider.GetRequiredService<IPurchaseOrderQueries>();
        var inlineOrder = (
            await orderQueries.ReadCurrentAsync(CommandStreamId, cancellationToken)
        )!;
        var summary = (await orderQueries.ReadSummaryAsync(CommandStreamId, cancellationToken))!;
        output.WriteLine(
            FormattableString.Invariant(
                $"inventory inline: committed-version={inlineStock.Version}, on-hand={inlineStock.OnHand:F3}"
            )
        );
        output.WriteLine(
            FormattableString.Invariant(
                $"purchasing inline: committed-version={inlineOrder.Version}, total={inlineOrder.Total:F2}"
            )
        );
        output.WriteLine(
            FormattableString.Invariant(
                $"purchasing summary: committed-version={summary.Version}, lines={summary.LineCount}, total={summary.Total:F2}"
            )
        );
    }

    private static AsyncServiceScope Scope(ServiceProvider provider)
    {
        var scope = provider.CreateAsyncScope();
        scope
            .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
            .Initialize(TenantContext.ForTenant(new TenantId("wholesale-alpha")));
        return scope;
    }
}
