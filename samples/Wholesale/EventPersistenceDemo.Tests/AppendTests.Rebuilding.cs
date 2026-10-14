using System.Text.Json;
using Gloaming.Events.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Inventory;
using ModulithFoundry.Samples.Wholesale.Inventory.Contracts;
using ModulithFoundry.Samples.Wholesale.Purchasing;
using ModulithFoundry.Samples.Wholesale.Purchasing.Contracts;

namespace ModulithFoundry.Samples.Wholesale.EventPersistenceDemo.Tests;

public sealed partial class AppendTests
{
    [Theory]
    [InlineData(true, "missing")]
    [InlineData(false, "missing")]
    [InlineData(true, "behind")]
    [InlineData(false, "behind")]
    [InlineData(true, "corrupt")]
    [InlineData(false, "corrupt")]
    public async Task RebuildRestoresAggregateStateAndFreshCommandsUseCorrectState(
        bool inventory,
        string damage
    )
    {
        string connection = await SeedAsync(inventory);
        string table = $"{Schema(inventory)}.{CurrentTable(inventory)}";
        await ExecuteAsync(
            connection,
            damage switch
            {
                "missing" => $"DELETE FROM {table}",
                "behind" => $"UPDATE {table} SET version = 1, state = '[]'::jsonb",
                "corrupt" => $"UPDATE {table} SET state = '[]'::jsonb",
                _ => throw new ArgumentOutOfRangeException(nameof(damage)),
            }
        );
        await using var provider = RebuildProvider(connection);
        await using (var repair = Scope(provider, Alpha))
        {
            var database = Context(repair, inventory);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.Equal((3, Changed), Rebuilt(await RebuildAsync(repair, inventory)));
            Assert.Equal(2, database.ChangeTracker.Entries().Count());
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }
        await AssertReadAsync(provider, inventory, (3, inventory ? 5m : 37.5m));
        Assert.Equal(3, await EventCountAsync(connection, inventory));
        await using (var write = Scope(provider, Alpha))
        {
            var database = Context(write, inventory);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            if (inventory)
            {
                var commands = write
                    .ServiceProvider.WithClock(Changed)
                    .GetRequiredService<IStockPositionCommands>();
                Assert.IsType<StockPositionChangeResult.InsufficientStock>(
                    await commands.IssueAsync(new IssueStock(Id, 3, [new StockIssue(6)]), Token)
                );
                Assert.Empty(database.ChangeTracker.Entries());
                Assert.Equal(
                    (4, 1m),
                    Proposed(
                        await commands.IssueAsync(new IssueStock(Id, 3, [new StockIssue(4)]), Token)
                    )
                );
            }
            else
                Assert.Equal((5, 100m), Proposed(await AppendAsync(write, false, 3, WinningBatch)));
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }
        await AssertReadAsync(provider, inventory, inventory ? (4, 1m) : (5, 100m));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RepairRollbackAndForeignTenantLeaveHistoryAndServingStateAlone(bool inventory)
    {
        string connection = await SeedAsync(inventory);
        await using var provider = RebuildProvider(connection);
        await using (var foreign = Scope(provider, Beta))
        {
            var database = Context(foreign, inventory);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.True(
                await RebuildAsync(foreign, inventory)
                    is StockPositionRebuildResult.NotFound
                        or PurchaseOrderRebuildResult.NotFound
            );
            Assert.Empty(database.ChangeTracker.Entries());
        }
        await using (var repair = Scope(provider, Alpha))
        {
            var database = Context(repair, inventory);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Rebuilt(await RebuildAsync(repair, inventory));
            await database.SaveChangesAsync(Token);
            await transaction.RollbackAsync(Token);
        }
        await AssertReadAsync(provider, inventory, (3, inventory ? 5m : 37.5m));
        await using var fresh = Scope(provider, Alpha);
        var freshDatabase = Context(fresh, inventory);
        await using var freshTransaction = await freshDatabase.Database.BeginTransactionAsync(
            Token
        );
        Rebuilt(await RebuildAsync(fresh, inventory));
        await freshDatabase.SaveChangesAsync(Token);
        await freshTransaction.CommitAsync(Token);
        Assert.Equal(3, await EventCountAsync(connection, inventory));
    }

    [Fact]
    public async Task PurchasingStateSqlFailureRollsBackReplacementAndFreshRepairRestoresDerivedSummary()
    {
        string connection = await SeedAsync(false);
        await ExecuteAsync(
            connection,
            "UPDATE purchasing.purchase_order_current SET state = '[]'::jsonb; ALTER TABLE purchasing.purchase_order_current ADD CONSTRAINT repair_fault CHECK (state = '[]'::jsonb) NOT VALID"
        );
        await using var provider = RebuildProvider(connection);
        await using (var repair = Scope(provider, Alpha))
        {
            var database = Context(repair, false);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Rebuilt(await RebuildAsync(repair, false));
            await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync(Token));
            await transaction.RollbackAsync(Token);
        }
        await using (var read = Scope(provider, Alpha))
            await Assert.ThrowsAnyAsync<JsonException>(() =>
                read
                    .ServiceProvider.GetRequiredService<IPurchaseOrderQueries>()
                    .ReadSummaryAsync(Id, Token)
            );
        await ExecuteAsync(
            connection,
            "ALTER TABLE purchasing.purchase_order_current DROP CONSTRAINT repair_fault"
        );
        await using (var repair = Scope(provider, Alpha))
        {
            var database = Context(repair, false);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Rebuilt(await RebuildAsync(repair, false));
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }
        await AssertReadAsync(provider, false, (3, 37.5m));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RealConsumerAppendInvalidatesCapturedRepairAndFreshReplayRecovers(
        bool inventory
    )
    {
        string connection = await SeedAsync(inventory);
        await using var provider = RebuildProvider(connection);
        await using var writer = Scope(provider, Alpha);
        var writerDatabase = Context(writer, inventory);
        await using var writerTransaction = await writerDatabase.Database.BeginTransactionAsync(
            Token
        );
        Proposed(await AppendAsync(writer, inventory, 3, WinningBatch));
        await using var repair = Scope(provider, Alpha);
        var repairDatabase = Context(repair, inventory);
        await using var repairTransaction = await repairDatabase.Database.BeginTransactionAsync(
            Token
        );
        Assert.Equal((3, Changed), Rebuilt(await RebuildAsync(repair, inventory)));
        await writerDatabase.SaveChangesAsync(Token);
        await writerTransaction.CommitAsync(Token);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            repairDatabase.SaveChangesAsync(Token)
        );
        await repairTransaction.RollbackAsync(Token);
        await using (var fresh = Scope(provider, Alpha))
        {
            var database = Context(fresh, inventory);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.Equal((5, Changed), Rebuilt(await RebuildAsync(fresh, inventory)));
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }
        await AssertReadAsync(provider, inventory, (5, inventory ? 20m : 100m));
    }

