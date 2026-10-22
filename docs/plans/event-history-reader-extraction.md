# Bounded EF event-history reader extraction

Status: interface and scope owner-approved ("Approve interface and scope"); active implementation
and executable consumer adoption are in place. Verification is recorded in the
[reader slice report](../reports/event-history-reader-extraction.md). The proposal below preserves
the reviewed surface and file/behavior scope.
Existing append/rebuild semantics and unrelated worktree/index entries remain intact.

## Evidence and boundary

StockPositionHistoryReader and PurchaseOrderHistoryReader duplicate ReadRowsAsync and
ReadReplayAsync: native ordered prefix loading, range/endpoints validation and typed-event
materialization. CounterStore.LoadStateAsync repeats prefix loading and integrity checks with
tenant-free keys and direct JSON decoding. These are technical obligations, not business rules.

Extract their common implementation into the existing EventSourcing.EntityFrameworkCore package.
The reader receives an observed stream record, never reloads the stream, and reads positions
1 through its captured version (or an explicitly requested earlier positive version). It uses
the complete mapped stream/event PK/FK relationship, including ownership components, while
preserving native global filters. It returns ordered decoded events with version/time metadata.
It does not evolve an aggregate, track rows, repair data, acquire locks, save or commit.

Consumers retain tenant admission, initial stream lookup, recorded-time cutoff selection,
payload aliases/schema decoding, reducers, business Contracts and error translation. The reader
requires no transaction for ordinary reads. A rebuilder still requires its consumer's explicit
native transaction and same module context, and independently validates its reconstruction.
There is no global ordering, paging, snapshot/tail support, upcasting or projector engine.

## Proposed public surface

```csharp
public interface IEventHistoryReader<TEvent, TStreamRecord>
    where TEvent : class
    where TStreamRecord : class, IEventStreamRecord
{
    Task<IReadOnlyList<ReplayedEvent<TEvent>>> ReadAsync(
        TStreamRecord observedStream,
        long? throughVersion = null,
        CancellationToken cancellationToken = default);
}

public abstract class EventHistoryReader<TEvent, TStreamRecord, TStoredEventRecord>
    : IEventHistoryReader<TEvent, TStreamRecord>
    where TEvent : class
    where TStreamRecord : class, IEventStreamRecord
    where TStoredEventRecord : class, IStoredEventRecord
{
    protected EventHistoryReader(DbContext database, string streamType);
    protected abstract TEvent DecodeEvent(TStoredEventRecord record);
    public Task<IReadOnlyList<ReplayedEvent<TEvent>>> ReadAsync(
        TStreamRecord observedStream,
        long? throughVersion = null,
        CancellationToken cancellationToken = default);
}
```

One decoding override avoids a new decoder adapter and keeps serialization dependencies optional.
TEvent can be the aggregate's event interface: each payload is decoded to its concrete shape.
TStoredEventRecord is the common persistence envelope, not a restriction to one concrete event.

Change the protected AggregateRebuilder constructor to accept
IEventHistoryReader<TEvent,TStreamRecord> historyReader as its fourth argument. Remove its abstract
ReadEventsAsync hook; call the injected reader for the full observed prefix. Keep Rehydrate and
the existing state mapping unchanged. Writing receives no new history dependency. Existing native
DI can alias the consumer's typed reader; no new registration helper is needed for this slice.
Each concrete reader constructor selects its module DbContext, preserving module dependency
isolation without registering a bare DbContext. Maintenance composition uses that same scoped
context for the reader and rebuilder; the read interface does not expose or own transactions.

## Proposed implementation for line-by-line review

This listing is a draft, not active production code or runtime proof evidence. It compiled in an
isolated temporary project against the current EF library and Events.History with zero warnings
or errors. Native relationship discovery and complete-key predicates intentionally follow the
existing inline reader approach. The active implementation additionally parameterizes complete-key
values with EF.Parameter, preserving native query parameters and SQL reuse across stream identities.
Current consumer integration and PostgreSQL results live in the slice report; the listing below
preserves the reviewed draft rather than replacing that report's evidence.

