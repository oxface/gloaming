using System.Data.Common;
using System.Text.Json;
using Gloaming.EventSourcing.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using ModulithFoundry.Tests.Infrastructure;
using Npgsql;

namespace Gloaming.EventSourcing.Postgres.Tests;

// Deliberately tenant-free, direct JSON, no Wholesale/codec dependency.
internal sealed class LedgerStream : IEventStreamRecord
{
    public Guid Id { get; set; }
    public string StreamType { get; set; } = null!;
    public long Version { get; set; }
    public Guid ConcurrencyStamp { get; set; } = Guid.NewGuid();

    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

internal sealed class BalanceRow : IInlineStateRecord
{
    public Guid StreamId { get; set; }
    public long Version { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public JsonElement State { get; set; }
}

internal sealed class LedgerDatabase(DbContextOptions<LedgerDatabase> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.ConfigureEventSourcingStorage<LedgerStream, StoredEventRecord>(
            new() { Schema = "ledger" }
        );
        model.Entity<StoredEventRecord>().Property(row => row.Payload).HasColumnType("jsonb");
        var balance = model.Entity<BalanceRow>();
        balance.ToTable("balances", "ledger");
        balance.HasKey(row => row.StreamId);
        balance
            .HasOne<LedgerStream>()
            .WithMany()
            .HasForeignKey(row => row.StreamId)
            .OnDelete(DeleteBehavior.Restrict);
        balance.Property(row => row.Version).IsConcurrencyToken().ValueGeneratedNever();
        balance.Property(row => row.State).HasColumnType("jsonb");
        model.ConfigureRequiredInlineState<LedgerStream, StoredEventRecord, BalanceRow>("ledger");
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        this.ValidateEventStreamChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default
    )
    {
        this.ValidateEventStreamChanges();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}

internal sealed record Movement(int Amount);

internal sealed record Balance(int Amount);

internal sealed class Ledger(Guid id, long version, Balance? state)
    : EventSourcedAggregate<Balance, Movement>(id, version, state)
{
    internal static Ledger Create(Guid id)
    {
        var ledger = new Ledger(id, 0, null);
        ledger.Move(10);
        return ledger;
    }

    internal void Move(int amount) => ApplyChanges([new(amount)]);

    internal static Balance Fold(IEnumerable<Movement> events) =>
        new(events.Sum(item => item.Amount));

    protected override Balance Evolve(Balance? state, IReadOnlyList<Movement> events) =>
        new((state?.Amount ?? 0) + events.Sum(item => item.Amount));

    protected override void ValidateCandidate(Balance candidate)
    {
        if (candidate.Amount < 0)
            throw new InvalidOperationException("Insufficient balance.");
    }
}

internal sealed class LedgerStore : EventStore<Ledger, Movement, LedgerStream, StoredEventRecord>
{
    internal LedgerStore(LedgerDatabase database, bool failCandidate = false)
        : base(database, new Records(), new FrozenClock())
    {
        ConfigureInlineState(new LedgerStateMapping(failCandidate));
    }

    protected override LedgerStream CreateStream(Guid id) => new();

    private sealed class Records : EventRecordMapping<Movement, LedgerStream, StoredEventRecord>
    {
        public override string StreamType => "ledger";

        public override StoredEventRecord ToRow(Movement @event, LedgerStream observedStream) =>
            new()
            {
                EventName = "movement",
                SchemaVersion = 1,
                Payload = JsonSerializer.SerializeToElement(@event),
            };
    }