    [Fact]
    public async Task ExecutableMaintenanceJourneyUsesBothModuleContracts()
    {
        string connection = await DatabaseAsync();
        using var output = new StringWriter();
        await RebuildJourney.RunAsync(connection, output, Token);
        Assert.Contains("inventory rebuilt", output.ToString(), StringComparison.Ordinal);
        Assert.Contains("purchasing rebuilt", output.ToString(), StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AnotherAdmittedOwnerCanWriteSameIdDuringUncommittedRepair(bool inventory)
    {
        string connection = await SeedAsync(inventory);
        await using var provider = RebuildProvider(connection);
        await using var repair = Scope(provider, Alpha);
        var database = Context(repair, inventory);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        Rebuilt(await RebuildAsync(repair, inventory));
        await using var other = Scope(provider, Beta);
        var otherDatabase = Context(other, inventory);
        await using var otherTransaction = await otherDatabase.Database.BeginTransactionAsync(
            Token
        );
        Assert.Equal(
            (1, 0m),
            Proposed(await CreateAsync(other, inventory).WaitAsync(TimeSpan.FromSeconds(5), Token))
        );
        await otherDatabase.SaveChangesAsync(Token);
        await otherTransaction.CommitAsync(Token);
        await database.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
        await AssertReadAsync(provider, inventory, (1, 0m), Beta);
        await AssertReadAsync(provider, inventory, (3, inventory ? 5m : 37.5m));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    [InlineData(false, true)]
    public async Task ReplayRetainsConsumerSchemaAndDomainSequenceFailuresBeforeTracking(
        bool inventory,
        bool invalidSequence
    )
    {
        string connection = await SeedAsync(inventory);
        string table = $"{Schema(inventory)}.events";
        await ExecuteAsync(
            connection,
            invalidSequence
                ? $"UPDATE {table} AS target SET event_name = source.event_name, schema_version = source.schema_version, payload = source.payload FROM {table} AS source WHERE target.stream_version = 1 AND source.stream_version = 2"
                : $"UPDATE {table} SET schema_version = 999 WHERE stream_version = 1"
        );
        await using var provider = RebuildProvider(connection);
        await using var repair = Scope(provider, Alpha);
        var database = Context(repair, inventory);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        if (invalidSequence)
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                RebuildAsync(repair, inventory)
            );
        else
            await Assert.ThrowsAsync<EventDecodingException>(() => RebuildAsync(repair, inventory));
        Assert.Empty(database.ChangeTracker.Entries());
        await transaction.RollbackAsync(Token);
        await using var read = Scope(provider, Alpha);
        Assert.Equal((3, inventory ? 5m : 37.5m), await CurrentAsync(read, inventory));
        Assert.Equal(3, await EventCountAsync(connection, inventory));
    }

    private static ServiceProvider RebuildProvider(string connection)
    {
        var services = DemoComposition.CreateServices(connection);
        services.AddStockPositionRebuilding();
        services.AddPurchaseOrderRebuilding();
        services.AddScoped<TestClock>();
        services.AddScoped<TimeProvider>(provider => provider.GetRequiredService<TestClock>());
        return services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
        );
    }

    private static async Task<object> RebuildAsync(AsyncServiceScope scope, bool inventory) =>
        inventory
            ? await scope
                .ServiceProvider.GetRequiredService<IStockPositionRebuilding>()
                .RebuildAsync(Id, Token)
            : await scope
                .ServiceProvider.GetRequiredService<IPurchaseOrderRebuilding>()
                .RebuildAsync(Id, Token);

    private static (long Version, DateTimeOffset RecordedAt) Rebuilt(object result) =>
        result switch
        {
            StockPositionRebuildResult.Changed staged => (staged.Version, staged.RecordedAt),
            PurchaseOrderRebuildResult.Changed staged => (staged.Version, staged.RecordedAt),
            _ => throw new InvalidOperationException("Expected a staged rebuild."),
        };
}
