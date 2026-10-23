# Ordered history range validation

A package-free .NET 10 integrity utility for a consumer-selected range. It has no JSON,
persistence, identity, tenancy, messaging or required event-type dependency. Wholesale replay
paths compose it with the separate JSON codec. The
[EventSourcing EF adapter](../../Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/README.md#bounded-event-history)
references this package for its provided bounded history reader; the independent counter
uses that reader without selecting the JSON codec.

## Explicit consumer usage

The consumer selects and orders rows, materializes them once, validates their metadata,
then decodes and hydrates those same rows:

```csharp
EventHistory.ValidateRange(
    rows.Select(row => new HistoryPosition(row.StreamVersion, row.RecordedAt)),
    afterVersion,
    throughVersion);
var events = rows.Select(row => codec.Deserialize(
    row.EventName, row.SchemaVersion, row.Payload));
var state = MyDomain.Rehydrate(events);
```

The validator receives only positions/timestamps. It has no payload generic, event base
class, retained collection, selector, decoder or domain reducer. Native EF filtering,
ordering and materialization belong to the consumer or its explicitly selected EventSourcing
reader before this call. See the executable
[Inventory](../tests/CodecAdoptionTests/Fixtures/Inventory/StockPositionHistoryExample.cs)
and [Purchasing](../tests/CodecAdoptionTests/Fixtures/Purchasing/PurchaseOrderHistoryExample.cs)
recipes; their current queries operate on fixture rows, not PostgreSQL.

## Contract and failures

The supplied positions must cover `(afterVersion, throughVersion]` exactly, already ordered.
The exclusive lower bound is nonnegative and the upper bound cannot precede it. Equal bounds
require an empty range; `(0, 0]` represents no selected events, not an event at stored version 0.
A normal prefix uses lower bound 0, and a later range can start after a nonzero version.

A single `foreach` tracks the last validated version and previous recorded timestamp.
Gaps, duplicate/reordered positions, missing tails and excess rows fail. Recorded timestamps
must be nondecreasing within the returned range; equal times are allowed. The validator
compares DateTimeOffset instants without requiring zero offsets. The EventSourcing EF reader
separately requires UTC metadata and checks captured stream endpoints. This standalone utility
does not compare the first row against an excluded predecessor or inspect excluded rows.

`EventHistoryException` has a fixed payload-free message and these classifications:

| Failure | Version context |
| --- | --- |
| `UnexpectedVersion = 1` | Expected next position and observed row position. |
| `RangeMismatch = 2` | Expected upper bound and last validated/first excess position; an empty range reports the exclusive lower bound. |
| `RecordedTimeRegression = 3` | Expected version is null; observed version identifies the offending row. |

Null input and invalid bounds retain native argument errors. Consumers attach stream identity,
tenant/product context and response policy. Stream versions remain separate from codec schema
versions; recorded time is not a claim about wall-clock commit order or business occurrence time.

## Scope and limits

Validation enumerates the metadata once, using O(n) work and O(1) validation state, without
counting, copying or retaining rows. This is an implementation property; no allocation benchmark
is claimed. The caller owns stable materialized rows during validation and hydration. There
is no snapshot, immutable-history wrapper or automatic fallback.

Selection by version/time stays in consumer queries. For a time read, the sample first finds
the highest qualifying version within the supplied head, then reads the complete prefix through
that version. Filtering rows by time alone could omit intermediate positions.

Successful validation certifies only the supplied range. Selected reads do not certify
corruption elsewhere, tenant scoping, stream metadata or a committed captured head. The utility
does not append, save, transact, retry or publish. Wholesale's native PostgreSQL suites exercise
queries, captured-head races and persistence beyond this range validator.

## Event-store composition and remaining limits

Wholesale's native replay paths use this validator after fetching the selected range, then
decode through Events.Serialization and run their own reducers. The EF adapter's
EventHistoryReader applies this same range validator before consumer-owned decoding. The
[counter history reader](../../Gloaming.EventSourcing/samples/EventStorageDemo/CounterHistoryReader.cs)
inherits it and decodes direct JSON. Required inline aggregate rebuilding and complete
regeneration are also supplied by the EventSourcing adapter; they remain explicit maintenance.

Snapshot-plus-tail loading, secondary/multi-stream projections, catch-up and a safe global
event feed remain deferred. Range success cannot establish excluded history or committed
global progress. Any selected extension needs captured-head/tail, corruption and concurrent
writer/progress proofs. See the [event-store capability record](../../Gloaming.EventSourcing/docs/capabilities.md)
for those pending contracts. Current metadata validation remains independently adoptable.