    private sealed class FrozenClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => new(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
    }
}

internal sealed class LedgerStateMapping(bool fail = false)
    : AggregateStateMapping<Ledger, BalanceRow>
{
    public override Ledger ToAggregate(BalanceRow state) =>
        new(state.StreamId, state.Version, state.State.Deserialize<Balance>()!);

    public override BalanceRow ToRow(Ledger aggregate) =>
        fail
            ? throw new InvalidOperationException("Candidate failure.")
            : new() { State = JsonSerializer.SerializeToElement(aggregate.State) };
}

internal sealed class LedgerHistoryReader(LedgerDatabase database)
    : EventHistoryReader<Movement, LedgerStream, StoredEventRecord>(database, "ledger")
{
    protected override Movement DecodeEvent(StoredEventRecord record) =>
        record.EventName == "movement" && record.SchemaVersion == 1
            ? record.Payload.Deserialize<Movement>()!
            : throw new InvalidDataException("Unknown schema.");
}

// Deliberately violates the read contract to retain independent rebuilder-boundary proofs.
internal sealed class FaultyLedgerHistoryReader(
    IEventHistoryReader<Movement, LedgerStream> reader,
    string? fault
) : IEventHistoryReader<Movement, LedgerStream>
{
    public async Task<IReadOnlyList<ReplayedEvent<Movement>>> ReadAsync(
        LedgerStream observedStream,
        long? throughVersion = null,
        CancellationToken cancellationToken = default
    )
    {
        var events = (
            await reader.ReadAsync(observedStream, throughVersion, cancellationToken)
        ).ToList();
        if (fault == "gap")
            events.RemoveAt(0);
        if (fault == "order")
            events.Reverse();
        if (fault == "duplicate")
            events[1] = events[0];
        if (fault == "time")
            events[0] = events[0] with { RecordedAt = observedStream.CreatedAt.AddMinutes(-1) };
        if (fault == "offset")
            events[0] = events[0] with
            {
                RecordedAt = events[0].RecordedAt.ToOffset(TimeSpan.FromHours(1)),
            };
        if (fault == "null")
            events[0] = events[0] with { Event = null! };
        return events;
    }
}

internal sealed class LedgerRebuilder(
    LedgerDatabase database,
    string? fault = null,
    bool failCandidate = false
)
    : AggregateRebuilder<Ledger, Movement, LedgerStream, BalanceRow>(
        database,
        "ledger",
        new LedgerStateMapping(failCandidate),
        new FaultyLedgerHistoryReader(new LedgerHistoryReader(database), fault)
    )
{
    protected override Ledger Rehydrate(LedgerStream header, IReadOnlyList<Movement> events)
    {
        var aggregate = new Ledger(
            fault == "identity" ? Guid.NewGuid() : header.Id,
            fault == "version" ? header.Version - 1 : header.Version,
            Ledger.Fold(events)
        );
        if (fault == "pending")
            aggregate.Move(1);
        return aggregate;
    }
}

internal static class RebuildConsumer
{
    internal static CancellationToken Token => TestContext.Current.CancellationToken;

    internal static LedgerDatabase Open(string connection, DbCommandInterceptor? commands = null)
    {
        var options = new DbContextOptionsBuilder<LedgerDatabase>().UseNpgsql(connection);
        if (commands is not null)
            options.AddInterceptors(commands);
        return new(options.Options);
    }

    internal static async Task<(string Connection, Guid Id)> SeedAsync(PostgreSqlFixture postgres)
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        Guid id = Guid.NewGuid();
        await using var database = Open(connection);
        await database.Database.EnsureCreatedAsync(Token);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        var store = new LedgerStore(database);
        Assert.Null(await store.GetForWritingAsync(id, cancellationToken: Token));
        var aggregate = Ledger.Create(id);
        aggregate.Move(5);
        await store.AppendAsync(aggregate, Token);
        await database.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
        return (connection, id);
    }

    internal static async Task ExecuteAsync(string connection, string sql)
    {
        await using var database = new NpgsqlConnection(connection);
        await database.OpenAsync(Token);
        await using var command = new NpgsqlCommand(sql, database);
        await command.ExecuteNonQueryAsync(Token);
    }

    internal static async Task AssertCommittedAsync(
        string connection,
        Guid id,
        int amount = 15,
        long version = 2
    )
    {
        await using var database = Open(connection);
        var header = await database.Set<LedgerStream>().SingleAsync(row => row.Id == id, Token);
        var balance = await database
            .Set<BalanceRow>()
            .SingleAsync(row => row.StreamId == id, Token);
        Assert.Equal(version, header.Version);
        Assert.Equal(header.Version, balance.Version);
        Assert.Equal(header.UpdatedAt, balance.RecordedAt);
        Assert.Equal(amount, balance.State.Deserialize<Balance>()!.Amount);
        Assert.Equal(
            version,
            await database.Set<StoredEventRecord>().LongCountAsync(row => row.StreamId == id, Token)
        );
    }

    internal static async Task<int> BackendAsync(LedgerDatabase database)
    {
        await using var command = database.Database.GetDbConnection().CreateCommand();
        command.CommandText = "SELECT pg_backend_pid()";
        return (int)(await command.ExecuteScalarAsync(Token))!;
    }
}

internal sealed class CommandTrace : DbCommandInterceptor
{
    internal List<string> Commands { get; } = [];

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command,
        CommandEventData eventData,
        InterceptionResult<DbDataReader> result,
        CancellationToken cancellationToken = default
    )
    {
        Commands.Add(command.CommandText);
        return ValueTask.FromResult(result);
    }
}
