using System.Text.Json;
using Gloaming.EventSourcing;
using Gloaming.EventSourcing.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Npgsql;

using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
var token = deadline.Token;
var supplied =
    Environment.GetEnvironmentVariable("GLOAMING_PACKAGE_TEST_CONNECTION_STRING")
    ?? throw new InvalidOperationException("A disposable PostgreSQL server is required.");

string name = "gloaming_event_storage_" + Guid.NewGuid().ToString("N");
var settings = new NpgsqlConnectionStringBuilder(supplied) { Pooling = false };
await using var admin = new NpgsqlConnection(settings.ConnectionString);
await admin.OpenAsync(token);
await using (var create = new NpgsqlCommand($"CREATE DATABASE {name}", admin))
    await create.ExecuteNonQueryAsync(token);

try
{
    settings.Database = name;
    var options = new DbContextOptionsBuilder<CounterDatabase>()
        .UseNpgsql(settings.ConnectionString)
        .Options;
    CounterDatabase Open(string owner = "first-owner") => new(options, owner);
    Guid id = Guid.NewGuid();
    await using (var setup = Open())
        await setup.Database.EnsureCreatedAsync(token);

    // Manual native save integration; no DI, application runtime or event codec is selected.
    await using (var database = Open())
    {
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        var store = new CounterStore(database);
        Check(
            await store.GetForWritingAsync(id, 0, token) is null,
            "The counter must begin absent."
        );
        var counter = Counter.Start(id, 10);
        counter.Increase(5);
        await store.AppendAsync(counter, token);
        await AssertAbsent(id);
        database.SaveChanges();
        await AssertAbsent(id);
        await transaction.CommitAsync(token);
    }

    // The same GUID in another admitted owner must be a separate complete stream key.
    await using (var database = Open("second-owner"))
    {
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        var store = new CounterStore(database);
        Check(
            await store.GetForWritingAsync(id, 0, token) is null,
            "Another owner must have its own stream."
        );
        await store.AppendAsync(Counter.Start(id, 50), token);
        await database.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
    }

    await AssertPublicReads("first-owner", 15, [10, 5]);
    await AssertPublicReads("second-owner", 50, [50]);
    var original = await Observe();
    var otherOwner = await Observe("second-owner");

    // Native SQL may execute successfully and still be rolled back by the caller.
    await using (var database = Open())
    {
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        var store = new CounterStore(database);
        var counter =
            await store.GetForWritingAsync(id, 2, token)
            ?? throw new InvalidOperationException("Load the committed counter.");
        counter.Increase(7);
        await store.AppendAsync(counter, token);
        await database.SaveChangesAsync(token);
        Check((await Observe()).State.Amount == 15, "Uncommitted append must remain invisible.");
        await transaction.RollbackAsync(token);
    }

    Check(
        Unchanged(original, await Observe()),
        "Rollback must preserve facts, head and inline state."
    );

    // A required state row cannot be omitted from a newly proposed append.
    Guid rejectedId = Guid.NewGuid();
    await using (var database = Open())
    {
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        var store = new CounterStore(database);
        await store.GetForWritingAsync(rejectedId, 0, token);
        await store.AppendAsync(Counter.Start(rejectedId, 1), token);
        database.ChangeTracker.Entries<CounterRow>().Single().State = EntityState.Detached;
        await ExpectAsync<InvalidOperationException>(() => database.SaveChangesAsync(token));
    }

    await AssertAbsent(rejectedId);

    await using (var staleDatabase = Open())
    {
        await using var staleTransaction = await staleDatabase.Database.BeginTransactionAsync(
            token
        );
        var staleStore = new CounterStore(staleDatabase);
        var staleCounter =
            await staleStore.GetForWritingAsync(id, 2, token)
            ?? throw new InvalidOperationException("Capture the pre-repair writer.");
        staleCounter.Increase(1);

        // Consumer-owned fault injection deliberately bypasses ordinary save validation.
        await using (var damage = Open())
            await damage
                .Set<CounterRow>()
                .Where(row => row.StreamId == id)
                .ExecuteUpdateAsync(set => set.SetProperty(row => row.Amount, -999), token);

        await using (var ordinary = Open())
        {
            await using var transaction = await ordinary.Database.BeginTransactionAsync(token);
            await ExpectAsync<InvalidDataException>(() =>
                new CounterStore(ordinary).GetForWritingAsync(id, cancellationToken: token)
            );
        }

        var damaged = await Observe();
        Check(damaged.State.Amount == -999, "Ordinary loading must not repair damaged state.");

        await using (var database = Open())
        {
            await using var transaction = await database.Database.BeginTransactionAsync(token);
            var result = await new CounterRebuilder(database).RebuildAsync(id, token);
            Check(result?.Version == 2, "Explicit repair must capture the existing event version.");
            await database.SaveChangesAsync(token);
            await transaction.RollbackAsync(token);
        }

        Check(
            Unchanged(damaged, await Observe()),
            "Rolled-back repair must preserve the damaged database."
        );

        await using (var database = Open())
        {
            await using var transaction = await database.Database.BeginTransactionAsync(token);
            var result = await new CounterRebuilder(database).RebuildAsync(id, token);
            Check(result?.Version == 2, "Repair must keep the captured event version.");
            database.SaveChanges();
            await transaction.CommitAsync(token);
        }

        var repaired = await Observe();
        Check(
            repaired.State.Amount == 15 && repaired.State.Version == 2,
            "Replay must restore the exact inline state."
        );
        Check(
            repaired.Header.Version == original.Header.Version
                && repaired.Header.CreatedAt.EqualsExact(original.Header.CreatedAt)
                && repaired.Header.UpdatedAt.EqualsExact(original.Header.UpdatedAt)
                && repaired.State.RecordedAt.EqualsExact(original.State.RecordedAt),
            "Repair must preserve logical version and times."
        );
        Check(
            repaired.Header.ConcurrencyStamp != original.Header.ConcurrencyStamp,
            "Repair must invalidate earlier decisions."
        );
        Check(
            repaired.Facts.SequenceEqual(original.Facts),
            "Repair must preserve all retained fact fields."
        );
        await staleStore.AppendAsync(staleCounter, token);
        await ExpectAsync<DbUpdateConcurrencyException>(() =>
            staleDatabase.SaveChangesAsync(token)
        );
    }

    await AssertPublicReads("first-owner", 15, [10, 5]);
    Check(
        Unchanged(otherOwner, await Observe("second-owner")),
        "Repair must preserve the other owner's same-ID stream."
    );

    // A fresh decision after repair must still append through the ordinary store.
    await using (var database = Open())
    {
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        var store = new CounterStore(database);
        var counter =
            await store.GetForWritingAsync(id, 2, token)
            ?? throw new InvalidOperationException("Reload the repaired counter.");
        counter.Increase(2);
        await store.AppendAsync(counter, token);
        await database.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
    }

    await AssertPublicReads("first-owner", 17, [10, 5, 2]);
    await AssertPublicReads("second-owner", 50, [50]);
    Console.WriteLine(
        "PASS event storage: package-only composite keys, native saves/rollback, bounded history, explicit repair and stale-writer conflict"
    );

    async Task AssertAbsent(Guid selectedId)
    {
        await using var observer = Open();
        Check(
            !await observer.Set<StreamRow>().AnyAsync(row => row.Id == selectedId, token)
                && !await observer
                    .Set<EventRow>()
                    .AnyAsync(row => row.StreamId == selectedId, token)
                && !await observer
                    .Set<CounterRow>()
                    .AnyAsync(row => row.StreamId == selectedId, token),
            "No half of an absent stream may be visible."
        );
    }

    async Task AssertPublicReads(string owner, int expectedAmount, int[] expectedEvents)
    {
        await using var database = Open(owner);
        var stream = await database
            .Set<StreamRow>()
            .AsNoTracking()
            .SingleAsync(row => row.Id == id, token);
        var state = await new InlineStateReader<StreamRow, CounterRow>(database).ReadAsync(
            stream,
            token
        );
        var history = new CounterHistoryReader(database);
        var full = await history.ReadAsync(stream, cancellationToken: token);
        var earlier = await history.ReadAsync(stream, 1, token);
        Check(
            state.Amount == expectedAmount && state.Version == expectedEvents.Length,
            "Read the correct owner's inline state."
        );
        Check(
            full.Select(item => item.Event.Amount).SequenceEqual(expectedEvents),
            "Read the owner's exact retained prefix in order."
        );
        Check(
            full.Select(item => item.StreamVersion)
                .SequenceEqual(
                    Enumerable.Range(1, expectedEvents.Length).Select(version => (long)version)
                ),
            "History positions must remain complete."
        );
        Check(
            earlier.Count == 1 && earlier[0].Event.Amount == expectedEvents[0],
            "An earlier prefix must remain bounded."
        );
        Check(
            !database.ChangeTracker.Entries().Any() && await database.SaveChangesAsync(token) == 0,
            "Public reads must neither track nor write."
        );
    }

    async Task<Snapshot> Observe(string owner = "first-owner")
    {
        await using var database = Open(owner);
        var header = await database
            .Set<StreamRow>()
            .AsNoTracking()
            .SingleAsync(row => row.Id == id, token);
        var state = await database
            .Set<CounterRow>()
            .AsNoTracking()
            .SingleAsync(row => row.StreamId == id, token);
        var rows = await database
            .Set<EventRow>()
            .AsNoTracking()
            .Where(row => row.StreamId == id)
            .OrderBy(row => row.StreamVersion)
            .ToArrayAsync(token);
        return new(
            header,
            state,
            rows.Select(row => new Fact(
                    row.Owner,
                    row.EventId,
                    row.StreamId,
                    row.StreamVersion,
                    row.EventName,
                    row.SchemaVersion,
                    row.RecordedAt,
                    row.Payload.GetRawText()
                ))
                .ToArray()
        );
    }
}
finally
{
    await using var drop = new NpgsqlCommand($"DROP DATABASE {name} WITH (FORCE)", admin);
    await drop.ExecuteNonQueryAsync(CancellationToken.None);
}

