using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.IntegrationTests.Scenarios.EventPersistence;
using ModulithFoundry.Samples.Wholesale.Inventory.Contracts;

namespace ModulithFoundry.Samples.Wholesale.IntegrationTests.EventPersistence;

public sealed partial class AppendTests
{
    [Fact]
    public async Task IssueEligibilityUsesLoadedStockAndRejectsTheCompleteBatch()
    {
        string connection = await SeedAsync(true); // Version 3, on-hand 5.
        await using var provider = Provider(connection);
        await using (var rejected = Scope(provider, Alpha))
        {
            var database = Context(rejected, true);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.Equal(
                new StockPositionChangeResult.InsufficientStock(5, 6),
                await IssueAsync(rejected, 3, [3, 3])
            );
            Assert.Empty(database.ChangeTracker.Entries());
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }
        await AssertReadAsync(provider, true, (3, 5));
        await using (var write = Scope(provider, Alpha))
        {
            var database = Context(write, true);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.Equal((5, 1m), Proposed(await IssueAsync(write, 3, [2, 2])));
            await AssertReadAsync(provider, true, (3, 5));
            await database.SaveChangesAsync(Token);
            await AssertReadAsync(provider, true, (3, 5));
            await transaction.CommitAsync(Token);
        }
        await AssertReadAsync(provider, true, (5, 1));
        await using (var fresh = Scope(provider, Alpha))
        {
            var database = Context(fresh, true);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.Equal(
                new StockPositionChangeResult.InsufficientStock(1, 2),
                await IssueAsync(fresh, 5, [1, 1])
            );
            Assert.Empty(database.ChangeTracker.Entries());
            Assert.Equal((6, 0m), Proposed(await IssueAsync(fresh, 5, [1])));
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }
        await AssertReadAsync(provider, true, (6, 0)); // Includes deterministic live replay.
        Assert.Equal(6, await EventCountAsync(connection, true));
    }

    [Fact]
    public async Task CompetingEligibleIssuesRequireAFreshDecisionAfterTheLosingSave()
    {
        string connection = await SeedAsync(true);
        await using var provider = Provider(connection);
        await using (var winner = Scope(provider, Alpha))
        await using (var loser = Scope(provider, Alpha))
        {
            var winnerDatabase = Context(winner, true);
            var loserDatabase = Context(loser, true);
            await using var winningTransaction =
                await winnerDatabase.Database.BeginTransactionAsync(Token);
            await using var losingTransaction = await loserDatabase.Database.BeginTransactionAsync(
                Token
            );
            Assert.Equal((5, 1m), Proposed(await IssueAsync(winner, 3, [3, 1])));
            Assert.Equal((5, 1m), Proposed(await IssueAsync(loser, 3, [2, 2])));
            await winnerDatabase.SaveChangesAsync(Token);
            await winningTransaction.CommitAsync(Token);
            var failure = await Assert.ThrowsAnyAsync<DbUpdateException>(() =>
                loserDatabase.SaveChangesAsync(Token)
            );
            Assert.True(IsConflict(true, failure));
            await losingTransaction.RollbackAsync(CancellationToken.None);
        }
        await AssertReadAsync(provider, true, (5, 1));
        Assert.Equal(5, await EventCountAsync(connection, true));
        await using (var fresh = Scope(provider, Alpha))
        {
            var database = Context(fresh, true);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.Equal(
                new StockPositionChangeResult.InsufficientStock(1, 4),
                await IssueAsync(fresh, 5, [2, 2])
            );
            Assert.Empty(database.ChangeTracker.Entries());
            Assert.Equal((6, 0m), Proposed(await IssueAsync(fresh, 5, [1])));
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }
        await AssertReadAsync(provider, true, (6, 0));
    }

