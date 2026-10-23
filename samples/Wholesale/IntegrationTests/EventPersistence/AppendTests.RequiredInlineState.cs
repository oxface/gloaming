using Gloaming.EventSourcing.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.IntegrationTests.Scenarios.EventPersistence;
using ModulithFoundry.Samples.Wholesale.Inventory.Contracts;

namespace ModulithFoundry.Samples.Wholesale.IntegrationTests.EventPersistence;

public sealed partial class AppendTests
{
    [Theory]
    [InlineData("detached", false)]
    [InlineData("version", true)]
    [InlineData("time", false)]
    [InlineData("token", true)]
    [InlineData("event", false)]
    [InlineData("header", true)]
    [InlineData("stamp", true)]
    public async Task SaveRejectsIncompleteOrChangedAggregateAppendAndFreshContextRecovers(
        string fault,
        bool synchronous
    )
    {
        string connection = await SeedAsync();
        await using var provider = Provider(connection);
        await using (var write = Scope(provider, Alpha))
        {
            var database = Context(write);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.Equal((5, 20m), Proposed(await AppendAsync(write, 3, WinningBatch)));
            database.ChangeTracker.DetectChanges();
            var main = database
                .ChangeTracker.Entries()
                .Single(entry => entry.Metadata.GetTableName() == ("stock_position_current"));
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

        await AssertReadAsync(provider, (3, 5m));
        Assert.Equal(3, await EventCountAsync(connection));
        await using (var fresh = Scope(provider, Alpha))
        {
            var database = Context(fresh);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Proposed(await AppendAsync(fresh, 3, WinningBatch));
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }

        await AssertReadAsync(provider, (5, 20m));
    }

    [Fact]
    public async Task RegisteredSaveRequiresAnExplicitNativeTransaction()
    {
        string connection = await SeedAsync();
        await using var provider = Provider(connection);
        await using (var write = Scope(provider, Alpha))
        {
            var database = Context(write);
            var transaction = await database.Database.BeginTransactionAsync(Token);
            Proposed(await AppendAsync(write, 3, WinningBatch));
            await transaction.RollbackAsync(Token);
            await transaction.DisposeAsync();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                database.SaveChangesAsync(Token)
            );
        }

        await AssertReadAsync(provider, (3, 5m));
    }

    [Fact]
    public async Task AlreadyTrackedInlineStateRejectsTheEntireProposalBeforeStaging()
    {
        string connection = await SeedAsync();
        await using var provider = Provider(connection);
        await using (var write = Scope(provider, Alpha))
        {
            var database = Context(write);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            var model = database
                .Model.GetEntityTypes()
                .Single(type => type.GetTableName() == ("stock_position_current"));
            var tracked = await database.FindAsync(model.ClrType, [Alpha, Id], Token);
            Assert.NotNull(tracked);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                AppendAsync(write, 3, WinningBatch)
            );
            var entry = Assert.Single(database.ChangeTracker.Entries());
            Assert.Same(tracked, entry.Entity);
            Assert.Equal(EntityState.Unchanged, entry.State);
            Assert.Equal(3L, entry.Property("Version").CurrentValue);
            await transaction.RollbackAsync(Token);
        }

        await AssertReadAsync(provider, (3, 5m));
        Assert.Equal(3, await EventCountAsync(connection));
    }

    [Fact]
    public async Task AvailabilityFiltersInlineStateInPostgreSqlWithoutReadingEvents()
    {
        string connection = await SeedAsync();
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
