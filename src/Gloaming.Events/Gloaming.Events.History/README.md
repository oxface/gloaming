# Ordered history range validation

A package-free .NET 10 integrity utility for a consumer-selected range. It has no JSON,
persistence, identity, tenancy, messaging or required event-type dependency. Wholesale replay paths compose it with the separate JSON codec;
the provided EF event store and independent counter do not require this package.

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
ordering and materialization belong to the consumer before this call. See the executable
[Inventory](../../../samples/Wholesale/EventCodecDemo/Inventory/StockPositionHistoryExample.cs)
and [Purchasing](../../../samples/Wholesale/EventCodecDemo/Purchasing/PurchaseOrderHistoryExample.cs)
recipes; their current queries operate on fixture rows, not PostgreSQL.

## Contract and failures

The supplied positions must cover `(afterVersion, throughVersion]` exactly, already ordered.
The exclusive lower bound is nonnegative and the upper bound cannot precede it. Equal bounds
require an empty range; `(0, 0]` represents no selected events, not an event at stored version 0.
A normal prefix uses lower bound 0, and a later range can start after a nonzero version.

A single `foreach` tracks the last validated version and previous recorded timestamp.
Gaps, duplicate/reordered positions, missing tails and excess rows fail. Recorded timestamps
must be nondecreasing within the returned range; equal times are allowed. The validator
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

Successful validation certifies only the supplied range. Full-stream integrity checks are
explicit maintenance work; selected-range reads do not certify corruption elsewhere, correct
tenant scoping, stream creation/update metadata or a committed captured head. The utility does
not append, save, transact, retry or publish. Wholesale consumer suites exercise actual native EF queries, captured-head races and
persistence on PostgreSQL; these are composition proofs beyond this utility alone. See [the plan](../../../docs/plans/e5-1-event-history.md)
and [report](../../../docs/reports/e5-1-event-history.md).

## Deferred direction and event-store composition

Wholesale's native replay paths use this validator after fetching the selected range, then
decode through Events.Serialization and run their own reducers. The EF write store does not
depend on this package; its independent counter has a local history validator/reader.

A generic history reader, snapshot-plus-tail loading, projection rebuild/catch-up and a safe
global event feed remain deferred. Range success cannot establish excluded history or committed
global progress. Any selected extension needs captured-head/tail, corruption and concurrent
writer/progress proofs. See the [event-store capability record](../../Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/docs/capabilities.md)
for those pending contracts. Current metadata validation remains independently adoptable.
