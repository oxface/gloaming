using System.Text.Json;
using Gloaming.Events.History;
using Gloaming.EventSourcing.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ModulithFoundry.Tests.Infrastructure;
using static Gloaming.EventSourcing.Postgres.Tests.RebuildConsumer;

namespace Gloaming.EventSourcing.Postgres.Tests;

public sealed class EventHistoryReaderTests(PostgreSqlFixture postgres)
    : IClassFixture<PostgreSqlFixture>
{
    [Fact]
    public async Task FullAndEarlierPrefixesDecodeInOrderWithoutTrackingOrWriting()
    {
        var (connection, id) = await SeedAsync(postgres);
        var commands = new CommandTrace();
        await using var database = Open(connection, commands);
        var stream = await ObserveAsync(database, id);
        commands.Commands.Clear();
        var reader = new LedgerHistoryReader(database);

        var full = await reader.ReadAsync(stream, cancellationToken: Token);
        var earlier = await reader.ReadAsync(stream, 1, Token);

        Assert.Equal([10, 5], full.Select(item => item.Event.Amount));
        Assert.Equal([1L, 2L], full.Select(item => item.StreamVersion));
        Assert.Equal(10, Assert.Single(earlier).Event.Amount);
        Assert.All(full, item => Assert.Equal(stream.UpdatedAt, item.RecordedAt));
        Assert.Empty(database.ChangeTracker.Entries());
        Assert.Equal(2, commands.Commands.Count);
        Assert.All(
            commands.Commands,
            sql =>
            {
                Assert.StartsWith("SELECT", sql, StringComparison.Ordinal);
                Assert.Contains("ORDER BY", sql, StringComparison.Ordinal);
                Assert.DoesNotContain("event_streams", sql, StringComparison.Ordinal);
            }
        );
        await AssertCommittedAsync(connection, id);
    }

    [Fact]
    public async Task CapturedPrefixExcludesLaterCommittedAppendAndFreshObservationIncludesIt()
    {
        var (connection, id) = await SeedAsync(postgres);
        await using var database = Open(connection);
        var stream = await ObserveAsync(database, id);
        await AppendAsync(connection, id, 4);

        var reader = new LedgerHistoryReader(database);
        var captured = await reader.ReadAsync(stream, cancellationToken: Token);
        var fresh = await reader.ReadAsync(
            await ObserveAsync(database, id),
            cancellationToken: Token
        );

        Assert.Equal(15, captured.Sum(item => item.Event.Amount));
        Assert.Equal(19, fresh.Sum(item => item.Event.Amount));
        Assert.Equal([1L, 2L], captured.Select(item => item.StreamVersion));
        Assert.Equal([1L, 2L, 3L], fresh.Select(item => item.StreamVersion));
        Assert.Empty(database.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0)]
    [InlineData(3)]
    public async Task InvalidBoundsRejectBeforeQueryOrDecoding(long target)
    {
        var (connection, id) = await SeedAsync(postgres);
        var commands = new CommandTrace();
        await using var database = Open(connection, commands);
        var stream = await ObserveAsync(database, id);
        commands.Commands.Clear();
        var reader = new ObservedDecoder(database);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            reader.ReadAsync(stream, target, Token)
        );
        Assert.Empty(commands.Commands);
        Assert.Equal(0, reader.DecodedCount);
        Assert.Empty(database.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public async Task MissingFirstMiddleOrTailRejectsBeforeDecoding(long missingPosition)
    {
        var (connection, id) = await SeedAsync(postgres);
        await AppendAsync(connection, id, 4);
        await ExecuteAsync(
            connection,
            $"DELETE FROM ledger.events WHERE stream_version = {missingPosition}"
        );
        await using var database = Open(connection);
        var reader = new ObservedDecoder(database);
        var stream = await ObserveAsync(database, id);

        await Assert.ThrowsAsync<EventHistoryException>(() =>
            reader.ReadAsync(stream, cancellationToken: Token)
        );
        Assert.Equal(0, reader.DecodedCount);
        Assert.Empty(database.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData("creation")]
    [InlineData("end")]
    [InlineData("outside")]
    [InlineData("regression")]
    public async Task TimestampCorruptionRejectsBeforeDecoding(string damage)
    {
        var (connection, id) = await SeedAsync(postgres);
        await AppendAsync(connection, id, 4);
        string sql = damage switch
        {
            "creation" =>
                "UPDATE ledger.event_streams SET created_at = created_at - INTERVAL '1 second'",
            "end" =>
                "UPDATE ledger.event_streams SET updated_at = updated_at + INTERVAL '1 second'",
            "outside" =>
                "UPDATE ledger.events SET recorded_at = recorded_at + INTERVAL '1 second' WHERE stream_version >= 2",
            _ =>
                "UPDATE ledger.events SET recorded_at = recorded_at + INTERVAL '2 seconds' WHERE stream_version = 2; UPDATE ledger.events SET recorded_at = recorded_at + INTERVAL '1 second' WHERE stream_version = 3; UPDATE ledger.event_streams SET updated_at = updated_at + INTERVAL '1 second'",
        };
        await ExecuteAsync(connection, sql);
        await using var database = Open(connection);
        var reader = new ObservedDecoder(database);
        var stream = await ObserveAsync(database, id);

        if (damage == "regression")
            await Assert.ThrowsAsync<EventHistoryException>(() =>
                reader.ReadAsync(stream, cancellationToken: Token)
            );
        else
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                reader.ReadAsync(stream, cancellationToken: Token)
            );
        Assert.Equal(0, reader.DecodedCount);
        Assert.Empty(database.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData("alias")]
    [InlineData("schema")]
    [InlineData("payload")]
    [InlineData("null")]
    public async Task DecoderFailuresPropagateWithoutChangingStoredOrTrackedState(string damage)
    {
        var (connection, id) = await SeedAsync(postgres);
        if (damage != "null")
            await ExecuteAsync(
                connection,
                damage switch
                {
                    "alias" =>
                        "UPDATE ledger.events SET event_name = 'unknown' WHERE stream_version = 2",
                    "schema" =>
                        "UPDATE ledger.events SET schema_version = 2 WHERE stream_version = 2",
                    _ =>
                        "UPDATE ledger.events SET payload = '{\"Amount\":\"invalid\"}' WHERE stream_version = 2",
                }
            );
        await using var database = Open(connection);
        var reader = new ObservedDecoder(database, returnNull: damage == "null");
        var stream = await ObserveAsync(database, id);

        if (damage == "payload")
            await Assert.ThrowsAsync<JsonException>(() =>
                reader.ReadAsync(stream, cancellationToken: Token)
            );
        else
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                reader.ReadAsync(stream, cancellationToken: Token)
            );
        Assert.Equal(2, reader.DecodedCount);
        Assert.Empty(database.ChangeTracker.Entries());
        await AssertCommittedAsync(connection, id);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CancellationBeforeQueryOrBetweenDecodesDoesNotWrite(bool duringDecode)
    {
        var (connection, id) = await SeedAsync(postgres);
        var commands = new CommandTrace();
        await using var database = Open(connection, commands);
        var stream = await ObserveAsync(database, id);
        commands.Commands.Clear();
        using var cancellation = new CancellationTokenSource();
        var reader = new ObservedDecoder(
            database,
            cancellation: duringDecode ? cancellation : null
        );
        if (!duringDecode)
            cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            reader.ReadAsync(stream, cancellationToken: cancellation.Token)
        );
        Assert.Equal(duringDecode ? 1 : 0, reader.DecodedCount);
        Assert.Equal(duringDecode ? 1 : 0, commands.Commands.Count);
        Assert.Empty(database.ChangeTracker.Entries());
        await AssertCommittedAsync(connection, id);
    }

    [Fact]
    public async Task WrongStreamTypeRejectsBeforeQuery()
    {
        var (connection, id) = await SeedAsync(postgres);
        var commands = new CommandTrace();
        await using var database = Open(connection, commands);
        var stream = await ObserveAsync(database, id);
        stream.StreamType = "other";
        commands.Commands.Clear();

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new LedgerHistoryReader(database).ReadAsync(stream, cancellationToken: Token)
        );
        Assert.Empty(commands.Commands);
        Assert.Empty(database.ChangeTracker.Entries());
    }

    [Fact]
    public async Task CompleteCompositeKeysSelectOnlyTheObservedOwnerAndDecodeDistinctPayloadShapes()
    {
        var (connection, id) = await SeedOwnedAsync();
        foreach (
            var (owner, amounts) in new[]
            {
                ("alpha", new[] { 10, 5, 2 }),
                ("beta", new[] { 100, 30, 40 }),
            }
        )
        {
            var commands = new CommandTrace();
            await using var database = OwnedOpen(connection, owner, commands);
            var stream = await database
                .Set<OwnedStream>()
                .AsNoTracking()
                .SingleAsync(row => row.Id == id, Token);
            commands.Commands.Clear();
            var events = await new OwnedReader(database).ReadAsync(
                stream,
                cancellationToken: Token
            );

            Assert.Equal(amounts, events.Select(item => item.Event.Amount));
            Assert.IsType<Opening>(events[0].Event);
            Assert.All(events.Skip(1), item => Assert.IsType<Adjustment>(item.Event));
            Assert.Empty(database.ChangeTracker.Entries());
            var sql = Assert.Single(commands.Commands);
            Assert.Contains("\"Owner\" =", sql, StringComparison.Ordinal);
            Assert.Contains("stream_id =", sql, StringComparison.Ordinal);
            Assert.Contains("stream_version <=", sql, StringComparison.Ordinal);
            Assert.Contains("ORDER BY", sql, StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task EventGlobalFiltersRemainEffectiveAndCannotConcealAnIncompletePrefix()
    {
        var (connection, id) = await SeedOwnedAsync();
        await ExecuteAsync(
            connection,
            "UPDATE owned.events SET \"Visible\" = false WHERE \"Owner\" = 'alpha' AND stream_version = 2"
        );
        await using var database = OwnedOpen(connection, "alpha");
        var stream = await database
            .Set<OwnedStream>()
            .AsNoTracking()
            .SingleAsync(row => row.Id == id, Token);

        await Assert.ThrowsAsync<EventHistoryException>(() =>
            new OwnedReader(database).ReadAsync(stream, cancellationToken: Token)
        );
        Assert.Empty(database.ChangeTracker.Entries());
        await using var beta = OwnedOpen(connection, "beta");
        var otherStream = await beta.Set<OwnedStream>()
            .AsNoTracking()
            .SingleAsync(row => row.Id == id, Token);
        Assert.Equal(
            170,
            (await new OwnedReader(beta).ReadAsync(otherStream, cancellationToken: Token)).Sum(
                item => item.Event.Amount
            )
        );
    }

    private async Task<(string Connection, Guid Id)> SeedOwnedAsync()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        Guid id = Guid.NewGuid();
        await using var database = OwnedOpen(connection, "alpha");
        await database.Database.EnsureCreatedAsync(Token);
        DateTimeOffset time = new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        foreach (
            var (owner, amounts) in new[]
            {
                ("alpha", new[] { 10, 5, 2 }),
                ("beta", new[] { 100, 30, 40 }),
            }
        )
        {
            database.Add(
                new OwnedStream
                {
                    Owner = owner,
                    Id = id,
                    StreamType = "owned",
                    Version = 3,
                    CreatedAt = time,
                    UpdatedAt = time,
                }
            );
            for (int index = 0; index < amounts.Length; index++)
                database.Add(
                    new OwnedEvent
                    {
                        Owner = owner,
                        EventId = Guid.NewGuid(),
                        StreamId = id,
                        StreamVersion = index + 1,
                        EventName = index == 0 ? "opening" : "adjustment",
                        SchemaVersion = 1,
                        RecordedAt = time,
                        Payload =
                            index == 0
                                ? JsonSerializer.SerializeToElement(
                                    new { opening = amounts[index] }
                                )
                                : JsonSerializer.SerializeToElement(new { delta = amounts[index] }),
                    }
                );
        }
        await database.SaveChangesAsync(Token);
        return (connection, id);
    }

    private static OwnedDatabase OwnedOpen(
        string connection,
        string owner,
        CommandTrace? commands = null
    )
    {
        var options = new DbContextOptionsBuilder<OwnedDatabase>().UseNpgsql(connection);
        if (commands is not null)
            options.AddInterceptors(commands);
        return new(options.Options, owner);
    }

    private static Task<LedgerStream> ObserveAsync(LedgerDatabase database, Guid id) =>
        database.Set<LedgerStream>().AsNoTracking().SingleAsync(row => row.Id == id, Token);

    private static async Task AppendAsync(string connection, Guid id, int amount)
    {
        await using var database = Open(connection);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        var store = new LedgerStore(database);
        var aggregate = (await store.GetForWritingAsync(id, cancellationToken: Token))!;
        aggregate.Move(amount);
        await store.AppendAsync(aggregate, Token);
        await database.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
    }

    private sealed class ObservedDecoder(
        LedgerDatabase database,
        bool returnNull = false,
        CancellationTokenSource? cancellation = null
    ) : EventHistoryReader<Movement, LedgerStream, StoredEventRecord>(database, "ledger")
    {
        internal int DecodedCount { get; private set; }

        protected override Movement DecodeEvent(StoredEventRecord record)
        {
            DecodedCount++;
            cancellation?.Cancel();
            if (returnNull && record.StreamVersion == 2)
                return null!;
            if (record.EventName != "movement" || record.SchemaVersion != 1)
                throw new InvalidDataException("Unknown schema.");
            return record.Payload.Deserialize<Movement>()!;
        }
    }

    private interface IOwnedEvent
    {
        int Amount { get; }
    }

    private sealed record Opening(int Amount) : IOwnedEvent;

    private sealed record Adjustment(int Amount) : IOwnedEvent;

    private sealed class OwnedReader(OwnedDatabase database)
        : EventHistoryReader<IOwnedEvent, OwnedStream, OwnedEvent>(database, "owned")
    {
        protected override IOwnedEvent DecodeEvent(OwnedEvent record) =>
            record.EventName switch
            {
                "opening" => new Opening(record.Payload.GetProperty("opening").GetInt32()),
                "adjustment" => new Adjustment(record.Payload.GetProperty("delta").GetInt32()),
                _ => throw new InvalidDataException("Unknown owned event."),
            };
    }

    private sealed class OwnedDatabase(DbContextOptions<OwnedDatabase> options, string owner)
        : DbContext(options)
    {
        public string Owner { get; } = owner;

        protected override void OnModelCreating(ModelBuilder model)
        {
            model.ConfigureEventSourcingStorage<OwnedStream, OwnedEvent>(
                stream => new { stream.Owner, stream.Id },
                record => new { record.Owner, record.EventId },
                record => new { record.Owner, record.StreamId },
                new() { Schema = "owned" }
            );
            model.Entity<OwnedStream>().HasQueryFilter(stream => stream.Owner == Owner);
            // Deliberately no event-owner filter: full-key selection must independently isolate owners.
            model.Entity<OwnedEvent>().HasQueryFilter(record => record.Visible);
            model.Entity<OwnedEvent>().Property(record => record.Payload).HasColumnType("jsonb");
        }
    }

    private sealed class OwnedStream : IEventStreamRecord
    {
        public string Owner { get; set; } = null!;
        public Guid Id { get; set; }
        public string StreamType { get; set; } = null!;
        public long Version { get; set; }
        public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }

    private sealed class OwnedEvent : IStoredEventRecord
    {
        public string Owner { get; set; } = null!;
        public Guid EventId { get; set; }
        public Guid StreamId { get; set; }
        public long StreamVersion { get; set; }
        public string EventName { get; set; } = null!;
        public int SchemaVersion { get; set; }
        public DateTimeOffset RecordedAt { get; set; }
        public JsonElement Payload { get; set; }
        public bool Visible { get; set; } = true;
    }
}
