using System.Data.Common;
using System.Text.Json;
using Gloaming.EventSourcing;
using Gloaming.EventSourcing.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ModulithFoundry.Tests.Infrastructure;
using Npgsql;

namespace ModulithFoundry.Samples.EventStorageDemo.Tests;

public sealed partial class AppendTests(PostgreSqlFixture postgres)
    : IClassFixture<PostgreSqlFixture>
{
    private static readonly int[] OneFact = [1];
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static DateTimeOffset RecordedAt => DemoData.RecordedAt;

    [Fact]
    public async Task HistoryDependentCounterStagesOnlyEligibleCompleteBatches()
    {
        string connection = await SeedAsync();
        await using (var write = Database(connection))
        {
            await using var transaction = await write.Database.BeginTransactionAsync(Token);
            var commands = new CounterCommands(write, new CounterClock());
            var staged = Assert.IsType<CounterChangeResult.Changed>(
                await commands.StageIncreaseAsync(DemoData.CounterId, 2, [3, 4], Token)
            );
            Assert.Equal(new CounterState(DemoData.CounterId, 4, 24, RecordedAt), staged.Proposed);
            await AssertCounterAsync(connection, 2, 17);
            await write.SaveChangesAsync(Token);
            await AssertCounterAsync(connection, 2, 17);
            await transaction.CommitAsync(Token);
        }
        await AssertCounterAsync(connection, 4, 24);
        await using (var fresh = Database(connection))
        {
            await using var transaction = await fresh.Database.BeginTransactionAsync(Token);
            var commands = new CounterCommands(fresh, new CounterClock());
            Assert.Equal(
                new CounterChangeResult.LimitExceeded(24, 2),
                await commands.StageIncreaseAsync(DemoData.CounterId, 4, [1, 1], Token)
            );
            Assert.Empty(fresh.ChangeTracker.Entries());
            var staged = Assert.IsType<CounterChangeResult.Changed>(
                await commands.StageIncreaseAsync(DemoData.CounterId, 4, [1], Token)
            );
            Assert.Equal(25, staged.Proposed.Value);
            await fresh.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }
        await AssertCounterAsync(connection, 5, 25);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompetingIndependentWritersCommitOneWholeBatch(bool creation)
    {
        string connection = await SeedAsync();
        Guid id = creation ? Guid.NewGuid() : DemoData.CounterId;
        await using (var winner = Database(connection))
        await using (var loser = Database(connection))
        {
            await using var winningTransaction = await winner.Database.BeginTransactionAsync(Token);
            await using var losingTransaction = await loser.Database.BeginTransactionAsync(Token);
            var winning = new CounterCommands(winner, new CounterClock());
            var losing = new CounterCommands(loser, new CounterClock());
            Assert.IsType<CounterChangeResult.Changed>(
                creation
                    ? await winning.StageStartAsync(id, 10, Token)
                    : await winning.StageIncreaseAsync(id, 2, [3, 2], Token)
            );
            Assert.IsType<CounterChangeResult.Changed>(
                creation
                    ? await losing.StageStartAsync(id, 12, Token)
                    : await losing.StageIncreaseAsync(id, 2, [4, 2], Token)
            );
            await winner.SaveChangesAsync(Token);
            await winningTransaction.CommitAsync(Token);
            var failure = await Assert.ThrowsAnyAsync<DbUpdateException>(() =>
                loser.SaveChangesAsync(Token)
            );
            if (creation)
                Assert.Equal(
                    "PK_streams",
                    Assert.IsType<PostgresException>(failure.InnerException).ConstraintName
                );
            else
                AssertVersionConflict(failure);
            await losingTransaction.RollbackAsync(CancellationToken.None);
        }
        await AssertCounterAsync(connection, creation ? 1 : 4, creation ? 10 : 22, id);
        await using var fresh = Database(connection);
        await using var transaction = await fresh.Database.BeginTransactionAsync(Token);
        var commands = new CounterCommands(fresh, new CounterClock());
        if (!creation)
        {
            Assert.Equal(
                new CounterChangeResult.LimitExceeded(22, 6),
                await commands.StageIncreaseAsync(id, 4, [4, 2], Token)
            );
            Assert.Empty(fresh.ChangeTracker.Entries());
            Assert.IsType<CounterChangeResult.Changed>(
                await commands.StageIncreaseAsync(id, 4, [3], Token)
            );
            await fresh.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
            await AssertCounterAsync(connection, 5, 25);
        }
    }

    [Fact]
    public async Task LaterEnvelopeFailureRollsBackAndFreshContextDecidesAgain()
    {
        string connection = await SeedAsync();
        await ExecuteAsync(
            connection,
            "ALTER TABLE journal.facts ADD CONSTRAINT es1_fault CHECK (stream_version <> 4) NOT VALID"
        );
        await using (var write = Database(connection))
        {
            await using var transaction = await write.Database.BeginTransactionAsync(Token);
            Assert.IsType<CounterChangeResult.Changed>(
                await new CounterCommands(write, new CounterClock()).StageIncreaseAsync(
                    DemoData.CounterId,
                    2,
                    [3, 2],
                    Token
                )
            );
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() =>
                write.SaveChangesAsync(Token)
            );
            Assert.Equal(
                PostgresErrorCodes.CheckViolation,
                Assert.IsType<PostgresException>(failure.InnerException).SqlState
            );
            await transaction.RollbackAsync(CancellationToken.None);
        }
        await AssertCounterAsync(connection, 2, 17);
        await ExecuteAsync(connection, "ALTER TABLE journal.facts DROP CONSTRAINT es1_fault");
        await using (var fresh = Database(connection))
        {
            await using var transaction = await fresh.Database.BeginTransactionAsync(Token);
            Assert.IsType<CounterChangeResult.Changed>(
                await new CounterCommands(fresh, new CounterClock()).StageIncreaseAsync(
                    DemoData.CounterId,
                    2,
                    [3, 2],
                    Token
                )
            );
            await fresh.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }
        await AssertCounterAsync(connection, 4, 22);
    }

    [Theory]
    [InlineData("position")]
    [InlineData("head-time")]
    public async Task CounterCannotDecideFromHistoryInconsistentWithItsObservedVersion(
        string damage
    )
    {
        string connection = await SeedAsync();
        await ExecuteAsync(
            connection,
            damage == "position"
                ? "UPDATE journal.facts SET stream_version = 3 WHERE event_name = 'proof.counter-increased'"
                : "UPDATE journal.streams SET updated_at = updated_at + interval '1 second' WHERE stream_type = 'proof.counter'"
        );
        await using var write = Database(connection);
        await using var transaction = await write.Database.BeginTransactionAsync(Token);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new CounterCommands(write, new CounterClock()).StageIncreaseAsync(
                DemoData.CounterId,
                2,
                [1],
                Token
            )
        );
        Assert.Empty(write.ChangeTracker.Entries());
        Assert.Equal(3, await write.Events.CountAsync(Token));
    }

    [Fact]
    public async Task CapturedHistoryRemainsBoundedWhenAWriterCommitsBetweenReads()
    {
        string connection = await SeedAsync();
        var observation = new BeforeHistoryRead(async () =>
        {
            await using var winner = Database(connection);
            await using var transaction = await winner.Database.BeginTransactionAsync(Token);
            Assert.IsType<CounterChangeResult.Changed>(
                await new CounterCommands(winner, new CounterClock()).StageIncreaseAsync(
                    DemoData.CounterId,
                    2,
                    [3],
                    Token
                )
            );
            await winner.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        });
        var options = new DbContextOptionsBuilder<StorageDbContext>(
            StorageDbContext.Options(connection)
        )
            .AddInterceptors(observation)
            .Options;
        await using (var loser = new StorageDbContext(options))
        {
            await using var transaction = await loser.Database.BeginTransactionAsync(Token);
            var proposed = Assert.IsType<CounterChangeResult.Changed>(
                await new CounterCommands(loser, new CounterClock()).StageIncreaseAsync(
                    DemoData.CounterId,
                    2,
                    [4],
                    Token
                )
            );
            Assert.Equal(21, proposed.Proposed.Value); // 17 at captured version 2, not winner's 20.
            var failure = await Assert.ThrowsAnyAsync<DbUpdateException>(() =>
                loser.SaveChangesAsync(Token)
            );
            AssertVersionConflict(failure);
            await transaction.RollbackAsync(CancellationToken.None);
        }
        await AssertCounterAsync(connection, 3, 20);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("later-encoder")]
    [InlineData("reused-row")]
    [InlineData("schema")]
    [InlineData("payload")]
    [InlineData("null-payload")]
    [InlineData("name")]
    [InlineData("aggregate-version")]
    [InlineData("null-fact")]
    [InlineData("cancelled")]
    [InlineData("regression")]
    [InlineData("non-utc")]
    public async Task StoreEncodingFailureCannotTrackAPartialBatch(string fault)
    {
        string connection = await SeedAsync();
        await using var database = Database(connection);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        var clock = new CounterClock
        {
            Now =
                fault == "regression" ? RecordedAt.AddSeconds(-1)
                : fault == "non-utc" ? RecordedAt.ToOffset(TimeSpan.FromHours(1))
                : RecordedAt,
        };
        var mapping = new TestRecordAdapter(fault);
        var store = new AmountStore(database, clock, records: mapping);
        var aggregate = (
            await store.GetForWritingAsync(DemoData.CounterId, cancellationToken: Token)
        )!;
        aggregate.Facts = fault == "empty" ? [] : [new(1), new(2)];
        aggregate.Version += aggregate.Facts.Length;
        if (fault == "aggregate-version")
            aggregate.Version++;
        if (fault == "null-fact")
            aggregate.Facts[1] = null!;
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var append = () =>
            store.AppendAsync(aggregate, fault == "cancelled" ? cancelled.Token : Token);
        if (fault == "cancelled")
            await Assert.ThrowsAsync<OperationCanceledException>(append);
        else if (fault == "later-encoder")
            Assert.Same(mapping.Failure, await Assert.ThrowsAsync<InvalidDataException>(append));
        else if (
            fault is "empty" or "reused-row" or "aggregate-version" or "regression" or "non-utc"
        )
            await Assert.ThrowsAsync<InvalidOperationException>(append);
        else
            await Assert.ThrowsAnyAsync<ArgumentException>(append);
        Assert.Empty(database.ChangeTracker.Entries());
        await AssertCounterAsync(connection, 2, 17);
    }

    [Fact]
    public async Task HistoricalFactsAreReadableWithoutReapplyingCurrentEligibility()
    {
        string connection = await SeedAsync();
        await using (var author = Database(connection))
        {
            await using var transaction = await author.Database.BeginTransactionAsync(Token);
            var store = new AmountStore(author, new CounterClock());
            var aggregate = (
                await store.GetForWritingAsync(DemoData.CounterId, cancellationToken: Token)
            )!;
            aggregate.Facts = [new(10)];
            aggregate.Version++;
            await store.AppendAsync(aggregate, Token);
            await author.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }
        await AssertCounterAsync(connection, 3, 27);
        await using var reader = Database(connection);
        var commands = new CounterCommands(reader, new CounterClock());
        Assert.NotNull(await commands.ReadAsync(DemoData.CounterId, Token));
        await using var decision = await reader.Database.BeginTransactionAsync(Token);
        Assert.Equal(
            new CounterChangeResult.LimitExceeded(27, 1),
            await commands.StageIncreaseAsync(DemoData.CounterId, 3, OneFact, Token)
        );
        Assert.Empty(reader.ChangeTracker.Entries());
    }

    [Fact]
    public async Task JsonElementPayloadSurvivesSourceDisposalAndFreshContextJsonbRoundTrip()
    {
        string connection = await SeedAsync();
        await using (var write = Database(connection))
        {
            await using var transaction = await write.Database.BeginTransactionAsync(Token);
            var clock = new CounterClock();
            using (
                var source = JsonDocument.Parse(
                    """
                    {"amount":3,"text":"café ☕ \"quoted\"\nnext","precision":12345.67890123456789,
                     "details":{"enabled":true,"values":["first",null,{"value":7}]}}
                    """
                )
            )
            {
                var store = new AmountStore(
                    write,
                    clock,
                    records: new TestRecordAdapter(payload: source.RootElement)
                );
                var aggregate = (
                    await store.GetForWritingAsync(DemoData.CounterId, cancellationToken: Token)
                )!;
                aggregate.Facts = [new(3)];
                aggregate.Version++;
                Assert.Equal(
                    new EventAppendResult(3, RecordedAt),
                    await store.AppendAsync(aggregate, Token)
                );
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    store.AppendAsync(aggregate, Token)
                );
                Assert.Equal(1, clock.Calls);
                var entry = write.ChangeTracker.Entries<EventStreamRecord>().Single();
                Assert.Equal(2, entry.Property(row => row.Version).OriginalValue);
                Assert.NotEqual(
                    entry.Property(row => row.ConcurrencyStamp).OriginalValue,
                    entry.Entity.ConcurrencyStamp
                );
            }
            await AssertCounterAsync(connection, 2, 17);
            await write.SaveChangesAsync(Token);
            await AssertCounterAsync(connection, 2, 17);
            await transaction.CommitAsync(Token);
        }
        await using var fresh = Database(connection);
        var stored = await fresh
            .Events.AsNoTracking()
            .SingleAsync(
                row => row.StreamId == DemoData.CounterId && row.StreamVersion == 3,
                Token
            );
        JsonElement payload = stored.Payload;
        Assert.Equal(3, payload.GetProperty("amount").GetInt32());
        Assert.Equal("café ☕ \"quoted\"\nnext", payload.GetProperty("text").GetString());
        Assert.Equal(12345.67890123456789m, payload.GetProperty("precision").GetDecimal());
        var details = payload.GetProperty("details");
        Assert.True(details.GetProperty("enabled").GetBoolean());
        var values = details.GetProperty("values");
        Assert.Equal("first", values[0].GetString());
        Assert.Equal(JsonValueKind.Null, values[1].ValueKind);
        Assert.Equal(7, values[2].GetProperty("value").GetInt32());
        await using (var database = new NpgsqlConnection(connection))
        {
            await database.OpenAsync(Token);
            await using var command = new NpgsqlCommand(
                "SELECT DISTINCT pg_typeof(payload)::text FROM journal.facts",
                database
            );
            Assert.Equal("jsonb", await command.ExecuteScalarAsync(Token));
        }
        await AssertCounterAsync(connection, 3, 20);
    }

    [Fact]
    public async Task ProvidedStoreCapturesVersionWithoutACommandExpectationAndRejectsSupersededRoots()
    {
        string connection = await SeedAsync();
        await using (var write = Database(connection))
        {
            IEventStore<AggregateProposal> store = new AmountStore(write, new CounterClock());
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.GetForWritingAsync(DemoData.CounterId, cancellationToken: Token)
            );
            await using var transaction = await write.Database.BeginTransactionAsync(Token);
            var superseded = (
                await store.GetForWritingAsync(DemoData.CounterId, cancellationToken: Token)
            )!;
            Assert.Equal(2, superseded.ExpectedVersion);
            Assert.Empty(superseded.PendingEvents);
            var aggregate = (
                await store.GetForWritingAsync(DemoData.CounterId, cancellationToken: Token)
            )!;
            superseded.Version = 3;
            superseded.Facts = [new AmountFact(1)];
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.AppendAsync(superseded, Token)
            );
            Assert.Empty(write.ChangeTracker.Entries());
            aggregate.Version = 4;
            aggregate.Facts = [new AmountFact(2), new AmountFact(3)];
            Assert.Equal(
                new EventAppendResult(4, RecordedAt),
                await store.AppendAsync(aggregate, Token)
            );
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                store.AppendAsync(aggregate, Token)
            );
            await write.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }
        await AssertCounterAsync(connection, 4, 22);
    }

    [Fact]
    public async Task ProvidedStoreRejectsReconstitutionAtADifferentVersionWithoutTracking()
    {
        string connection = await SeedAsync();
        await using var write = Database(connection);
        await using var transaction = await write.Database.BeginTransactionAsync(Token);
        IEventStore<AggregateProposal> store = new AmountStore(
            write,
            new CounterClock(),
            wrongVersion: true
        );
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            store.GetForWritingAsync(DemoData.CounterId, cancellationToken: Token)
        );
        Assert.Empty(write.ChangeTracker.Entries());
    }

    private sealed class AmountStore(
        StorageDbContext database,
        TimeProvider timeProvider,
        bool wrongVersion = false,
        TestRecordAdapter? records = null
    )
        : EventStore<AggregateProposal, AmountFact, EventStreamRecord, StoredEventRecord>(
            database,
            records ?? new TestRecordAdapter(),
            timeProvider
        )
    {
        protected override EventStreamRecord CreateStream(Guid id) => new();

        protected override Task<AggregateProposal> LoadAggregateAsync(
            EventStreamRecord stream,
            CancellationToken cancellationToken
        ) => Task.FromResult(Proposal(stream.Id, stream.Version + (wrongVersion ? 1 : 0), []));
    }

    [Fact]
    public async Task MappedOwnershipPrefixRejectsForeignEnvelopesAndAllowsSameGuidAcrossOwners()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        var options = new DbContextOptionsBuilder<OwnedContext>().UseNpgsql(connection).Options;
        Guid id = Guid.NewGuid();
        await using (var setup = new OwnedContext(options, "alpha"))
            await setup.Database.EnsureCreatedAsync(Token);
        foreach (var owner in new[] { "alpha", "beta" })
        {
            await using var database = new OwnedContext(options, owner);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            var bad = new OwnedStore(database, "foreign");
            Assert.Null(await bad.GetForWritingAsync(id, cancellationToken: Token));
            await Assert.ThrowsAsync<ArgumentException>(() =>
                bad.AppendAsync(Proposal(id, 0, [1]), Token)
            );
            Assert.Empty(database.ChangeTracker.Entries());
            var store = new OwnedStore(database, owner);
            Assert.Null(await store.GetForWritingAsync(id, cancellationToken: Token));
            await store.AppendAsync(Proposal(id, 0, [owner == "alpha" ? 1 : 2]), Token);
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }
        await using var read = new OwnedContext(options, "alpha");
        Assert.Equal(2, await read.Set<OwnedHeader>().IgnoreQueryFilters().CountAsync(Token));
        var rows = await read.Set<OwnedFactRecord>()
            .OrderBy(row => row.Account)
            .ToArrayAsync(Token);
        Assert.Equal(["alpha", "beta"], rows.Select(row => row.Account));
        Assert.Equal([1, 2], rows.Select(row => row.Payload.GetProperty("amount").GetInt32()));
        Assert.Equal(2, rows.Select(row => row.EventId).Distinct().Count());
    }

    private sealed class OwnedStore(OwnedContext database, string envelopeOwner)
        : EventStore<AggregateProposal, AmountFact, OwnedHeader, OwnedFactRecord>(
            database,
            new OwnedRecordAdapter(envelopeOwner),
            new CounterClock()
        )
    {
        protected override OwnedHeader CreateStream(Guid id) => new() { Owner = database.Owner };

        protected override Task<AggregateProposal> LoadAggregateAsync(
            OwnedHeader stream,
            CancellationToken cancellationToken
        ) => Task.FromResult(Proposal(stream.Id, stream.Version, []));
    }

    private sealed record AmountFact(int Amount);

    // Deliberately mutable/custom implementation exercises the public contract's validation.
    private sealed class AggregateProposal : IEventSourcedAggregate<AmountFact>
    {
        public Guid Id { get; set; }
        public long ExpectedVersion { get; init; }
        public long Version { get; set; }

        public AmountFact[] Facts { get; set; } = [];
        public IReadOnlyList<AmountFact> PendingEvents => Facts;
    }

    private static AggregateProposal Proposal(Guid id, long expected, int[] amounts) =>
        new()
        {
            Id = id,
            ExpectedVersion = expected,
            Version = unchecked(expected + amounts.Length),
            Facts = amounts.Select(amount => new AmountFact(amount)).ToArray(),
        };

    private sealed class TestRecordAdapter(string? fault = null, JsonElement? payload = null)
        : EventRecordMapping<AmountFact, EventStreamRecord, StoredEventRecord>
    {
        private StoredEventRecord? reused;
        internal InvalidDataException Failure { get; } =
            new("The consumer encoder rejected the later fact.");
        public override string StreamType => "proof.counter";

        public override StoredEventRecord ToRow(AmountFact fact, EventStreamRecord stream)
        {
            if (fault == "later-encoder" && fact.Amount == 2)
                throw Failure;
            var row = new StoredEventRecord
            {
                EventName = "proof.counter-increased",
                SchemaVersion = 1,
                Payload =
                    payload ?? JsonSerializer.SerializeToElement(new { amount = fact.Amount }),
            };
            if (fault == "reused-row")
                return reused ??= row;
            if (fact.Amount == 2)
            {
                if (fault == "schema")
                    row.SchemaVersion = 0;
                if (fault == "payload")
                    row.Payload = default;
                if (fault == "null-payload")
                    row.Payload = JsonSerializer.SerializeToElement<object?>(null);
                if (fault == "name")
                    row.EventName = " ";
            }
            return row;
        }
    }

    private sealed class OwnedRecordAdapter(string owner)
        : EventRecordMapping<AmountFact, OwnedHeader, OwnedFactRecord>
    {
        public override string StreamType => "independent";

        public override OwnedFactRecord ToRow(AmountFact fact, OwnedHeader stream) =>
            new()
            {
                Account = owner,
                EventName = "independent",
                SchemaVersion = 1,
                Payload = JsonSerializer.SerializeToElement(new { amount = fact.Amount }),
            };
    }

    private static StorageDbContext Database(string connection) =>
        new(StorageDbContext.Options(connection));

    private async Task<string> SeedAsync()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using var database = Database(connection);
        await database.Database.MigrateAsync(Token);
        DemoData.Stage(database);
        await database.SaveChangesAsync(Token);
        return connection;
    }

    private static async Task AssertCounterAsync(
        string connection,
        long version,
        int value,
        Guid? id = null
    )
    {
        await using var read = Database(connection);
        Guid identity = id ?? DemoData.CounterId;
        Assert.Equal(
            new CounterState(identity, version, value, RecordedAt),
            await new CounterCommands(read, new CounterClock()).ReadAsync(identity, Token)
        );
        Assert.Equal(
            version,
            await read.Events.LongCountAsync(row => row.StreamId == identity, Token)
        );
    }

    private static async Task ExecuteAsync(string connection, string sql)
    {
        await using var database = new NpgsqlConnection(connection);
        await database.OpenAsync(Token);
        await using var command = new NpgsqlCommand(sql, database);
        await command.ExecuteNonQueryAsync(Token);
    }

    private static void AssertVersionConflict(DbUpdateException failure)
    {
        if (failure is DbUpdateConcurrencyException concurrency)
        {
            Assert.NotEmpty(concurrency.Entries);
            Assert.All(
                concurrency.Entries,
                entry => Assert.IsType<EventStreamRecord>(entry.Entity)
            );
            return;
        }
        var collision = Assert.IsType<PostgresException>(failure.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, collision.SqlState);
        Assert.Equal("journal", collision.SchemaName);
        Assert.Equal("IX_facts_stream_id_stream_version", collision.ConstraintName);
    }

    private sealed class BeforeHistoryRead(Func<Task> action) : DbCommandInterceptor
    {
        private bool _invoked;

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            if (
                !_invoked
                && command.CommandText.Contains("FROM journal.facts AS", StringComparison.Ordinal)
            )
            {
                _invoked = true;
                await action();
            }
            return result;
        }
    }

    private sealed class OwnedContext(DbContextOptions<OwnedContext> options, string owner)
        : DbContext(options)
    {
        public string Owner { get; } = owner;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureEventSourcingStorage<OwnedHeader, OwnedFactRecord>(
                row => new { row.Owner, row.Id },
                row => new { row.Account, row.EventId },
                row => new { row.Account, row.StreamId }
            );
            modelBuilder.Entity<OwnedHeader>().HasQueryFilter(row => row.Owner == Owner);
            modelBuilder
                .Entity<OwnedFactRecord>()
                .Property(row => row.Payload)
                .HasColumnType("jsonb");
        }
    }

    private sealed class OwnedHeader : IEventStreamRecord
    {
        public string Owner { get; set; } = null!;
        public Guid Id { get; set; }
        public string StreamType { get; set; } = null!;
        public long Version { get; set; }
        public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }

    private sealed class OwnedFactRecord : IStoredEventRecord
    {
        public string Account { get; set; } = null!;
        public Guid EventId { get; set; }
        public Guid StreamId { get; set; }
        public long StreamVersion { get; set; }
        public string EventName { get; set; } = null!;
        public int SchemaVersion { get; set; }
        public DateTimeOffset RecordedAt { get; set; }
        public JsonElement Payload { get; set; }
    }
}
