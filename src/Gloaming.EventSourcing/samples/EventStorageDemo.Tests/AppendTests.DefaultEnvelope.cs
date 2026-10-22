using System.Text.Json;
using Gloaming.EventSourcing;
using Gloaming.EventSourcing.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using DefaultEnvelope = Gloaming.EventSourcing.EntityFrameworkCore.StoredEventRecord;

namespace ModulithFoundry.Samples.EventStorageDemo.Tests;

public sealed partial class AppendTests
{
    [Fact]
    public async Task DefaultEnvelopeWorksThroughProvidedStoreAndExistingMigratedConsumerTables()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using (var migration = Database(connection))
            await migration.Database.MigrateAsync(Token);
        var options = new DbContextOptionsBuilder<DefaultEnvelopeContext>()
            .UseNpgsql(connection)
            .Options;
        Guid id = Guid.NewGuid();
        await using (var write = new DefaultEnvelopeContext(options))
        {
            var store = new DefaultEnvelopeStore(write);
            await using var transaction = await write.Database.BeginTransactionAsync(Token);
            Assert.Null(await store.GetForWritingAsync(id, cancellationToken: Token));
            var aggregate = new EnvelopeProposal(id);
            Assert.Equal(
                new EventAppendResult(2, RecordedAt),
                await store.AppendAsync(aggregate, Token)
            );
            await write.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }
        // The retained consumer mapping/migrations can read the same rows written by the library envelope.
        await using var read = Database(connection);
        var header = await read.Streams.AsNoTracking().SingleAsync(row => row.Id == id, Token);
        Assert.Equal(
            ("proof.default-envelope", 2L, RecordedAt),
            (header.StreamType, header.Version, header.UpdatedAt)
        );
        var rows = await read
            .Events.AsNoTracking()
            .Where(row => row.StreamId == id)
            .OrderBy(row => row.StreamVersion)
            .ToArrayAsync(Token);
        Assert.Equal([1L, 2L], rows.Select(row => row.StreamVersion));
        Assert.Equal(["proof.opened", "proof.renamed"], rows.Select(row => row.EventName));
        Assert.All(
            rows,
            row =>
            {
                Assert.NotEqual(Guid.Empty, row.EventId);
                Assert.Equal(1, row.SchemaVersion);
                Assert.Equal(RecordedAt, row.RecordedAt);
            }
        );
        Assert.Equal(new Opened(12.5m, "USD"), rows[0].Payload.Deserialize<Opened>(EnvelopeJson));
        Assert.Equal(new Renamed("Review"), rows[1].Payload.Deserialize<Renamed>(EnvelopeJson));
    }

    private static readonly JsonSerializerOptions EnvelopeJson = new(JsonSerializerDefaults.Web);

    private sealed record Opened(decimal Amount, string Currency);

    private sealed record Renamed(string Name);

    private sealed class EnvelopeProposal(Guid id) : IEventSourcedAggregate<object>
    {
        public Guid Id => id;
        public long ExpectedVersion => 0;
        public long Version => PendingEvents.Count;
        public IReadOnlyList<object> PendingEvents { get; } =
        [new Opened(12.5m, "USD"), new Renamed("Review")];
    }

    private sealed class DefaultEnvelopeStore(DefaultEnvelopeContext database)
        : EventStore<EnvelopeProposal, object, EventStreamRecord, DefaultEnvelope>(
            database,
            new DefaultEnvelopeAdapter(),
            new CounterClock()
        )
    {
        protected override EventStreamRecord CreateStream(Guid id) =>
            new() { Description = "Default envelope" };
    }

    private sealed class DefaultEnvelopeAdapter
        : EventRecordMapping<object, EventStreamRecord, DefaultEnvelope>
    {
        public override string StreamType => "proof.default-envelope";

        public override DefaultEnvelope ToRow(object fact, EventStreamRecord stream) =>
            new()
            {
                EventName = fact switch
                {
                    Opened => "proof.opened",
                    Renamed => "proof.renamed",
                    _ => throw new InvalidOperationException("Unsupported event type."),
                },
                SchemaVersion = 1,
                Payload = JsonSerializer.SerializeToElement(fact, fact.GetType(), EnvelopeJson),
            };
    }

    private sealed class DefaultEnvelopeContext(DbContextOptions<DefaultEnvelopeContext> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model)
        {
            model.ConfigureEventSourcingStorage<EventStreamRecord, DefaultEnvelope>(
                new EventSourcingStorageOptions
                {
                    Schema = "journal",
                    StreamsTable = "streams",
                    EventsTable = "facts",
                }
            );
            model
                .Entity<EventStreamRecord>()
                .Property(row => row.Description)
                .HasColumnName("description")
                .HasMaxLength(128);
            model.Entity<DefaultEnvelope>().Property(row => row.Payload).HasColumnType("jsonb");
        }
    }
}