```csharp
using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using ModulithFoundry.Events.History;

namespace ModulithFoundry.EventSourcing.EntityFrameworkCore;

public abstract class EventHistoryReader<TEvent, TStreamRecord, TStoredEventRecord>
    : IEventHistoryReader<TEvent, TStreamRecord>
    where TEvent : class
    where TStreamRecord : class, IEventStreamRecord
    where TStoredEventRecord : class, IStoredEventRecord
{
    private readonly DbContext database;
    private readonly string streamType;
    private readonly IForeignKey streamForeignKey;

    protected EventHistoryReader(DbContext database, string streamType)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(streamType);

        var streamModel = database.Model.FindEntityType(typeof(TStreamRecord))
            ?? throw new InvalidOperationException("Map the stream record before reading history.");
        var eventModel = database.Model.FindEntityType(typeof(TStoredEventRecord))
            ?? throw new InvalidOperationException("Map the event record before reading history.");
        var streamKey = streamModel.FindPrimaryKey()
            ?? throw new InvalidOperationException("Map the complete stream primary key.");
        streamForeignKey = eventModel.GetForeignKeys()
            .Single(foreignKey => foreignKey.PrincipalKey == streamKey);

        foreach (var property in streamKey.Properties.Concat(streamForeignKey.Properties))
        {
            if (property.PropertyInfo is null || property.GetValueConverter() is not null
                || property.GetProviderClrType() is not null)
                throw new InvalidOperationException("Use mapped unconverted stream/event keys.");
        }

        this.database = database;
        this.streamType = streamType;
    }

    protected abstract TEvent DecodeEvent(TStoredEventRecord record);

    public async Task<IReadOnlyList<ReplayedEvent<TEvent>>> ReadAsync(
        TStreamRecord observedStream,
        long? throughVersion = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(observedStream);
        cancellationToken.ThrowIfCancellationRequested();
        EventStreamValidation.ValidateExistingStream(observedStream, streamType);

        // Capture the bound and endpoints before querying. Do not discover a newer stream version.
        long observedVersion = observedStream.Version;
        long targetVersion = throughVersion ?? observedVersion;
        DateTimeOffset createdAt = observedStream.CreatedAt;
        DateTimeOffset updatedAt = observedStream.UpdatedAt;
        if (targetVersion < 1 || targetVersion > observedVersion)
            throw new ArgumentOutOfRangeException(nameof(throughVersion));

        var rows = await database.Set<TStoredEventRecord>()
            .AsNoTracking()
            .Where(MatchesStream(observedStream))
            .Where(record => record.StreamVersion <= targetVersion)
            .OrderBy(record => record.StreamVersion)
            .ToArrayAsync(cancellationToken);

        // Validate metadata before decoding any payload. Never conceal a missing tail.
        EventHistory.ValidateRange(
            rows.Select(record => new HistoryPosition(record.StreamVersion, record.RecordedAt)),
            0,
            targetVersion);
        if (rows.Any(record => record.RecordedAt.Offset != TimeSpan.Zero
                || record.RecordedAt < createdAt || record.RecordedAt > updatedAt)
            || !rows[0].RecordedAt.EqualsExact(createdAt)
            || (targetVersion == observedVersion && !rows[^1].RecordedAt.EqualsExact(updatedAt)))
            throw new InvalidDataException("History timestamps differ from the observed stream.");

        var events = new ReplayedEvent<TEvent>[rows.Length];
        for (int index = 0; index < rows.Length; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var record = rows[index];
            var decodedEvent = DecodeEvent(record)
                ?? throw new InvalidDataException("An event decoder returned null.");
            events[index] = new(record.StreamVersion, record.RecordedAt, decodedEvent);
        }

        return events;
    }

    private Expression<Func<TStoredEventRecord, bool>> MatchesStream(TStreamRecord observedStream)
    {
        var parameter = Expression.Parameter(typeof(TStoredEventRecord), "record");
        Expression predicate = Expression.Constant(true);
        for (int index = 0; index < streamForeignKey.Properties.Count; index++)
        {
            var property = streamForeignKey.Properties[index];
            object? keyValue = streamForeignKey.PrincipalKey.Properties[index]
                .PropertyInfo!.GetValue(observedStream);
            if (keyValue is null)
                throw new ArgumentException("Supply the complete observed stream key.");

            predicate = Expression.AndAlso(predicate, Expression.Equal(
                Expression.Property(parameter, property.PropertyInfo!),
                Expression.Constant(keyValue, property.ClrType)));
        }

        return Expression.Lambda<Func<TStoredEventRecord, bool>>(predicate, parameter);
    }
}
```

## Consumer adoption

The existing module reader retains its business current/version/time entry points but derives
from the provided reader. Its only persistence-decoding hook is:

```csharp
internal sealed class StockPositionHistoryReader(InventoryDbContext database)
    : EventHistoryReader<IStockPositionEvent, EventStream, StoredEvent>(database, StreamType),
      IStockPositionHistory
{
    internal const string StreamType = "inventory.stock-position";
    private static readonly JsonEventCodec<IStockPositionEvent> Codec =
        StockPositionCodec.CreateCodec();

    protected override IStockPositionEvent DecodeEvent(StoredEvent record) =>
        Codec.Deserialize(record.EventName, record.SchemaVersion, record.Payload);

    // Business read methods capture the stream and select a temporal target as today.
    // They use base.ReadAsync(stream, target, token), then consumer-owned evolution.
}
```

Rebuilding then supplies this existing reader directly to base:

```csharp
internal sealed class StockPositionRebuilder(
    InventoryDbContext database,
    StockPositionHistoryReader historyReader)
    : AggregateRebuilder<StockPositionAggregate, IStockPositionEvent, EventStream,
        StockPositionStateRow>(database, StockPositionHistoryReader.StreamType,
            new StockPositionStateMapping(), historyReader)
{
    protected override StockPositionAggregate Rehydrate(
        EventStream observedStream, IReadOnlyList<IStockPositionEvent> events) =>
        StockPositionAggregate.FromState(observedStream.Id, observedStream.Version,
            StockPositionEvolution.Evolve(null, events));
}
```

