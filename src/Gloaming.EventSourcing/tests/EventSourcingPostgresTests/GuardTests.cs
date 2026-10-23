using Gloaming.EventSourcing.EntityFrameworkCore;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Gloaming.EventSourcing.Postgres.Tests.RebuildConsumer;

namespace Gloaming.EventSourcing.Postgres.Tests;

public sealed class GuardTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    [Theory]
    [InlineData("detach")]
    [InlineData("version")]
    [InlineData("time")]
    [InlineData("original")]
    [InlineData("header")]
    [InlineData("event")]
    [InlineData("stamp")]
    [InlineData("body")]
    [InlineData("offset")]
    public async Task MaintenanceSaveRejectsOmissionsOrMetadataHistoryTampering(string tamper)
    {
        var (connection, id) = await SeedAsync(postgres);
        await ExecuteAsync(connection, "UPDATE ledger.balances SET \"State\" = '{\"Amount\":99}'");
        await using var database = Open(connection);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        await new LedgerRebuilder(database).RebuildAsync(id, Token);
        var row = database.ChangeTracker.Entries<BalanceRow>().Single();
        if (tamper == "detach")
            row.State = EntityState.Detached;
        if (tamper == "version")
            row.Entity.Version++;
        if (tamper == "offset")
            row.Entity.RecordedAt = row.Entity.RecordedAt.ToOffset(TimeSpan.FromHours(1));
        if (tamper == "time")
            row.Entity.RecordedAt = row.Entity.RecordedAt.AddSeconds(1);
        if (tamper == "original")
            row.Property(item => item.Version).OriginalValue = 1;
        if (tamper == "stamp")
            database
                .ChangeTracker.Entries<LedgerStream>()
                .Single()
                .Property(item => item.ConcurrencyStamp)
                .OriginalValue = Guid.NewGuid();
        if (tamper == "body")
            row.Entity.State = System.Text.Json.JsonSerializer.SerializeToElement(new Balance(99));
        if (tamper == "header")
            database.ChangeTracker.Entries<LedgerStream>().Single().Entity.Version++;
        if (tamper == "event")
            (await database.Set<StoredEventRecord>().FirstAsync(Token)).EventName = "tampered";
        if (tamper == "body")
            Assert.Throws<InvalidOperationException>(() => database.SaveChanges());
        else
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                database.SaveChangesAsync(Token)
            );
        await transaction.RollbackAsync(Token);
        await AssertCommittedAsync(connection, id, 99);
    }

    [Fact]
    public async Task DirectAggregateStateMutationIsRejected()
    {
        var (connection, id) = await SeedAsync(postgres);
        await using var database = Open(connection);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        (await database.Set<BalanceRow>().SingleAsync(Token)).State =
            System.Text.Json.JsonSerializer.SerializeToElement(new Balance(99));
        Assert.Throws<InvalidOperationException>(() => database.SaveChanges());
        await transaction.RollbackAsync(Token);
        await AssertCommittedAsync(connection, id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RebuildingRequiresItsNativeTransactionAndFreshTracker(bool tracked)
    {
        var (connection, id) = await SeedAsync(postgres);
        await using var database = Open(connection);
        await using var transaction = tracked
            ? await database.Database.BeginTransactionAsync(Token)
            : null;
        if (tracked)
            await database.Set<BalanceRow>().SingleAsync(Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new LedgerRebuilder(database).RebuildAsync(id, Token)
        );
        Assert.False(database.ChangeTracker.HasChanges());
        await AssertCommittedAsync(connection, id);
    }

    [Fact]
    public async Task MissingStateBindingCannotFallBackToRawAggregateLoading()
    {
        var (connection, _) = await SeedAsync(postgres);
        await using var database = Open(connection);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new UnconfiguredStore(database).GetForWritingAsync(
                Guid.NewGuid(),
                cancellationToken: Token
            )
        );
        Assert.Empty(database.ChangeTracker.Entries());
    }

    private sealed class UnconfiguredStore(LedgerDatabase database)
        : EventStore<Ledger, Movement, LedgerStream, StoredEventRecord>(
            database,
            new Mapping(),
            TimeProvider.System
        )
    {
        protected override LedgerStream CreateStream(Guid id) => new();
    }

    private sealed class Mapping : EventRecordMapping<Movement, LedgerStream, StoredEventRecord>
    {
        public override string StreamType => "ledger";

        public override StoredEventRecord ToRow(Movement @event, LedgerStream capturedHeader) =>
            throw new NotSupportedException();
    }
}
