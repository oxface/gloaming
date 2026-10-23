using Gloaming.Samples.Wholesale.Inventory;
using Gloaming.Samples.Wholesale.Inventory.Contracts;
using Gloaming.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gloaming.Samples.Wholesale.IntegrationTests.Scenarios.EventPersistence;

public static class StockIssueJourney
{
    private static readonly Guid Id = Guid.Parse("e5010000-0000-0000-0000-000000000001");

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
        await using (var read = Scope(provider))
        {
            var current = await read
                .ServiceProvider.GetRequiredService<IStockPositionQueries>()
                .ReadCurrentAsync(Id, cancellationToken);
            if (current is null)
                await CommitAsync(
                    provider,
                    FixtureHistories.OpenedAt,
                    commands =>
                        commands.OpenAsync(
                            new OpenStockPosition(
                                Id,
                                Guid.Parse("11111111-1111-1111-1111-111111111111"),
                                Guid.Parse("22222222-2222-2222-2222-222222222222"),
                                "EA"
                            ),
                            cancellationToken
                        ),
                    cancellationToken
                );
        }
        await using (var read = Scope(provider))
        {
            var current = (
                await read
                    .ServiceProvider.GetRequiredService<IStockPositionQueries>()
                    .ReadCurrentAsync(Id, cancellationToken)
            )!;
            if (current.Version == 1)
                await CommitAsync(
                    provider,
                    FixtureHistories.FirstChangeAt,
                    commands =>
                        commands.ReceiveAsync(
                            new ReceiveStock(
                                Id,
                                current.Version,
                                [new StockReceipt(7), new StockReceipt(6)]
                            ),
                            cancellationToken
                        ),
                    cancellationToken
                );
        }
        await using (var read = Scope(provider))
        {
            var current = (
                await read
                    .ServiceProvider.GetRequiredService<IStockPositionQueries>()
                    .ReadCurrentAsync(Id, cancellationToken)
            )!;
            if (current.Version == 3)
                await CommitAsync(
                    provider,
                    FixtureHistories.LastStockChangeAt,
                    commands =>
                        commands.IssueAsync(
                            new IssueStock(
                                Id,
                                current.Version,
                                [new StockIssue(4), new StockIssue(3)]
                            ),
                            cancellationToken
                        ),
                    cancellationToken
                );
        }
        await using var final = Scope(provider);
        var committed = (
            await final
                .ServiceProvider.GetRequiredService<IStockPositionQueries>()
                .ReadCurrentAsync(Id, cancellationToken)
        )!;
        var database = final.ServiceProvider.GetRequiredService<InventoryDbContext>();
        var queries = final.ServiceProvider.GetRequiredService<IStockPositionQueries>();
        if (
            !(await queries.ReadAvailableAsync(6, cancellationToken)).Any(row => row.Id == Id)
            || (await queries.ReadAvailableAsync(7, cancellationToken)).Any(row => row.Id == Id)
        )
            throw new InvalidOperationException(
                "The inline availability filter disagrees with committed stock."
            );
        await using var rejection = await database.Database.BeginTransactionAsync(
            cancellationToken
        );
        var result = await final
            .ServiceProvider.WithClock(FixtureHistories.LastStockChangeAt)
            .GetRequiredService<IStockPositionCommands>()
            .IssueAsync(
                new IssueStock(Id, committed.Version, [new StockIssue(7)]),
                cancellationToken
            );
        if (
            result is not StockPositionChangeResult.InsufficientStock { Available: 6, Requested: 7 }
        )
            throw new InvalidOperationException(
                "The stock-issue journey did not reject insufficient stock."
            );
        await rejection.RollbackAsync(cancellationToken);
        output.WriteLine(
            FormattableString.Invariant(
                $"inventory issues: committed-version={committed.Version}, on-hand={committed.OnHand:F3}, rejected=7"
            )
        );
    }

    private static async Task CommitAsync(
        ServiceProvider provider,
        DateTimeOffset time,
        Func<IStockPositionCommands, Task<StockPositionChangeResult>> stage,
        CancellationToken cancellationToken
    )
    {
        await using var operation = Scope(provider);
        var database = operation.ServiceProvider.GetRequiredService<InventoryDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(
            cancellationToken
        );
        try
        {
            if (
                await stage(
                    operation
                        .ServiceProvider.WithClock(time)
                        .GetRequiredService<IStockPositionCommands>()
                )
                is not StockPositionChangeResult.Changed
            )
                throw new InvalidOperationException("The stock-issue journey did not stage.");
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
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
