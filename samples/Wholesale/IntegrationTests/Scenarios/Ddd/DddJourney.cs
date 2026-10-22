using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Purchasing;
using ModulithFoundry.Samples.Wholesale.Purchasing.Contracts;
using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;

namespace ModulithFoundry.Samples.Wholesale.IntegrationTests.Scenarios.Ddd;

public static class DddJourney
{
    public static Guid OrderId { get; } = Guid.Parse("d1000000-0000-0000-0000-000000000001");

    public static async Task RunAsync(
        string connection,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(output);

        await using var provider = DddComposition
            .CreateServices(connection)
            .BuildServiceProvider(
                new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
            );
        await using (var setup = DddComposition.CreateScope(provider))
        {
            var sales = setup.ServiceProvider.GetRequiredService<SalesDbContext>();
            var purchasing = setup.ServiceProvider.GetRequiredService<PurchasingDbContext>();
            await sales.Database.MigrateAsync(cancellationToken);
            await purchasing.Database.MigrateAsync(cancellationToken);
            // Creation is finite sample setup, not a production creation endpoint.
            SalesDemoSeed.Stage(sales);
            await sales.SaveChangesAsync(cancellationToken);
        }

        await using (var edit = DddComposition.CreateScope(provider))
        {
            var profiles = edit.ServiceProvider.GetRequiredService<ICustomerProfiles>();
            var result = await profiles.ChangeAsync(
                new CustomerProfileChange(
                    SalesDemoSeed.AlphaCustomerId,
                    SalesDemoSeed.AlphaAddressId,
                    1,
                    "D1 Customer",
                    "D1 Street"
                ),
                cancellationToken
            );
            if (result is not ProfileChangeResult.Updated { Profile.Version: 2 })
                throw new InvalidOperationException("The Sales profile edit did not commit.");

            var before = await profiles.ReadAsync(SalesDemoSeed.AlphaCustomerId, cancellationToken);
            bool rejected = false;
            try
            {
                await profiles.ChangeAsync(
                    new CustomerProfileChange(
                        SalesDemoSeed.AlphaCustomerId,
                        SalesDemoSeed.AlphaAddressId,
                        2,
                        "Must not replace the name",
                        " "
                    ),
                    cancellationToken
                );
            }
            catch (ArgumentException)
            {
                rejected = true;
            }

            if (
                !rejected
                || before
                    != await profiles.ReadAsync(SalesDemoSeed.AlphaCustomerId, cancellationToken)
            )
                throw new InvalidOperationException(
                    "A rejected Sales edit changed accepted state."
                );
        }

        await using (var draft = DddComposition.CreateScope(provider))
        {
            var database = draft.ServiceProvider.GetRequiredService<PurchasingDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(
                cancellationToken
            );
            var result = await draft
                .ServiceProvider.GetRequiredService<IPurchaseOrderCommands>()
                .DraftAsync(
                    new DraftPurchaseOrder(OrderId, "D1-ORDER", "SUPPLIER-1", "EUR"),
                    cancellationToken
                );
            if (result is not PurchaseOrderChangeResult.Changed { Proposed.Version: 1 })
                throw new InvalidOperationException("The Purchasing draft did not stage.");

            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        await using (var lines = DddComposition.CreateScope(provider))
        {
            var database = lines.ServiceProvider.GetRequiredService<PurchasingDbContext>();
            var commands = lines.ServiceProvider.GetRequiredService<IPurchaseOrderCommands>();
            await using var transaction = await database.Database.BeginTransactionAsync(
                cancellationToken
            );
            bool rejected = false;
            try
            {
                await commands.ChangeLinesAsync(
                    new ChangePurchaseOrderLines(
                        OrderId,
                        1,
                        [new PurchaseOrderLine("ITEM-1", decimal.MaxValue, 2)]
                    ),
                    cancellationToken
                );
            }
            catch (OverflowException)
            {
                rejected = true;
            }

            if (!rejected || database.ChangeTracker.HasChanges())
                throw new InvalidOperationException(
                    "The overflowing total left a tracked proposal."
                );

            var result = await commands.ChangeLinesAsync(
                new ChangePurchaseOrderLines(
                    OrderId,
                    1,
                    [new PurchaseOrderLine("ITEM-1", 3, 12.5m)]
                ),
                cancellationToken
            );
            if (
                result
                is not PurchaseOrderChangeResult.Changed
                {
                    Proposed.Version: 2,
                    Proposed.Total: 37.5m
                }
            )
                throw new InvalidOperationException(
                    "The complete Purchasing candidate did not stage."
                );

            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        await using var read = DddComposition.CreateScope(provider);
        var order = await read
            .ServiceProvider.GetRequiredService<IPurchaseOrderQueries>()
            .ReadCurrentAsync(OrderId, cancellationToken);
        if (order is not { Version: 2, Total: 37.5m })
            throw new InvalidOperationException(
                "Committed Purchasing state differs from the candidate."
            );

        await output.WriteLineAsync("Sales: created v1, committed v2; invalid address rejected.");
        await output.WriteLineAsync(
            "Purchasing: drafted v1, committed v2 total=37.50 EUR; overflowing total rejected."
        );
    }
}
