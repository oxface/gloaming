# E5.1 ordered range validation and explicit hydration

Status: the owner reviewed and approved the selected-range revision and its complete
38-file change set, checkpointed as `4cc12a1` on 2026-10-06 after E4 checkpoint `2a49ef3b`.
[The report](../reports/e5-1-event-history.md) records fresh verification and limits.

## Approved revision and extraction case

Keep the repeated integrity checks seen in the archived
[Inventory reader](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/modules/Inventory/Inventory/StockPositions/Persistence/StockPositionEventReader.cs)
and [Purchasing reader](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/modules/Purchasing/Purchasing/PurchaseOrders/Persistence/PurchaseOrderEventReader.cs).
Replace the complete in-memory history/snapshot/selection object with a small metadata
range validator. Ordinary persistence reads should select their range through native EF
queries before materialization, validation, decoding and consumer-owned hydration.

The first complete-history shape was too centered on the finite demonstration: it copied
all rows, coupled validation to in-memory version/time selection and required validation of
metadata beyond an earlier requested range. Those capabilities are removed from the library.
Selected-range validation cannot certify corruption outside the supplied range; full-stream
integrity checks remain explicit maintenance work.

The current inline project `ModulithFoundry.Events.History` is retained for review and
independent adoption proofs. Its final package division will be assessed alongside the real
E5.2 EF consumers. Memory-only reuse does not settle that division.

## Concrete interface and obligations

Four public types remain:

- `HistoryPosition(long StreamVersion, DateTimeOffset RecordedAt)`: a readonly record struct
  containing no event payload or type constraint.
- Static `EventHistory.ValidateRange(IEnumerable<HistoryPosition> positions, long afterVersion,
  long throughVersion)`: validate the exact ordered range `(afterVersion, throughVersion]`.
- `EventHistoryException`: bounded failure, nullable expected/observed versions and a fixed
  payload-free message; the consumer attaches stream/product context.
- `EventHistoryFailure`: `UnexpectedVersion = 1`, `RangeMismatch = 2`,
  `RecordedTimeRegression = 3`. These replace the uncheckpointed complete-history interface.

The exclusive lower bound is nonnegative and the upper bound cannot precede it; invalid
bounds/null input are caller errors. Equal bounds require no rows. Lower bound 0 permits a
full prefix starting at 1; an empty `(0, 0]` selection invents no persisted event at version 0.
A nonzero lower bound permits validation of a later contiguous range.

Use one `foreach`, tracking the observed version and previous recorded timestamp. Check
positions against the next expected version, reject excess rows before advancing beyond the
upper bound, require nondecreasing recorded timestamps within the supplied range, and check
the terminal version after enumeration. Equal timestamps remain valid. No indexes, integer
casts, preliminary count, row copies, retained collections or payload generics are needed.

The consumer materializes its selected ordered rows once, validates a metadata projection,
then decodes and hydrates the same rows. Sources must remain stable during those operations.
The utility enumerates metadata once with O(n) work and O(1) validation state. It has no EF,
JSON, actor, tenant, transport, domain-event base, reducer or automatic operation dependency.

## Consumer selection and proofs

Inventory and Purchasing retain their own fixture row shape and domain folds. Their
consumer-owned version selectors reject requests outside a supplied positive head, filter
and order rows to the requested version, then validate the returned prefix.

For a time cutoff, find the maximum qualifying stream version within the supplied head,
then select every row through that version. Validate before decoding/hydration. A cutoff
before the first event selects an empty range and yields no historical state; equal-time
facts remain in stream order. Native EF query translation and real captured-head consistency
belong to E5.2; the present demonstration uses in-memory fixture rows.

Relevant proofs protect:

1. Gap/duplicate/order errors, missing tails/excess rows, nonzero starts, valid empty ranges
   and maximum-version arithmetic.
2. Recorded-time regression within the returned range, equal-time acceptance and one-pass
   enumeration, without array/record/framework-only tests.
3. Independently expected current/historical Inventory quantities and Purchasing replacement
   totals through the actual console recipe and both consumer folds.
4. Existing codec/domain failure boundaries and repeat hydration without accumulated state.
5. Earlier reads succeeding despite metadata corruption outside their selected range, with a
   later read detecting the regression. No full-stream integrity claim follows from earlier success.
6. Consumer selector rejection instead of silently clamping future versions, and exclusion of
   rows beyond a supplied head. These are fixture composition proofs, not PostgreSQL race proofs.

Archived fixtures remain unchanged; authored metadata and additional v1 literals are
consumer-owned. Native build/style/analyzers/formatter and existing architecture policies
accompany these proofs. No restored-project parser, generic repository, payload-interface
hierarchy or synthetic publisher/pending-event framework is introduced.

## Later gate

The reusable candidate is ordered-range integrity checking. Query selection, row shape,
timestamps, domain evolution, business validation, admission and errors remain consumer
policy. Template material is the exercised select/materialize/validate/decode/hydrate recipe;
materialized template output remains E10.

The proposed [E5.2.1](e5-2-1-native-event-history.md) integrates the event families into owning
modules and reviews native EF mappings, tenant-scoped queries and captured-head loading.
E5.2.2 separately reviews expected-version staging and caller-owned save/commit. Real
PostgreSQL proofs across those increments must cover competing appends, capture races,
stream/event-write faults, rollback and recovery. Reassess whether this validator earns a
separate library there. Required views/repair remain E6, and reliable messaging E7.