The tenant-free counter uses the same implementation with ordinary keys and a decoder using
JsonElement access directly. It keeps creation-event placement and event sequencing rules in
that consumer, and historical evolution continues to omit today's command ceiling rule.

## Dependencies, ownership and errors

Add one project dependency from EventSourcing.EntityFrameworkCore to the existing package-free
Events.History, reusing EventHistory.ValidateRange rather than duplicating its algorithm and
exceptions. Events.History remains independently adoptable. No dependency on Events.Serialization,
Npgsql, tenancy, Contracts, messaging or Marten is added. EventSourcing core and T1 stay event-free
in dependencies/composition as applicable: the core keeps its existing package-free shape and
T1 continues to select neither event family.

Invalid bounds/arguments use native argument exceptions; unsupported relationship configuration
uses InvalidOperationException; range gaps/order/time regression preserve EventHistoryException;
invalid stream/endpoints/UTC metadata use InvalidDataException. Consumer decoder failures,
cancellation and EF/provider failures propagate. CounterStore preserves its established
InvalidDataException-facing classification by translating EventHistoryException locally.
The reader does not retry or certify excluded events, uncooperative writers or performance at
unbounded history lengths. SQL selection happens before materialization; decode happens afterward.

## Exact file and behavior scope

Paths below are relative to the repository root; EF denotes
src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore.

| File | Proposed change |
| --- | --- |
| EF/IEventHistoryReader.cs; EF/EventHistoryReader.cs | Add the reviewed interface and reusable bounded-prefix implementation above. |
| EF/ModulithFoundry.EventSourcing.EntityFrameworkCore.csproj | Reference existing Events.History only. |
| EF/AggregateRebuilder.cs | Inject typed reader; remove ReadEventsAsync override requirement; preserve native reconstruction/save/concurrency behavior. |
| samples/Wholesale/modules/Inventory/Inventory/StockPositions/StockPositionHistoryReader.cs; Purchasing/Purchasing/PurchaseOrders/PurchaseOrderHistoryReader.cs | Derive from shared reader; remove ReadRowsAsync/ReadReplayAsync; preserve temporal target selection, module Contracts and pure evolution. |
| Corresponding StockPositionRebuilder.cs; PurchaseOrderRebuilder.cs | Pass existing reader to base; remove the one-line history hook. |
| src/Gloaming.EventSourcing/samples/EventStorageDemo/CounterHistoryReader.cs | Add a thin typed-context decoder using direct JSON, without tenancy/codec registration. |
| src/Gloaming.EventSourcing/samples/EventStorageDemo/CounterStore.cs | Delegate captured history loading to provided implementation; retain domain evolution and local error classification. |
| src/Gloaming.EventSourcing/tests/EventSourcingPostgresTests/RebuildConsumer.cs | Adopt shared reader in tenant-free ledger; preserve independent maintenance composition. |
| Same tests/EventHistoryReaderTests.cs; RegistrationTests.cs; RebuildTests.cs | Add actual reader proof coverage and update consumer constructor composition. |
| samples/Wholesale/IntegrationTests/EventPersistence/HistoryReadTests.cs; src/Gloaming.EventSourcing/samples/EventStorageDemo.Tests/AppendTests.cs | Extend adoption checks only where existing cases do not cover the extracted behavior. |
| tests/ArchitectureTests/AdoptionDependencyTests.cs; AssemblyDependencyTests.cs | Update both project and assembly dependency assertions for the approved Events.History dependency and consumer error translation; retain other independent consumer/T1 exclusions. |
| EF/README.md; EF/docs/capabilities.md; EventSourcing/docs/capabilities.md; docs/plans/library-extraction.md; new docs/reports/event-history-reader-extraction.md | Document supported setup/limits and separate fresh proof results from prior evidence. |

No model/SQL schema changes, migrations, fixture changes, archive edits, package relocation,
worker, projection engine or template presets. Existing edits and index entries stay intact;
this capability remains unstaged and requires separate exact commit approval.

## Verification contract

Prove full/earlier prefixes, capture followed by concurrent append, complete tenant-composite
key selection (including same stream GUID across tenants), native filter preservation, ordered
heterogeneous decoding, missing first/middle/tail rows, timestamp corruption, unknown event/schema,
decoder failure and cancellation without tracking/writing. Use independent semantic outcomes.
The implementation ran real PostgreSQL consumer read/rebuild/append suites, family tests,
dependency checks, active build/style/analyzers, archive integrity and the event-free T1 checks.
[The report](../reports/event-history-reader-extraction.md) records 341 fresh relevant test passes
plus 10 generated-consumer tests and identifies the new reusable loading mechanism. Older ES2
test counts remain historical evidence rather than additional executions of this reader.
