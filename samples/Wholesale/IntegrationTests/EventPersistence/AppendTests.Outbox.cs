using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.IntegrationTests.Scenarios.EventPersistence;
using ModulithFoundry.Samples.Wholesale.Inventory;
using ModulithFoundry.Samples.Wholesale.Inventory.Contracts;

namespace ModulithFoundry.Samples.Wholesale.IntegrationTests.EventPersistence;

public sealed partial class AppendTests
{
    [Fact]
    public async Task AcceptedIssueCommitsOneNotificationForTheWholeBatchAndRejectionEmitsNothing()
    {
        string connection = await SeedAsync(true);
        await using var provider = Provider(connection);
        await using (var rejected = Scope(provider, Alpha))
        {
            var database = Context(rejected, true);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.IsType<StockPositionChangeResult.InsufficientStock>(
                await IssueAsync(rejected, 3, [3, 3])
            );
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }
        await AssertOutboxAsync(provider, 0);
        await using (var writer = Scope(provider, Alpha))
        {
            var database = Context(writer, true);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.Equal((5, 2m), Proposed(await IssueAsync(writer, 3, [2, 1])));
            await database.SaveChangesAsync(Token);
            await AssertOutboxAsync(provider, 0);
            await transaction.CommitAsync(Token);
        }
        await AssertReadAsync(provider, true, (5, 2m));
        Assert.Equal(5, await EventCountAsync(connection, true));
        await using var read = Scope(provider, Alpha);
        var row = Assert.Single(
            await Context(read, true).Set<OutboxMessageRecord>().ToArrayAsync(Token)
        );
        Assert.Equal(Alpha, row.TenantKey);
        Assert.Equal(row.MessageId, row.Payload.GetProperty("messageId").GetGuid());
        Assert.Equal(5, row.Payload.GetProperty("version").GetInt64());
        Assert.Equal(3m, row.Payload.GetProperty("issuedQuantity").GetDecimal());
        Assert.Equal(2m, row.Payload.GetProperty("remainingQuantity").GetDecimal());
        Assert.Equal(Changed, row.Payload.GetProperty("recordedAt").GetDateTimeOffset());
        await using var foreign = Scope(provider, Beta);
        Assert.Empty(await Context(foreign, true).Set<OutboxMessageRecord>().ToArrayAsync(Token));
    }

    [Theory]
    [InlineData("outbox")]
    [InlineData("current")]
    [InlineData("header")]
    [InlineData("later-event")]
    public async Task FailureInAnyParticipantRollsBackEventsStateAndNotificationThenFreshIssueRecovers(
        string fault
    )
    {
        string connection = await SeedAsync(true);
        if (fault == "outbox")
            await ExecuteAsync(
                connection,
                "ALTER TABLE inventory.outbox_messages ADD CONSTRAINT test_outbox CHECK (schema_version > 1)"
            );
        else
            await FaultAsync(connection, true, fault);
        await using var provider = Provider(connection);
        await using (var writer = Scope(provider, Alpha))
        {
            var database = Context(writer, true);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Proposed(await IssueAsync(writer, 3, [2, 1]));
            await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync(Token));
            await transaction.RollbackAsync(Token);
        }
        await AssertReadAsync(provider, true, (3, 5m));
        Assert.Equal(3, await EventCountAsync(connection, true));
        await AssertOutboxAsync(provider, 0);
        string table = fault switch
        {
            "outbox" => "outbox_messages",
            "current" => CurrentTable(true),
            "header" => "event_streams",
            _ => "events",
        };
        string constraint = fault switch
        {
            "outbox" => "test_outbox",
            "current" => "test_view_fault",
            "header" => "test_header_fault",
            _ => "test_event_fault",
        };
        await ExecuteAsync(
            connection,
            $"ALTER TABLE inventory.{table} DROP CONSTRAINT {constraint}"
        );
        await using var fresh = Scope(provider, Alpha);
        var recovered = Context(fresh, true);
        await using var recovery = await recovered.Database.BeginTransactionAsync(Token);
        Proposed(await IssueAsync(fresh, 3, [2, 1]));
        await recovered.SaveChangesAsync(Token);
        await recovery.CommitAsync(Token);
        await AssertOutboxAsync(provider, 1);
    }

    [Fact]
    public async Task RollbackAndCompetingIssuesLeaveOnlyTheWinningNotificationAndRebuildHasNoExternalEffects()
    {
        string connection = await SeedAsync(true);
        await using var provider = RebuildProvider(connection);
        await using (var rolledBack = Scope(provider, Alpha))
        {
            var database = Context(rolledBack, true);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Proposed(await IssueAsync(rolledBack, 3, [1]));
            await database.SaveChangesAsync(Token);
            await transaction.RollbackAsync(Token);
        }
        await AssertOutboxAsync(provider, 0);
        await using (var winner = Scope(provider, Alpha))
        await using (var loser = Scope(provider, Alpha))
        {
            var first = Context(winner, true);
            var second = Context(loser, true);
            await using var winningTransaction = await first.Database.BeginTransactionAsync(Token);
            await using var losingTransaction = await second.Database.BeginTransactionAsync(Token);
            Proposed(await IssueAsync(winner, 3, [4]));
            Proposed(await IssueAsync(loser, 3, [3]));
            await first.SaveChangesAsync(Token);
            await winningTransaction.CommitAsync(Token);
            var conflict = await Assert.ThrowsAnyAsync<DbUpdateException>(() =>
                second.SaveChangesAsync(Token)
            );
            Assert.True(IsConflict(true, conflict));
            await losingTransaction.RollbackAsync(Token);
        }
        await AssertReadAsync(provider, true, (4, 1m));
        await AssertOutboxAsync(provider, 1);
        await using (var repair = Scope(provider, Alpha))
        {
            var database = Context(repair, true);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Rebuilt(await RebuildAsync(repair, true));
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }
        await AssertOutboxAsync(provider, 1);
    }

    [Fact]
    public async Task OutboxUsesTheExistingTrustedOwnershipSavePolicy()
    {
        string connection = await DatabaseAsync();
        await using var provider = Provider(connection);
        await using var scope = Scope(provider, Alpha);
        var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        scope
            .ServiceProvider.GetRequiredService<IOutbox<InventoryDbContext>>()
            .Enqueue(
                new OutgoingMessage(
                    Guid.NewGuid(),
                    "d",
                    "m",
                    1,
                    System.Text.Json.JsonSerializer.SerializeToElement(new { amount = 1 }),
                    Beta
                )
            );
        await Assert.ThrowsAsync<TenantOwnershipException>(() => database.SaveChangesAsync(Token));
        await transaction.RollbackAsync(Token);
        await AssertOutboxAsync(provider, 0);
    }

    private static async Task AssertOutboxAsync(ServiceProvider provider, int expected)
    {
        await using var scope = Scope(provider, Alpha);
        Assert.Equal(
            expected,
            await Context(scope, true).Set<OutboxMessageRecord>().CountAsync(Token)
        );
    }
}
