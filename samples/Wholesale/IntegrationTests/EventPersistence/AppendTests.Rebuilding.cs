using Gloaming.Events.Serialization;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.IntegrationTests.Scenarios.EventPersistence;
using ModulithFoundry.Samples.Wholesale.Inventory;
using ModulithFoundry.Samples.Wholesale.Inventory.Contracts;

namespace ModulithFoundry.Samples.Wholesale.IntegrationTests.EventPersistence;

public sealed partial class AppendTests
{
    [Theory]
    [InlineData("missing")]
    [InlineData("behind")]
    [InlineData("corrupt")]
    public async Task RebuildRestoresAggregateStateAndFreshCommandsUseCorrectState(string damage)
    {
        string connection = await SeedAsync();
        string table = $"{Schema}.{CurrentTable}";
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
            var database = Context(repair);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.Equal((3, Changed), Rebuilt(await RebuildAsync(repair)));
            Assert.Equal(2, database.ChangeTracker.Entries().Count());
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }

        await AssertReadAsync(provider, (3, 5m));
        Assert.Equal(3, await EventCountAsync(connection));
        await using (var write = Scope(provider, Alpha))
        {
            var database = Context(write);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
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
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }

        await AssertReadAsync(provider, (4, 1m));
    }

    [Fact]
    public async Task RepairRollbackAndForeignTenantLeaveHistoryAndServingStateAlone()
    {
        string connection = await SeedAsync();
        await using var provider = RebuildProvider(connection);
        await using (var foreign = Scope(provider, Beta))
        {
            var database = Context(foreign);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.True(await RebuildAsync(foreign) is StockPositionRebuildResult.NotFound);
            Assert.Empty(database.ChangeTracker.Entries());
        }

        await using (var repair = Scope(provider, Alpha))
        {
            var database = Context(repair);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Rebuilt(await RebuildAsync(repair));
            await database.SaveChangesAsync(Token);
            await transaction.RollbackAsync(Token);
        }

        await AssertReadAsync(provider, (3, 5m));
        await using var fresh = Scope(provider, Alpha);
        var freshDatabase = Context(fresh);
        await using var freshTransaction = await freshDatabase.Database.BeginTransactionAsync(
            Token
        );
        Rebuilt(await RebuildAsync(fresh));
        await freshDatabase.SaveChangesAsync(Token);
        await freshTransaction.CommitAsync(Token);
        Assert.Equal(3, await EventCountAsync(connection));
    }

    [Fact]
    public async Task RealConsumerAppendInvalidatesCapturedRepairAndFreshReplayRecovers()
    {
        string connection = await SeedAsync();
        await using var provider = RebuildProvider(connection);
        await using var writer = Scope(provider, Alpha);
        var writerDatabase = Context(writer);
        await using var writerTransaction = await writerDatabase.Database.BeginTransactionAsync(
            Token
        );
        Proposed(await AppendAsync(writer, 3, WinningBatch));
        await using var repair = Scope(provider, Alpha);
        var repairDatabase = Context(repair);
        await using var repairTransaction = await repairDatabase.Database.BeginTransactionAsync(
            Token
        );
        Assert.Equal((3, Changed), Rebuilt(await RebuildAsync(repair)));
        await writerDatabase.SaveChangesAsync(Token);
        await writerTransaction.CommitAsync(Token);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            repairDatabase.SaveChangesAsync(Token)
        );
        await repairTransaction.RollbackAsync(Token);
        await using (var fresh = Scope(provider, Alpha))
        {
            var database = Context(fresh);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.Equal((5, Changed), Rebuilt(await RebuildAsync(fresh)));
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }

        await AssertReadAsync(provider, (5, 20m));
    }

    [Fact]
    public async Task MaintenanceJourneyUsesInventoryContracts()
    {
        string connection = await DatabaseAsync();
        using var output = new StringWriter();
        await RebuildJourney.RunAsync(connection, output, Token);
        Assert.Contains("inventory rebuilt", output.ToString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnotherAdmittedOwnerCanWriteSameIdDuringUncommittedRepair()
    {
        string connection = await SeedAsync();
        await using var provider = RebuildProvider(connection);
        await using var repair = Scope(provider, Alpha);
        var database = Context(repair);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        Rebuilt(await RebuildAsync(repair));
        await using var other = Scope(provider, Beta);
        var otherDatabase = Context(other);
        await using var otherTransaction = await otherDatabase.Database.BeginTransactionAsync(
            Token
        );
        Assert.Equal(
            (1, 0m),
            Proposed(await CreateAsync(other).WaitAsync(TimeSpan.FromSeconds(5), Token))
        );
        await otherDatabase.SaveChangesAsync(Token);
        await otherTransaction.CommitAsync(Token);
        await database.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
        await AssertReadAsync(provider, (1, 0m), Beta);
        await AssertReadAsync(provider, (3, 5m));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReplayRetainsConsumerSchemaAndDomainSequenceFailuresBeforeTracking(
        bool invalidSequence
    )
    {
        string connection = await SeedAsync();
        string table = $"{Schema}.events";
        await ExecuteAsync(
            connection,
            invalidSequence
                ? $"UPDATE {table} AS target SET event_name = source.event_name, schema_version = source.schema_version, payload = source.payload FROM {table} AS source WHERE target.stream_version = 1 AND source.stream_version = 2"
                : $"UPDATE {table} SET schema_version = 999 WHERE stream_version = 1"
        );
        await using var provider = RebuildProvider(connection);
        await using var repair = Scope(provider, Alpha);
        var database = Context(repair);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        if (invalidSequence)
            await Assert.ThrowsAsync<InvalidOperationException>(() => RebuildAsync(repair));
        else
            await Assert.ThrowsAsync<EventDecodingException>(() => RebuildAsync(repair));
        Assert.Empty(database.ChangeTracker.Entries());
        await transaction.RollbackAsync(Token);
        await using var read = Scope(provider, Alpha);
        Assert.Equal((3, 5m), await CurrentAsync(read));
        Assert.Equal(3, await EventCountAsync(connection));
    }

    private static ServiceProvider RebuildProvider(string connection)
    {
        var services = DemoComposition.CreateServices(connection);
        services.AddStockPositionRebuilding();
        services.AddScoped<TestClock>();
        services.AddScoped<TimeProvider>(provider => provider.GetRequiredService<TestClock>());
        return services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
        );
    }

    private static async Task<StockPositionRebuildResult> RebuildAsync(AsyncServiceScope scope) =>
        await scope
            .ServiceProvider.GetRequiredService<IStockPositionRebuilding>()
            .RebuildAsync(Id, Token);

    private static (long Version, DateTimeOffset RecordedAt) Rebuilt(
        StockPositionRebuildResult result
    ) =>
        result switch
        {
            StockPositionRebuildResult.Changed staged => (staged.Version, staged.RecordedAt),
            _ => throw new InvalidOperationException("Expected a staged rebuild."),
        };
}