static void Check(bool condition, string message)
{
    if (!condition)
        throw new InvalidOperationException(message);
}

static async Task ExpectAsync<TException>(Func<Task> action)
    where TException : Exception
{
    try
    {
        await action();
    }
    catch (TException)
    {
        return;
    }

    throw new InvalidOperationException($"Expected {typeof(TException).Name}.");
}

static bool Unchanged(Snapshot before, Snapshot after) =>
    before.Header.Version == after.Header.Version
    && before.Header.ConcurrencyStamp == after.Header.ConcurrencyStamp
    && before.Header.CreatedAt.EqualsExact(after.Header.CreatedAt)
    && before.Header.UpdatedAt.EqualsExact(after.Header.UpdatedAt)
    && before.State.Amount == after.State.Amount
    && before.State.Version == after.State.Version
    && before.State.RecordedAt.EqualsExact(after.State.RecordedAt)
    && before.Facts.SequenceEqual(after.Facts);

sealed record Fact(
    string Owner,
    Guid Id,
    Guid StreamId,
    long Version,
    string Alias,
    int Schema,
    DateTimeOffset At,
    string Payload
);

sealed record Snapshot(StreamRow Header, CounterRow State, Fact[] Facts);

sealed record Delta(int Amount);

sealed record CounterState(int Amount);

