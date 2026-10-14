using Gloaming.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Inventory;
using ModulithFoundry.Samples.Wholesale.Inventory.Contracts;

namespace ModulithFoundry.Samples.Wholesale.EventPersistenceDemo;

// Explicit shape-compatible replay and maintenance; the host owns native completion.
public static class SchemaEvolutionJourney
{
    private static readonly Guid Id = Guid.Parse("e5030000-0000-0000-0000-000000000001");
    private const string Owner = "schema-evolution";

    public static async Task RunAsync(
        string connection,
        string fixtures,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        var services = DemoComposition.CreateServices(connection);
        services.AddStockPositionRebuilding();
        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
        );
        await using (var setup = Scope(provider))
        {
            var database = setup.ServiceProvider.GetRequiredService<InventoryDbContext>();
            await database.Database.MigrateAsync(cancellationToken);
            var current = await setup
                .ServiceProvider.GetRequiredService<IStockPositionQueries>()
                .ReadCurrentAsync(Id, cancellationToken);
            if (current is null)
            {
                await using var transaction = await database.Database.BeginTransactionAsync(
                    cancellationToken
                );
                InventoryHistorySeed.Add(database, Id, FixtureHistories.Inventory(fixtures));
                await database.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
        }
        await using (var write = Scope(provider))
        {
            var database = write.ServiceProvider.GetRequiredService<InventoryDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(
                cancellationToken
            );
            var current = (
                await write
                    .ServiceProvider.GetRequiredService<IStockPositionQueries>()
                    .ReadCurrentAsync(Id, cancellationToken)
            )!;
            if (current.Version == 3)
            {
                var result = await write
                    .ServiceProvider.WithClock(FixtureHistories.LastStockChangeAt.AddSeconds(10))
                    .GetRequiredService<IStockPositionCommands>()
                    .ReceiveAsync(
                        new ReceiveStock(Id, 3, [new StockReceipt(2)]),
                        cancellationToken
                    );
                if (result is not StockPositionChangeResult.Changed)
                    throw new InvalidOperationException("The schema example receipt was rejected.");
                await database.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);
            }
        }
        string retainedFacts;
        await using (var repair = Scope(provider))
        {
            var database = repair.ServiceProvider.GetRequiredService<InventoryDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(
                cancellationToken
            );
            retainedFacts = await ReadFactsAsync(database, cancellationToken);
            var result = await repair
                .ServiceProvider.GetRequiredService<IStockPositionRebuilding>()
                .RebuildAsync(Id, cancellationToken);
            if (result is not StockPositionRebuildResult.Changed)
                throw new InvalidOperationException("The schema example stream is missing.");
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        await using var read = Scope(provider);
        var readDatabase = read.ServiceProvider.GetRequiredService<InventoryDbContext>();
        if (retainedFacts != await ReadFactsAsync(readDatabase, cancellationToken))
            throw new InvalidOperationException("Rebuilding changed retained event facts.");
        var history = read.ServiceProvider.GetRequiredService<IStockPositionHistory>();
        var old = (await history.ReadAtVersionAsync(Id, 2, cancellationToken))!;
        var live = (await history.ReadCurrentAsync(Id, cancellationToken))!;
        var inline = (
            await read
                .ServiceProvider.GetRequiredService<IStockPositionQueries>()
                .ReadCurrentAsync(Id, cancellationToken)
        )!;
        output.WriteLine(
            FormattableString.Invariant(
                $"inventory schemas: retained-v1={old.OnHand:F3}, current-version={live.Version}, inline={inline.OnHand:F3}, rebuilt={live.OnHand:F3}"
            )
        );
    }

    private static Task<string> ReadFactsAsync(
        InventoryDbContext database,
        CancellationToken cancellationToken
    ) =>
        database
            .Database.SqlQuery<string>(
                $"""
                SELECT jsonb_agg(to_jsonb(e) ORDER BY stream_version)::text AS "Value"
                FROM inventory.events AS e WHERE organization_key = {Owner} AND stream_id = {Id}
                """
            )
            .SingleAsync(cancellationToken);

    private static AsyncServiceScope Scope(ServiceProvider provider)
    {
        var scope = provider.CreateAsyncScope();
        scope
            .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
            .Initialize(TenantContext.ForTenant(new TenantId(Owner)));
        return scope;
    }
}
