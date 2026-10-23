using System.Text.Json;
using Gloaming.EventSourcing.EntityFrameworkCore;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Gloaming.EventSourcing.Postgres.Tests.RebuildConsumer;

namespace Gloaming.EventSourcing.Postgres.Tests;

public sealed class ConcurrencyTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    [Fact]
    public async Task SameVersionRepairInvalidatesAWriterWhichDecidedFromCorruptState()
    {
        var (connection, id) = await SeedAsync(postgres);
        await ExecuteAsync(connection, "UPDATE ledger.balances SET \"State\" = '{\"Amount\":99}'");
        var commands = new CommandTrace();
        await using var writer = Open(connection, commands);
        await using var writeTransaction = await writer.Database.BeginTransactionAsync(Token);
        var store = new LedgerStore(writer);
        var aggregate = (await store.GetForWritingAsync(id, cancellationToken: Token))!;
        aggregate.Move(-90); // Eligible only under the corrupt observed state.
        await store.AppendAsync(aggregate, Token);
        var oldStamp = writer
            .ChangeTracker.Entries<LedgerStream>()
            .Single()
            .Property(row => row.ConcurrencyStamp)
            .OriginalValue;
        await using (var repair = Open(connection))
        {
            await using var transaction = await repair.Database.BeginTransactionAsync(Token);
            await new LedgerRebuilder(repair).RebuildAsync(id, Token);
            await repair.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }
        var failure = await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            writer.SaveChangesAsync(Token)
        );
        Assert.Contains(failure.Entries, entry => entry.Entity is LedgerStream);
        string executedSql = string.Join("\n", commands.Commands);
        Assert.Contains("UPDATE ledger.event_streams", executedSql, StringComparison.Ordinal);
        Assert.Contains("AND concurrency_stamp =", executedSql, StringComparison.Ordinal);
        Assert.Contains("AND version =", executedSql, StringComparison.Ordinal);
        await writeTransaction.RollbackAsync(Token);
        await AssertCommittedAsync(connection, id);
        await using var fresh = Open(connection);
        var header = await fresh.Set<LedgerStream>().AsNoTracking().SingleAsync(Token);
        Assert.NotEqual(oldStamp, header.ConcurrencyStamp);
        await using var freshTransaction = await fresh.Database.BeginTransactionAsync(Token);
        var restored = (
            await new LedgerStore(fresh).GetForWritingAsync(id, cancellationToken: Token)
        )!;
        Assert.Throws<InvalidOperationException>(() => restored.Move(-90));
        Assert.Empty(restored.PendingEvents);
    }

    [Fact]
    public async Task AppendInvalidatesAnEarlierCapturedRepairAndFreshReplayUsesNewHead()
    {
        var (connection, id) = await SeedAsync(postgres);
        await ExecuteAsync(connection, "UPDATE ledger.balances SET \"State\" = '{\"Amount\":99}'");
        await using var repair = Open(connection);
        await using var repairTransaction = await repair.Database.BeginTransactionAsync(Token);
        await new LedgerRebuilder(repair).RebuildAsync(id, Token);
        await using (var writer = Open(connection))
        {
            await using var transaction = await writer.Database.BeginTransactionAsync(Token);
            var store = new LedgerStore(writer);
            var aggregate = (await store.GetForWritingAsync(id, cancellationToken: Token))!;
            aggregate.Move(1);
            await store.AppendAsync(aggregate, Token);
            await writer.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
            repair.SaveChangesAsync(Token)
        );
        await repairTransaction.RollbackAsync(Token);
        await AssertCommittedAsync(connection, id, 100, 3);
        await using var fresh = Open(connection);
        await using var freshTransaction = await fresh.Database.BeginTransactionAsync(Token);
        Assert.Equal(3, (await new LedgerRebuilder(fresh).RebuildAsync(id, Token))!.Version);
        await fresh.SaveChangesAsync(Token);
        await freshTransaction.CommitAsync(Token);
        await AssertCommittedAsync(connection, id, 16, 3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompetingRepairsOrWritersHaveOneNativeWinner(bool rebuilding)
    {
        var (connection, id) = await SeedAsync(postgres);
        await using var first = Open(connection);
        await using var second = Open(connection);
        await using var firstTransaction = await first.Database.BeginTransactionAsync(Token);
        await using var secondTransaction = await second.Database.BeginTransactionAsync(Token);
        if (rebuilding)
        {
            await new LedgerRebuilder(first).RebuildAsync(id, Token);
            await new LedgerRebuilder(second).RebuildAsync(id, Token);
        }
        else
        {
            var firstStore = new LedgerStore(first);
            var secondStore = new LedgerStore(second);
            var a = (await firstStore.GetForWritingAsync(id, cancellationToken: Token))!;
            var b = (await secondStore.GetForWritingAsync(id, cancellationToken: Token))!;
            a.Move(1);
            b.Move(2);
            await firstStore.AppendAsync(a, Token);
            await secondStore.AppendAsync(b, Token);
        }
        await first.SaveChangesAsync(Token);
        await firstTransaction.CommitAsync(Token);
        await Assert.ThrowsAnyAsync<DbUpdateException>(() => second.SaveChangesAsync(Token));
        await secondTransaction.RollbackAsync(Token);
        await AssertCommittedAsync(connection, id, rebuilding ? 15 : 16, rebuilding ? 2 : 3);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SavedRepairIncludingSecondSaveCanRollbackThenRecoverFresh(bool missing)
    {
        var (connection, id) = await SeedAsync(postgres);
        await ExecuteAsync(
            connection,
            missing
                ? "DELETE FROM ledger.balances"
                : "UPDATE ledger.balances SET \"State\" = '{\"Amount\":99}'"
        );
        Guid originalStamp;
        await using (var repair = Open(connection))
        {
            originalStamp = (
                await repair.Set<LedgerStream>().AsNoTracking().SingleAsync(Token)
            ).ConcurrencyStamp;
            await using var transaction = await repair.Database.BeginTransactionAsync(Token);
            await new LedgerRebuilder(repair).RebuildAsync(id, Token);
            Assert.True(await repair.SaveChangesAsync(Token) > 0);
            Assert.Equal(0, await repair.SaveChangesAsync(Token));
            await transaction.RollbackAsync(Token);
        }
        await using (var read = Open(connection))
        {
            Assert.Equal(
                originalStamp,
                (await read.Set<LedgerStream>().SingleAsync(Token)).ConcurrencyStamp
            );
            if (missing)
                Assert.Empty(await read.Set<BalanceRow>().ToArrayAsync(Token));
            else
                Assert.Equal(
                    99,
                    (await read.Set<BalanceRow>().SingleAsync(Token))
                        .State.Deserialize<Balance>()!
                        .Amount
                );
        }
        await using var fresh = Open(connection);
        await using var freshTransaction = await fresh.Database.BeginTransactionAsync(Token);
        await new LedgerRebuilder(fresh).RebuildAsync(id, Token);
        await fresh.SaveChangesAsync(Token);
        await freshTransaction.CommitAsync(Token);
        await AssertCommittedAsync(connection, id);
    }
}
