using Gloaming.EventSourcing.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Inventory.Contracts;

namespace ModulithFoundry.Samples.Wholesale.EventPersistenceDemo.Tests;

public sealed partial class AppendTests
{
    [Theory]
    [InlineData(true, "detached", false)]
    [InlineData(true, "version", true)]
    [InlineData(true, "time", false)]
    [InlineData(true, "token", true)]
    [InlineData(true, "event", false)]
    [InlineData(true, "header", true)]
    [InlineData(false, "detached", true)]
    [InlineData(false, "version", false)]
    [InlineData(false, "time", true)]
    [InlineData(false, "token", false)]
    [InlineData(false, "event", true)]
    [InlineData(false, "header", false)]
    [InlineData(true, "stamp", true)]
    [InlineData(false, "stamp", false)]
    public async Task SaveRejectsIncompleteOrChangedAggregateAppendAndFreshContextRecovers(
        bool inventory,
        string fault,
        bool synchronous
    )
    {
        string connection = await SeedAsync(inventory);
        await using var provider = Provider(connection);
        await using (var write = Scope(provider, Alpha))
        {
            var database = Context(write, inventory);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.Equal(
                (5, inventory ? 20m : 100m),
                Proposed(await AppendAsync(write, inventory, 3, WinningBatch))
            );
            database.ChangeTracker.DetectChanges();
            var main = database
                .ChangeTracker.Entries()
                .Single(entry =>
                    entry.Metadata.GetTableName()
                    == (inventory ? "stock_position_current" : "purchase_order_current")
                );
            switch (fault)
            {
                case "detached":
                    main.State = EntityState.Detached;
                    break;
                case "version":
                    main.Property("Version").CurrentValue = 4L;
                    break;
                case "time":
                    main.Property("RecordedAt").CurrentValue = Changed.AddSeconds(1);
                    break;
                case "token":
                    main.Property("Version").IsModified = false;
                    break;
                case "event":
                    database
                        .ChangeTracker.Entries()
                        .First(entry => entry.Entity is IStoredEventRecord)
                        .State = EntityState.Detached;
                    break;
                case "stamp":
                    var header = database
                        .ChangeTracker.Entries()
                        .Single(entry => entry.Entity is IEventStreamRecord);
                    header.Property("ConcurrencyStamp").CurrentValue = header
                        .Property("ConcurrencyStamp")
                        .OriginalValue;
                    break;
                case "header":
                    database
                        .ChangeTracker.Entries()
                        .Single(entry => entry.Entity is IEventStreamRecord)
                        .State = EntityState.Detached;
                    break;
            }
            if (synchronous)
                Assert.Throws<InvalidOperationException>(() => database.SaveChanges());
            else
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    database.SaveChangesAsync(Token)
                );
            await transaction.RollbackAsync(Token);
        }
        await AssertReadAsync(provider, inventory, (3, inventory ? 5m : 37.5m));
        Assert.Equal(3, await EventCountAsync(connection, inventory));
        await using (var fresh = Scope(provider, Alpha))
        {
            var database = Context(fresh, inventory);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Proposed(await AppendAsync(fresh, inventory, 3, WinningBatch));
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }
        await AssertReadAsync(provider, inventory, (5, inventory ? 20m : 100m));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RegisteredSaveRequiresAnExplicitNativeTransaction(bool inventory)
    {
        string connection = await SeedAsync(inventory);
        await using var provider = Provider(connection);
        await using (var write = Scope(provider, Alpha))
        {
            var database = Context(write, inventory);
            var transaction = await database.Database.BeginTransactionAsync(Token);
            Proposed(await AppendAsync(write, inventory, 3, WinningBatch));
            await transaction.RollbackAsync(Token);
            await transaction.DisposeAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                database.SaveChangesAsync(Token)
            );
        }
        await AssertReadAsync(provider, inventory, (3, inventory ? 5m : 37.5m));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task AlreadyTrackedInlineStateRejectsTheEntireProposalBeforeStaging(bool inventory)
    {
        string connection = await SeedAsync(inventory);
        await using var provider = Provider(connection);
        await using (var write = Scope(provider, Alpha))
        {
            var database = Context(write, inventory);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            var model = database
                .Model.GetEntityTypes()
                .Single(type =>
                    type.GetTableName()
                    == (inventory ? "stock_position_current" : "purchase_order_current")
                );
            var tracked = await database.FindAsync(model.ClrType, [Alpha, Id], Token);
            Assert.NotNull(tracked);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                AppendAsync(write, inventory, 3, WinningBatch)
            );
            var entry = Assert.Single(database.ChangeTracker.Entries());
            Assert.Same(tracked, entry.Entity);
            Assert.Equal(EntityState.Unchanged, entry.State);
            Assert.Equal(3L, entry.Property("Version").CurrentValue);
            await transaction.RollbackAsync(Token);
        }
        await AssertReadAsync(provider, inventory, (3, inventory ? 5m : 37.5m));
        Assert.Equal(3, await EventCountAsync(connection, inventory));
    }

    [Fact]
    public async Task AvailabilityFiltersInlineStateInPostgreSqlWithoutReadingEvents()
    {
        string connection = await SeedAsync(true);
        var observation = new QueryObservation("inventory");
        await using var provider = Provider(connection, observation);
        await using (var read = Scope(provider, Alpha))
        {
            var queries = read.ServiceProvider.GetRequiredService<IStockPositionQueries>();
            var available = Assert.Single(await queries.ReadAvailableAsync(5, Token));
            Assert.Equal((Id, 3L, 5m), (available.Id, available.Version, available.OnHand));
            Assert.Empty(await queries.ReadAvailableAsync(6, Token));
            await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
                queries.ReadAvailableAsync(0, Token)
            );
        }
        await using (var read = Scope(provider, Beta))
            Assert.Empty(
                await read
                    .ServiceProvider.GetRequiredService<IStockPositionQueries>()
                    .ReadAvailableAsync(1, Token)
            );
        Assert.DoesNotContain(
            observation.Commands,
            command => command.Sql.Contains("FROM inventory.events", StringComparison.Ordinal)
        );
        Assert.Contains(
            observation.Commands,
            command =>
                command.Sql.Contains("onHand", StringComparison.Ordinal)
                && command.Sql.Contains(">=", StringComparison.Ordinal)
        );
    }
}