    [Theory]
    [InlineData("current")]
    [InlineData("later-event")]
    public async Task IssueParticipantFaultRollsBackAndFreshContextCanIssue(string fault)
    {
        string connection = await SeedAsync(true);
        await FaultAsync(connection, true, fault);
        await using var provider = Provider(connection);
        await using (var write = Scope(provider, Alpha))
        {
            var database = Context(write, true);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.Equal((5, 1m), Proposed(await IssueAsync(write, 3, [2, 2])));
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() =>
                database.SaveChangesAsync(Token)
            );
            Assert.False(IsConflict(true, failure));
            await transaction.RollbackAsync(CancellationToken.None);
        }
        await AssertReadAsync(provider, true, (3, 5));
        Assert.Equal(3, await EventCountAsync(connection, true));
        await ExecuteAsync(
            connection,
            fault == "current"
                ? "ALTER TABLE inventory.stock_position_current DROP CONSTRAINT test_view_fault"
                : "ALTER TABLE inventory.events DROP CONSTRAINT test_event_fault"
        );
        await using (var fresh = Scope(provider, Alpha))
        {
            var database = Context(fresh, true);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.Equal((5, 1m), Proposed(await IssueAsync(fresh, 3, [2, 2])));
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }
        await AssertReadAsync(provider, true, (5, 1));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("behind")]
    [InlineData("timestamp")]
    public async Task IssueCannotDecideFromADamagedRequiredView(string damage)
    {
        string connection = await SeedAsync(true);
        await ExecuteAsync(
            connection,
            damage switch
            {
                "missing" => "DELETE FROM inventory.stock_position_current",
                "behind" => "UPDATE inventory.stock_position_current SET version = 2",
                _ =>
                    "UPDATE inventory.stock_position_current SET recorded_at = recorded_at - interval '1 second'",
            }
        );
        await using var provider = Provider(connection);
        await using var write = Scope(provider, Alpha);
        var database = Context(write, true);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        await Assert.ThrowsAsync<InvalidDataException>(() => IssueAsync(write, 3, [1]));
        Assert.Empty(database.ChangeTracker.Entries());
        Assert.Equal(3, await EventCountAsync(connection, true));
    }

    [Fact]
    public async Task IssueRejectsAnAheadViewObservedAfterTheHeader()
    {
        string connection = await SeedAsync(true);
        await using var winner = Provider(connection);
        var observation = new QueryObservation("inventory")
        {
            BeforeInlineRead = async cancellation =>
            {
                await using var operation = Scope(winner, Alpha);
                var database = Context(operation, true);
                await using var transaction = await database.Database.BeginTransactionAsync(
                    cancellation
                );
                Proposed(await IssueAsync(operation, 3, [4]));
                await database.SaveChangesAsync(cancellation);
                await transaction.CommitAsync(cancellation);
            },
        };
        await using var provider = Provider(connection, observation);
        await using var loser = Scope(provider, Alpha);
        var loserDatabase = Context(loser, true);
        await using var loserTransaction = await loserDatabase.Database.BeginTransactionAsync(
            Token
        );
        Assert.IsType<StockPositionChangeResult.Conflict>(await IssueAsync(loser, 3, [2]));
        Assert.Empty(loserDatabase.ChangeTracker.Entries());
        await AssertReadAsync(winner, true, (4, 1));
    }

    [Fact]
    public async Task IssuePreservesTenantIsolationAndStaleVersionOutcomes()
    {
        string connection = await SeedAsync(true);
        await using var provider = Provider(connection);
        await using (var foreign = Scope(provider, Beta))
        {
            var database = Context(foreign, true);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.IsType<StockPositionChangeResult.NotFound>(await IssueAsync(foreign, 3, [1]));
            Assert.Empty(database.ChangeTracker.Entries());
            Proposed(await CreateAsync(foreign, true));
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }
        await using (var foreign = Scope(provider, Beta))
        {
            var database = Context(foreign, true);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Proposed(await AppendAsync(foreign, true, 1, [9]));
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }
        await using (var foreign = Scope(provider, Beta))
        {
            var database = Context(foreign, true);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.Equal((3, 1m), Proposed(await IssueAsync(foreign, 2, [8])));
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }
        await using (var stale = Scope(provider, Alpha))
        {
            var database = Context(stale, true);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.IsType<StockPositionChangeResult.Conflict>(await IssueAsync(stale, 2, [1]));
            Assert.Empty(database.ChangeTracker.Entries());
        }
        await AssertReadAsync(provider, true, (3, 5));
        await AssertReadAsync(provider, true, (3, 1), Beta);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InvalidIssueBatchCannotLeaveAPartiallyAcceptedProposal(bool overflow)
    {
        string connection = await SeedAsync(true);
        await using var provider = Provider(connection);
        await using var operation = Scope(provider, Alpha);
        var database = Context(operation, true);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        if (overflow)
            await Assert.ThrowsAsync<OverflowException>(() =>
                IssueAsync(operation, 3, [decimal.MaxValue, 1])
            );
        else
            await Assert.ThrowsAnyAsync<ArgumentException>(() => IssueAsync(operation, 3, [1, 0]));
        Assert.Empty(database.ChangeTracker.Entries());
        await AssertReadAsync(provider, true, (3, 5));
    }

    private static Task<StockPositionChangeResult> IssueAsync(
        AsyncServiceScope operation,
        long expected,
        decimal[] quantities
    ) =>
        operation
            .ServiceProvider.WithClock(Changed)
            .GetRequiredService<IStockPositionCommands>()
            .IssueAsync(
                new IssueStock(
                    Id,
                    expected,
                    quantities.Select(quantity => new StockIssue(quantity)).ToArray()
                ),
                Token
            );
}