sealed class Counter(Guid id, long version, CounterState? state)
    : EventSourcedAggregate<CounterState, Delta>(id, version, state)
{
    internal static Counter Start(Guid id, int amount)
    {
        var counter = new Counter(id, 0, null);
        counter.Increase(amount);
        return counter;
    }

    internal void Increase(int amount) => ApplyChanges([new(amount)]);

    protected override CounterState Evolve(CounterState? state, IReadOnlyList<Delta> events) =>
        new((state?.Amount ?? 0) + events.Sum(item => item.Amount));

    protected override void ValidateCandidate(CounterState candidate)
    {
        if (candidate.Amount < 0)
            throw new InvalidOperationException("The counter cannot be negative.");
    }
}

sealed class CounterDatabase(DbContextOptions<CounterDatabase> options, string owner)
    : DbContext(options)
{
    internal string Owner { get; } = owner;

    protected override void OnModelCreating(ModelBuilder model)
    {
        model.ConfigureEventSourcingStorage<StreamRow, EventRow>(
            stream => new { stream.Owner, stream.Id },
            row => new { row.Owner, row.EventId },
            row => new { row.Owner, row.StreamId },
            new()
            {
                Schema = "package_counter",
                StreamsTable = "heads",
                EventsTable = "facts",
            }
        );
        model.Entity<StreamRow>().HasQueryFilter(row => row.Owner == Owner);
        model.Entity<EventRow>().HasQueryFilter(row => row.Owner == Owner);
        model.Entity<EventRow>().Property(row => row.Payload).HasColumnType("jsonb");
        var state = model.Entity<CounterRow>();
        state.ToTable("totals", "package_counter");
        state.HasKey(row => new { row.Owner, row.StreamId });
        state.HasOne<StreamRow>().WithMany().HasForeignKey(row => new { row.Owner, row.StreamId });
        state.HasQueryFilter(row => row.Owner == Owner);
        state.Property(row => row.Version).IsConcurrencyToken().ValueGeneratedNever();
        model.ConfigureRequiredInlineState<StreamRow, EventRow, CounterRow>("counter");
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

sealed class StreamRow : IEventStreamRecord
{
    public string Owner { get; set; } = null!;
    public Guid Id { get; set; }
    public string StreamType { get; set; } = null!;
    public long Version { get; set; }
    public Guid ConcurrencyStamp { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
}

sealed class EventRow : IStoredEventRecord
{
    public string Owner { get; set; } = null!;
    public Guid EventId { get; set; }
    public Guid StreamId { get; set; }
    public long StreamVersion { get; set; }
    public string EventName { get; set; } = null!;
    public int SchemaVersion { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public JsonElement Payload { get; set; }
}

sealed class CounterRow : IInlineStateRecord
{
    public string Owner { get; set; } = null!;
    public Guid StreamId { get; set; }
    public long Version { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public int Amount { get; set; }
}

sealed class CounterStore : EventStore<Counter, Delta, StreamRow, EventRow>
{
    private readonly CounterDatabase database;

    internal CounterStore(CounterDatabase database)
        : base(database, new Records(), TimeProvider.System)
    {
        this.database = database;
        ConfigureInlineState(new StateMapping());
    }

    protected override StreamRow CreateStream(Guid id) => new() { Owner = database.Owner };

    private sealed class Records : EventRecordMapping<Delta, StreamRow, EventRow>
    {
        public override string StreamType => "counter";

        public override EventRow ToRow(Delta value, StreamRow stream) =>
            new()
            {
                Owner = stream.Owner,
                EventName = "counter.increased",
                SchemaVersion = 1,
                Payload = JsonSerializer.SerializeToElement(value),
            };
    }
}

sealed class StateMapping : AggregateStateMapping<Counter, CounterRow>
{
    public override Counter ToAggregate(CounterRow row)
    {
        if (row.Amount < 0)
            throw new InvalidDataException("The stored counter body is invalid.");

        return new(row.StreamId, row.Version, new(row.Amount));
    }

    public override CounterRow ToRow(Counter counter) => new() { Amount = counter.State!.Amount };
}

sealed class CounterHistoryReader(CounterDatabase database)
    : EventHistoryReader<Delta, StreamRow, EventRow>(database, "counter")
{
    protected override Delta DecodeEvent(EventRow row) =>
        row.EventName == "counter.increased" && row.SchemaVersion == 1
            ? row.Payload.Deserialize<Delta>()
                ?? throw new InvalidDataException("Missing counter fact.")
            : throw new InvalidDataException("Unknown counter fact.");
}

sealed class CounterRebuilder(CounterDatabase database)
    : AggregateRebuilder<Counter, Delta, StreamRow, CounterRow>(
        database,
        "counter",
        new StateMapping(),
        new CounterHistoryReader(database)
    )
{
    protected override Counter Rehydrate(StreamRow stream, IReadOnlyList<Delta> events) =>
        new(stream.Id, stream.Version, new(events.Sum(item => item.Amount)));
}
