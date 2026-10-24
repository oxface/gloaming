using Gloaming.Samples.Wholesale.Inventory.Contracts;
using Gloaming.Samples.Wholesale.Sales;
using Gloaming.Samples.Wholesale.Sales.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gloaming.Samples.Wholesale.IntegrationTests.Scenarios.Cqrs;

public static class CqrsJourney
{
    public static async Task RunAsync(
        string connection,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(output);

        await using var provider = CqrsComposition
            .CreateServices(connection)
            .BuildServiceProvider(
                new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
            );
        await CqrsComposition.SetupAsync(provider, cancellationToken);
        await using (var read = CqrsComposition.CreateScope(provider))
        {
            var available = await read
                .ServiceProvider.GetRequiredService<IStockPositionQueries>()
                .ReadAvailableAsync(3, cancellationToken);
            if (available is not [{ Available: 3 }])
                throw new InvalidOperationException("Expected on-hand 10 minus reserved 7.");
        }

        foreach (
            var request in new[]
            {
                CqrsDemoData.Draft(CqrsDemoData.OrderId, 40),
                CqrsDemoData.Draft(Guid.Parse("c1000000-0000-0000-0000-000000000010"), 5),
                CqrsDemoData.Draft(Guid.Parse("c1000000-0000-0000-0000-000000000030"), 60),
            }
        )
        {
            await using var write = CqrsComposition.CreateScope(provider);
            var database = write.ServiceProvider.GetRequiredService<SalesDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(
                cancellationToken
            );
            var result = await write
                .ServiceProvider.GetRequiredService<IDraftOrderCommands>()
                .CreateAsync(request, cancellationToken);
            if (result is not DraftOrderChangeResult.Changed)
                throw new InvalidOperationException("Expected an accepted draft proposal.");

            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        await using (var edit = CqrsComposition.CreateScope(provider))
        {
            var database = edit.ServiceProvider.GetRequiredService<SalesDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(
                cancellationToken
            );
            var result = await edit
                .ServiceProvider.GetRequiredService<IDraftOrderCommands>()
                .ChangeLinesAsync(
                    new(
                        CqrsDemoData.OrderId,
                        1,
                        [new DraftOrderLineChange.Change(CqrsDemoData.LineId, 3, 15)]
                    ),
                    cancellationToken
                );
            if (result is not DraftOrderChangeResult.Changed { Proposed.Total: 45 })
                throw new InvalidOperationException("Expected a proposed total of 45.");

            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        await using (var rejected = CqrsComposition.CreateScope(provider))
        {
            var database = rejected.ServiceProvider.GetRequiredService<SalesDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(
                cancellationToken
            );
            var result = await rejected
                .ServiceProvider.GetRequiredService<IDraftOrderCommands>()
                .ChangeLinesAsync(
                    new(
                        CqrsDemoData.OrderId,
                        2,
                        [new DraftOrderLineChange.Change(CqrsDemoData.LineId, 7, 15)]
                    ),
                    cancellationToken
                );
            if (
                result is not DraftOrderChangeResult.LimitExceeded { Requested: 105 }
                || database.ChangeTracker.HasChanges()
            )
                throw new InvalidOperationException("Rejected decision changed the draft.");

            await transaction.RollbackAsync(cancellationToken);
        }

        await using var search = CqrsComposition.CreateScope(provider);
        var sales = search.ServiceProvider.GetRequiredService<SalesDbContext>();
        await using var snapshot = await sales.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.RepeatableRead,
            cancellationToken
        );
        var page = await search
            .ServiceProvider.GetRequiredService<IDraftOrderQueries>()
            .SearchAsync(
                new(CustomerNameContains: "Alpha", Currency: "EUR", MinimumTotal: 30, Limit: 1),
                cancellationToken
            );
        if (page.TotalCount != 2 || page.Items is not [{ Version: 2, Total: 45, LineCount: 1 }])
            throw new InvalidOperationException(
                "Filtered page/count differ from committed Sales data."
            );

        await snapshot.CommitAsync(cancellationToken);
        await output.WriteLineAsync(
            "Inventory availability=3 (10 on-hand minus 7 reserved); advisory read only."
        );
        await output.WriteLineAsync(
            "Draft v2 total=45 EUR; rejected total=105 without changing state."
        );
        await output.WriteLineAsync(
            "Sales joined search: count=2, page=1, first total=45 EUR; cheap draft excluded before paging."
        );
    }
}
