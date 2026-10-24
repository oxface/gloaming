using System.Text.Json;
using Gloaming.Events.History;
using Gloaming.EventSourcing.EntityFrameworkCore;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Gloaming.EventSourcing.Postgres.Tests.RebuildConsumer;

namespace Gloaming.EventSourcing.Postgres.Tests;

public sealed class RebuildTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    [Theory]
    [InlineData("missing")]
    [InlineData("behind")]
    [InlineData("corrupt")]
    [InlineData("correct")]
    public async Task FullReplayReplacesAggregateStateWithoutChangingHistoryAndSupportsFreshDecisions(
        string damage
    )
    {
        var (connection, id) = await SeedAsync(postgres);
        if (damage == "missing")
            await ExecuteAsync(connection, "DELETE FROM ledger.balances");
        if (damage == "behind")
            await ExecuteAsync(
                connection,
                "UPDATE ledger.balances SET \"Version\" = 1, \"State\" = '{\"Amount\":99}'"
            );
        if (damage == "corrupt")
            await ExecuteAsync(connection, "UPDATE ledger.balances SET \"State\" = '[]'");
        await using (var database = Open(connection))
        {
            var before = await database
                .Set<StoredEventRecord>()
                .AsNoTracking()
                .OrderBy(row => row.StreamVersion)
                .ToArrayAsync(Token);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            var result = await new LedgerRebuilder(database).RebuildAsync(id, Token);
            Assert.Equal(2, result!.Version);
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
            var after = await database
                .Set<StoredEventRecord>()
                .AsNoTracking()
                .OrderBy(row => row.StreamVersion)
                .ToArrayAsync(Token);
            Assert.Equal(
                before.Select(row =>
                    (row.EventId, row.StreamVersion, row.RecordedAt, row.Payload.GetRawText())
                ),
                after.Select(row =>
                    (row.EventId, row.StreamVersion, row.RecordedAt, row.Payload.GetRawText())
                )
            );
        }
        await AssertCommittedAsync(connection, id);
        await using (var database = Open(connection))
        {
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            var store = new LedgerStore(database);
            var aggregate = (await store.GetForWritingAsync(id, cancellationToken: Token))!;
            Assert.Throws<InvalidOperationException>(() => aggregate.Move(-16));
            Assert.Empty(aggregate.PendingEvents);
            aggregate.Move(-14);
            await store.AppendAsync(aggregate, Token);
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }
        await AssertCommittedAsync(connection, id, 1, 3);
    }

    [Theory]
    [InlineData("gap")]
    [InlineData("order")]
    [InlineData("duplicate")]
    [InlineData("time")]
    [InlineData("offset")]
    [InlineData("identity")]
    [InlineData("version")]
    [InlineData("null")]
    [InlineData("pending")]
    public async Task InvalidReplayFailsBeforeTracking(string fault)
    {
        var (connection, id) = await SeedAsync(postgres);
        await using var database = Open(connection);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        var action = () => new LedgerRebuilder(database, fault).RebuildAsync(id, Token);
        if (fault == "null")
            await Assert.ThrowsAsync<ArgumentNullException>(action);
        else
            await Assert.ThrowsAsync<InvalidDataException>(action);
        Assert.Empty(database.ChangeTracker.Entries());
        await transaction.RollbackAsync(Token);
        await AssertCommittedAsync(connection, id);
    }

    [Theory]
    [InlineData("schema")]
    [InlineData("position")]
    [InlineData("endpoint")]
    [InlineData("ahead")]
    [InlineData("candidate")]
    public async Task CorruptStoredInputsAndCandidateFailureDoNotStageReplacement(string fault)
    {
        var (connection, id) = await SeedAsync(postgres);
        if (fault == "schema")
            await ExecuteAsync(
                connection,
                "UPDATE ledger.events SET schema_version = 2 WHERE stream_version = 1"
            );
        if (fault == "position")
            await ExecuteAsync(connection, "DELETE FROM ledger.events WHERE stream_version = 1");
        if (fault == "endpoint")
            await ExecuteAsync(
                connection,
                "UPDATE ledger.event_streams SET created_at = created_at - INTERVAL '1 second'"
            );
        if (fault == "ahead")
            await ExecuteAsync(connection, "UPDATE ledger.balances SET \"Version\" = 3");
        await using var database = Open(connection);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        if (fault == "candidate")
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new LedgerRebuilder(database, failCandidate: true).RebuildAsync(id, Token)
            );
        else if (fault == "position")
            await Assert.ThrowsAsync<EventHistoryException>(() =>
                new LedgerRebuilder(database).RebuildAsync(id, Token)
            );
        else
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                new LedgerRebuilder(database).RebuildAsync(id, Token)
            );
        Assert.Empty(database.ChangeTracker.Entries());
        var row = await database.Set<BalanceRow>().AsNoTracking().SingleAsync(Token);
        Assert.Equal(15, row.State.Deserialize<Balance>()!.Amount);
    }

    [Fact]
    public async Task FailedReplacementAndRollbackRetainServingStateThenFreshRepairWorks()
    {
        var (connection, id) = await SeedAsync(postgres);
        await ExecuteAsync(
            connection,
            "UPDATE ledger.balances SET \"State\" = '{\"Amount\":99}'; ALTER TABLE ledger.balances ADD CONSTRAINT reject_repair CHECK ((\"State\" ->> 'Amount')::int <> 15) NOT VALID"
        );
        await using (var database = Open(connection))
        {
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            await new LedgerRebuilder(database).RebuildAsync(id, Token);
            await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync(Token));
            await transaction.RollbackAsync(Token);
        }
        await using (var read = Open(connection))
        {
            Assert.Equal(
                99,
                (await read.Set<BalanceRow>().SingleAsync(Token))
                    .State.Deserialize<Balance>()!
                    .Amount
            );
        }
        await ExecuteAsync(connection, "ALTER TABLE ledger.balances DROP CONSTRAINT reject_repair");
        await using (var database = Open(connection))
        {
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            await new LedgerRebuilder(database).RebuildAsync(id, Token);
            await database.SaveChangesAsync(Token);
            await transaction.RollbackAsync(Token);
        }
        await using (var database = Open(connection))
        {
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            await new LedgerRebuilder(database).RebuildAsync(id, Token);
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }
        await AssertCommittedAsync(connection, id);
    }

    [Fact]
    public async Task CancellingAnExecutingReplacementRollsBackForFreshRecovery()
    {
        var (connection, id) = await SeedAsync(postgres);
        await ExecuteAsync(
            connection,
            "UPDATE ledger.balances SET \"State\" = '{\"Amount\":99}'; CREATE FUNCTION ledger.slow_repair() RETURNS trigger LANGUAGE plpgsql AS 'BEGIN PERFORM pg_sleep(30); RETURN NEW; END'; CREATE TRIGGER slow_repair BEFORE UPDATE ON ledger.balances FOR EACH ROW EXECUTE FUNCTION ledger.slow_repair()"
        );
        await using (var database = Open(connection))
        {
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            int pid = await BackendAsync(database);
            await new LedgerRebuilder(database).RebuildAsync(id, Token);
            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
            var saving = database.SaveChangesAsync(cancellation.Token);
            await using var observer = new Npgsql.NpgsqlConnection(connection);
            await observer.OpenAsync(Token);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            while (true)
            {
                await using var command = new Npgsql.NpgsqlCommand(
                    "SELECT EXISTS (SELECT FROM pg_stat_activity WHERE pid = @pid AND wait_event = 'PgSleep')",
                    observer
                );
                command.Parameters.AddWithValue("pid", pid);
                if ((bool)(await command.ExecuteScalarAsync(timeout.Token))!)
                    break;
                await Task.Delay(20, timeout.Token);
            }
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => saving);
            await transaction.RollbackAsync(Token);
        }
        await using (var read = Open(connection))
        {
            Assert.Equal(
                99,
                (await read.Set<BalanceRow>().SingleAsync(Token))
                    .State.Deserialize<Balance>()!
                    .Amount
            );
        }
        await ExecuteAsync(
            connection,
            "DROP TRIGGER slow_repair ON ledger.balances; DROP FUNCTION ledger.slow_repair()"
        );
        await using (var fresh = Open(connection))
        {
            await using var transaction = await fresh.Database.BeginTransactionAsync(Token);
            await new LedgerRebuilder(fresh).RebuildAsync(id, Token);
            await fresh.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }
        await AssertCommittedAsync(connection, id);
    }
}
