using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;

namespace ModulithFoundry.Samples.Wholesale.RichAggregateDemo;

public static class DraftOrderJourney
{
    public static Guid OrderId { get; } = Guid.Parse("d1100000-0000-0000-0000-000000000001");
    public static Guid FirstLineId { get; } = Guid.Parse("d1100000-0000-0000-0000-000000000011");
    public static Guid SecondLineId { get; } = Guid.Parse("d1100000-0000-0000-0000-000000000012");
    public static Guid NewLineId { get; } = Guid.Parse("d1100000-0000-0000-0000-000000000013");

    public static async Task RunAsync(
        string connection,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(output);

        await using var provider = DraftOrderComposition
            .CreateServices(connection)
            .BuildServiceProvider(
                new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
            );
        await DraftOrderComposition.SetupAsync(provider, cancellationToken);
        await CommitAsync(
            provider,
            commands =>
                commands.CreateAsync(
                    new CreateDraftOrder(
                        OrderId,
                        SalesDemoSeed.AlphaCustomerId,
                        "DRAFT-1",
                        "EUR",
                        100,
                        [
                            new(FirstLineId, "DEMO-NOTEBOOK", 2, 10),
                            new(SecondLineId, "DEMO-NOTEBOOK", 1, 20),
                        ]
                    ),
                    cancellationToken
                ),
            cancellationToken
        );
        await CommitAsync(
            provider,
            commands =>
                commands.ChangeLinesAsync(
                    new ChangeDraftOrderLines(
                        OrderId,
                        1,
                        [
                            new DraftOrderLineChange.Change(FirstLineId, 3, 10),
                            new DraftOrderLineChange.Add(new(NewLineId, "DEMO-NOTEBOOK", 1, 15)),
                            new DraftOrderLineChange.Remove(SecondLineId),
                        ]
                    ),
                    cancellationToken
                ),
            cancellationToken
        );

        await using (var rejected = DraftOrderComposition.CreateScope(provider))
        {
            var database = rejected.ServiceProvider.GetRequiredService<SalesDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(
                cancellationToken
            );
            var result = await rejected
                .ServiceProvider.GetRequiredService<IDraftOrderCommands>()
                .ChangeLinesAsync(
                    new ChangeDraftOrderLines(
                        OrderId,
                        2,
                        [new DraftOrderLineChange.Change(NewLineId, 5, 15)]
                    ),
                    cancellationToken
                );
            if (
                result is not DraftOrderChangeResult.LimitExceeded { Limit: 100, Requested: 105 }
                || database.ChangeTracker.HasChanges()
            )
                throw new InvalidOperationException(
                    "The rejected order-limit decision left a partial proposal."
                );

            await transaction.RollbackAsync(cancellationToken);
        }

        await using (var customer = DraftOrderComposition.CreateScope(provider))
        {
            var result = await customer
                .ServiceProvider.GetRequiredService<ICustomerProfiles>()
                .ChangeAsync(
                    new CustomerProfileChange(
                        SalesDemoSeed.AlphaCustomerId,
                        SalesDemoSeed.AlphaAddressId,
                        1,
                        "Current customer name",
                        "Alpha Street"
                    ),
                    cancellationToken
                );
            if (result is not ProfileChangeResult.Updated)
                throw new InvalidOperationException("The separate customer edit did not commit.");
        }

        await using var read = DraftOrderComposition.CreateScope(provider);
        var summary = await read
            .ServiceProvider.GetRequiredService<IDraftOrderQueries>()
            .ReadSummaryAsync(OrderId, cancellationToken);
        if (
            summary
            is not { Version: 2, LineCount: 2, Total: 45, CustomerName: "Current customer name" }
        )
            throw new InvalidOperationException(
                "The joined query differs from the committed draft/customer state."
            );

        await output.WriteLineAsync(
            "Draft v2: 2 relational lines, total=45.00 EUR; limit=100.00 EUR."
        );
        await output.WriteLineAsync(
            "Added, changed and removed children; rejected total=105.00 EUR without changing state."
        );
        await output.WriteLineAsync(
            "Joined current customer name without changing the draft or reserving catalog stock."
        );
    }

    private static async Task CommitAsync(
        ServiceProvider provider,
        Func<IDraftOrderCommands, Task<DraftOrderChangeResult>> stage,
        CancellationToken cancellationToken
    )
    {
        await using var scope = DraftOrderComposition.CreateScope(provider);
        var database = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(
            cancellationToken
        );
        var result = await stage(scope.ServiceProvider.GetRequiredService<IDraftOrderCommands>());
        if (result is not DraftOrderChangeResult.Changed)
            throw new InvalidOperationException(
                "The draft operation did not stage an accepted proposal."
            );

        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }
}
