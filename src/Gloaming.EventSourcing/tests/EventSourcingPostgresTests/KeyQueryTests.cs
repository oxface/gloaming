using System.Data.Common;
using System.Text.Json;
using Gloaming.Events.History;
using Gloaming.EventSourcing.EntityFrameworkCore;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Gloaming.EventSourcing.Postgres.Tests;

public sealed class KeyQueryTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset RecordedAt = new(2026, 10, 23, 0, 0, 0, TimeSpan.Zero);

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompleteKeysReuseOneParameterizedQueryAndPreserveFilters(bool history)
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        // Isolate the native query cache so other tests cannot warm this query first.
        await using var services = new ServiceCollection()
            .AddEntityFrameworkNpgsql()
            .BuildServiceProvider();
        int compilations = 0;
        var commands = new QueryTrace();
        var options = new DbContextOptionsBuilder<QueryDatabase>()
            .UseNpgsql(connection)
            .UseInternalServiceProvider(services)
            .AddInterceptors(commands)
            .LogTo(_ => compilations++, [CoreEventId.QueryCompilationStarting])
            .Options;
        Guid firstId = Guid.NewGuid();
        var streams = new[]
        {
            Stream("first-owner", firstId),
            Stream("second-owner", firstId),
            Stream("first-owner", Guid.NewGuid()),
        };
        var hidden = Stream("first-owner", Guid.NewGuid());
        await using (var seed = new QueryDatabase(options))
        {
            await seed.Database.EnsureCreatedAsync(Token);
            for (int index = 0; index < streams.Length; index++)
                Add(seed, streams[index], (index + 1) * 11);

            Add(seed, hidden, 99, visible: false);
            await seed.SaveChangesAsync(Token);
        }

        compilations = 0;
        commands.Items.Clear();
        await using var database = new QueryDatabase(options);
        var inline = new InlineStateReader<StreamRow, StateRow>(database);
        var reader = new HistoryReader(database);
        foreach (int index in new[] { 0, 1, 2, 0 })
        {
            var stream = streams[index];
            int actual = history
                ? Assert
                    .Single(await reader.ReadAsync(stream, cancellationToken: Token))
                    .Event.Amount
                : (await inline.ReadAsync(stream, Token)).Amount;
            Assert.Equal((index + 1) * 11, actual);
            var command = commands.Items[^1];
            Assert.Equal(history ? 3 : 2, command.Values.Length);
            Assert.Contains(stream.Owner, command.Values);
            Assert.Contains(stream.Id, command.Values);
        }

        Assert.Equal(4, commands.Items.Count);
        Assert.Single(commands.Items.Select(command => command.Sql).Distinct());
        Assert.Equal(1, compilations);
        if (history)
            await Assert.ThrowsAsync<EventHistoryException>(() =>
                reader.ReadAsync(hidden, cancellationToken: Token)
            );
        else
            await Assert.ThrowsAsync<InvalidDataException>(() => inline.ReadAsync(hidden, Token));

        Assert.Empty(database.ChangeTracker.Entries());
        Assert.Equal(0, await database.SaveChangesAsync(Token));
        Assert.All(
            commands.Items,
            command => Assert.StartsWith("SELECT", command.Sql, StringComparison.Ordinal)
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingReferenceKeyKeepsTheReadersExistingFailureContract(bool history)
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        var options = new DbContextOptionsBuilder<QueryDatabase>().UseNpgsql(connection).Options;
        await using var database = new QueryDatabase(options);
        await database.Database.EnsureCreatedAsync(Token);
        var incomplete = Stream(null!, Guid.NewGuid());
        if (history)
            await Assert.ThrowsAsync<ArgumentException>(() =>
                new HistoryReader(database).ReadAsync(incomplete, cancellationToken: Token)
            );
        else
            await Assert.ThrowsAsync<InvalidDataException>(() =>
                new InlineStateReader<StreamRow, StateRow>(database).ReadAsync(incomplete, Token)
            );

        Assert.Empty(database.ChangeTracker.Entries());
    }

    private static StreamRow Stream(string owner, Guid id) =>
        new()
        {
            Owner = owner,
            Id = id,
            StreamType = "query-proof",
            Version = 1,
            ConcurrencyStamp = Guid.NewGuid(),
            CreatedAt = RecordedAt,
            UpdatedAt = RecordedAt,
        };

    private static void Add(
        QueryDatabase database,
        StreamRow stream,
        int amount,
        bool visible = true
    )
    {
        database.Add(stream);
        database.Add(
            new StateRow
            {
                Partition = stream.Owner,
                StreamId = stream.Id,
                Version = 1,
                RecordedAt = RecordedAt,
                Amount = amount,
                Visible = visible,
            }
        );
        database.Add(
            new EventRow
            {
                Partition = stream.Owner,
                EventId = Guid.NewGuid(),
                StreamId = stream.Id,
                StreamVersion = 1,
                EventName = "amount",
                SchemaVersion = 1,
                RecordedAt = RecordedAt,
                Payload = JsonSerializer.SerializeToElement(new { amount }),
                Visible = visible,
            }
        );
    }

    private sealed class QueryDatabase(DbContextOptions<QueryDatabase> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model)
        {
            model.ConfigureEventSourcingStorage<StreamRow, EventRow>(
                stream => new { stream.Owner, stream.Id },
                row => new { row.Partition, row.EventId },
                row => new { row.Partition, row.StreamId }
            );
            model.Entity<EventRow>().Property(row => row.Payload).HasColumnType("jsonb");
            model.Entity<EventRow>().HasQueryFilter(row => row.Visible);
            var state = model.Entity<StateRow>();
            state.HasKey(row => new { row.Partition, row.StreamId });
            state
                .HasOne<StreamRow>()
                .WithMany()
                .HasForeignKey(row => new { row.Partition, row.StreamId });
            state.Property(row => row.Version).IsConcurrencyToken().ValueGeneratedNever();
            state.HasQueryFilter(row => row.Visible);
        }
    }

    private sealed class StreamRow : IEventStreamRecord
    {
        public string Owner { get; set; } = null!;
        public Guid Id { get; set; }
        public string StreamType { get; set; } = null!;
        public long Version { get; set; }
        public Guid ConcurrencyStamp { get; set; }
        public DateTimeOffset CreatedAt { get; set; }
        public DateTimeOffset UpdatedAt { get; set; }
    }

    private sealed class StateRow : IInlineStateRecord
    {
        public string Partition { get; set; } = null!;
        public Guid StreamId { get; set; }
        public long Version { get; set; }
        public DateTimeOffset RecordedAt { get; set; }
        public int Amount { get; set; }
        public bool Visible { get; set; }
    }

    private sealed class EventRow : IStoredEventRecord
    {
        public string Partition { get; set; } = null!;
        public Guid EventId { get; set; }
        public Guid StreamId { get; set; }
        public long StreamVersion { get; set; }
        public string EventName { get; set; } = null!;
        public int SchemaVersion { get; set; }
        public DateTimeOffset RecordedAt { get; set; }
        public JsonElement Payload { get; set; }
        public bool Visible { get; set; }
    }

    private sealed record AmountEvent(int Amount);

    private sealed class HistoryReader(QueryDatabase database)
        : EventHistoryReader<AmountEvent, StreamRow, EventRow>(database, "query-proof")
    {
        protected override AmountEvent DecodeEvent(EventRow row) =>
            new(row.Payload.GetProperty("amount").GetInt32());
    }

    private sealed class QueryTrace : DbCommandInterceptor
    {
        internal List<(string Sql, object[] Values)> Items { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            Items.Add(
                (
                    command.CommandText,
                    command.Parameters.Cast<DbParameter>().Select(p => p.Value!).ToArray()
                )
            );
            return ValueTask.FromResult(result);
        }
    }
}
