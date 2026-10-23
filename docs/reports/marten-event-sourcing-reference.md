# Marten event-sourcing reference

Research date: 2026-10-06. This is external design evidence and extraction guidance,
not a new executable proof or an approved library interface.

The official documentation currently labels itself v9.x. Source links below target
`master`; they are moving references, not a pinned release. Attempts to retrieve a
release source reference and the current commit through the available browser did
not succeed. Configuration-dependent details are therefore described explicitly;
this report does not claim a single universal Marten schema.

## Separate stream registry and event records

Marten stores events in `mt_events` and stream metadata in a separate `mt_streams`
table. `mt_event_progression` records asynchronous projection progress. Event data
normally occupies JSONB `data`; each event has a GUID identity, stream reference,
within-stream version, timestamp, event-type alias, tenant identifier and optional
.NET type name. The conventional store-wide `seq_id` supports asynchronous
processing and is distinct from the within-stream version. Event aliases let
deserialization avoid requiring CLR type information inside every payload.
[Storage documentation](https://martendb.io/events/storage)

The stream-table source declares `id`, nullable aggregate `type`, `version`,
last-update `timestamp`, `created`, tenancy and archiving information. In conjoined
tenancy the primary key contains tenant first, then identity. Aggregate type is
metadata, not part of that primary key. GUID and string stream identities are
alternative configurations. The source also contains a compaction watermark;
these optional lifecycle features should not become mandatory Foundry fields.
[Stream-table source](https://raw.githubusercontent.com/JasperFx/marten/master/src/Marten/Events/Schema/StreamsTable.cs)

The ordinary unpartitioned event table uses the sequence column as primary key,
references the stream registry, and enforces a unique stream/version position.
Conjoined tenancy extends the stream reference and position constraint with tenant.
Some partitioning modes alter these guarantees: per-tenant sequence allocation
requires tenant in the event primary key, and the examined tenant-partitioned mode
omits the parent stream foreign key. Optional GUID uniqueness, binary payload and
metadata columns further vary the shape. Our reviewed FK and event-identity choices
should be justified by our own guarantees rather than copied without qualification.
[Event-table source](https://raw.githubusercontent.com/JasperFx/marten/master/src/Marten/Events/Schema/EventsTable.cs)

Stream identity configuration changes both registry identity and event reference
column types. Conjoined tenancy is opt-in. These are reasons to keep module schema,
tenancy and provider configuration explicit in consumers.
[Configuration](https://martendb.io/events/configuration.html)

Correlation, causation, user name and headers are individually optional metadata
columns. Marten flows enabled metadata from its session; Foundry's explicit-control
principle instead calls for deliberate consumer metadata construction. Neither
actor identity nor tenancy should be forced into every event payload or library.
[Metadata](https://martendb.io/events/metadata.html)

## Aggregate state and projection lifecycle

Live aggregation reads events and calculates state in memory without persisting a
view. Inline projection persists its view with the appended events in the same
database transaction. Asynchronous projection updates views through background
processing with eventual consistency. A single-stream projection and a view that
combines multiple streams therefore have different identity and consistency needs.
[Projection overview](https://martendb.io/events/projections/)

Marten supports explicit `Evolve`/`EvolveAsync` methods taking existing state and an
event, returning the next state. More complicated projection lifecycles can return
explicit update/delete decisions through `DetermineAction`. This supports a useful
separation: deterministic domain evolution can be reused for live replay and stored
views, while native persistence operations remain separate.
[Explicit projection code](https://martendb.io/events/projections/explicit)

`FetchForWriting<T>` combines current aggregate state, observed stream version and
an append handle, with concurrency enforced when the session saves. It supports
single-stream aggregations rather than arbitrary multi-stream projections. This is
a framework-managed implementation of a useful invariant: decisions must use the
state corresponding to the version against which their events are appended.
[Command workflow](https://martendb.io/scenarios/command_handler_workflow.html)

Current aggregate-reading guidance recommends separating application logic from
projected state. Its decider example explicitly computes events from aggregate
state. Foundry can demonstrate rich aggregate wrappers or functional deciders
without adopting automatic dispatch, discovery, persistence or event collection.
[Reading aggregates](https://martendb.io/events/projections/read-aggregates.html)

## Production of views is distinct from code generation

Marten 9 removed the earlier runtime Roslyn generation pipeline. Conventional
projection methods now use a compile-time source generator and `partial` classes;
explicit `Evolve`/`EvolveAsync` overrides do not need source generation. Our no-code-
generation decision can coexist with producing and rebuilding projection data
through explicitly authored evolution logic.
[F# and projection dispatch](https://martendb.io/events/projections/fsharp.html)

Asynchronous processing needs more than a sequence column and `WHERE sequence >
checkpoint`: Marten's daemon distinguishes committed progress from sequence gaps
left by pending or rolled-back appends. It also owns scheduling and optional leader
election. Those framework mechanisms are outside the present mapping capability.
[Async daemon](https://martendb.io/events/projections/async-daemon.html)

Rebuilds are another explicit capability. Marten offers a single-stream rebuild
operation and separate broader projection rebuild machinery. Its optimized
stream-by-stream rebuild documentation cautions against using that optimization
when multiple views consume the same stream. Independent write and summary views
need explicit proofs rather than an assumed one-stream/one-view correspondence.
[Rebuilding projections](https://martendb.io/events/projections/rebuilding.html)

## Archived implementation comparison

The archive implements one event_streams/events pair per module. Its StreamType is a stable
technical header field, not an EF inheritance discriminator. Inventory's event record adds
GlobalSequence and JSON metadata; both are consumer-mapped fields, beyond the active E5.3
minimum. The global sequence has a unique index but is not the stream order or a commit cursor.
See the archived [stream mapping](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/modules/Inventory/Inventory/StockPositions/Persistence/EventStreamConfiguration.cs),
[event mapping](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/modules/Inventory/Inventory/StockPositions/Persistence/StoredEventConfiguration.cs)
and [recorded-order rules](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/docs/plans/event-sourcing.md#history-rebuild-and-deferred-async-work).

| Archived capability | Concrete evidence | Extraction assessment |
| --- | --- | --- |
| Rich aggregate wrapper | [StockPositionAggregate](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/modules/Inventory/Inventory/StockPositions/StockPositionAggregate.cs) and [PurchaseOrderAggregate](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/modules/Purchasing/Purchasing/PurchaseOrders/PurchaseOrderAggregate.cs) evolve a proposed complete batch before retaining pending facts and advancing version. Historical events do not become pending events. | Compare optional pending-batch bookkeeping; do not impose aggregate inheritance or automatic event collection. |
| Decider and policy | [StockPositionDecider](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/modules/Inventory/Inventory/StockPositions/StockPositionDecider.cs), [StockPositionPolicy](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/modules/Inventory/Inventory/StockPositions/StockPositionPolicy.cs) and PurchaseOrderAggregate distinguish command decisions from final-candidate validation. | Business rules and value-object limits remain in the consumer. Historical evolution must not rerun current eligibility or authorization. |
| Inline decision loading | [StockPositionStore](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/modules/Inventory/Inventory/StockPositions/Persistence/StockPositionStore.cs) and [PurchaseOrderStore](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/modules/Purchasing/Purchasing/PurchaseOrders/Persistence/PurchaseOrderStore.cs) read a persisted aggregate-shaped view and compare its version with the registry. | Demonstrate this loading path in the active sample before considering shared version/load coordination. The current append sample reconstructs from events. |
| Live/temporal projection | [StockPositionEventReader](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/modules/Inventory/Inventory/StockPositions/Persistence/StockPositionEventReader.cs) and [PurchaseOrderEventReader](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/modules/Purchasing/Purchasing/PurchaseOrders/Persistence/PurchaseOrderEventReader.cs) select a captured-head prefix, decode facts and evolve state. | Current codec/range utilities cover part of this flow. A generic fold needs to remove meaningful obligations beyond a foreach loop before becoming another library. |
| Required inline views | [StockPositionInlineProjection](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/modules/Inventory/Inventory/StockPositions/Persistence/StockPositionInlineProjection.cs) stages the write view; [PurchaseOrderInlineProjection](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/modules/Purchasing/Purchasing/PurchaseOrders/Persistence/PurchaseOrderInlineProjection.cs) stages the write view and an independent summary. | Strong candidate: explicitly invoked accepted-batch/required-projector coordination. Keep view reducers, mappings, admission and save/commit consumer-owned. Missing or behind views fail rather than trigger implicit repair. |
| Bounded reconstruction | [StockPositionProjectionRebuilder](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/modules/Inventory/Inventory/StockPositions/Rebuild/StockPositionProjectionRebuilder.cs) replays a known stream and replaces its view under an Organization-wide writer barrier. | Compare reconstruction mechanics separately from privileged admission, stream discovery, lock scope and operational audit. Purchasing has live reads but no implemented repair. |
| Integration-fed reference projection | [StockItemProjectionBootstrapper](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/modules/Purchasing/Purchasing/StockItemProjection/StockItemProjectionBootstrapper.cs) and [reconciliation](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/modules/Purchasing/Purchasing/StockItemProjection/StockItemProjectionReconciliation.cs) combine an owner-provided snapshot, checkpoint and subsequent integration updates. | A different lifecycle from projecting private event history. Keep its snapshot/tail ordering concrete until another consumer establishes reuse. |

These are source findings and historical proofs, not new executions. In particular, the
archived [PurchaseOrderPersistenceTests](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/tests/PersistenceTests/PurchaseOrderPersistenceTests.cs)
include an edit with event-history reads unavailable, independent literal totals, failure of
required persistence participants and missing/behind views. The archived
[correctness gate](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/docs/plans/event-sourcing-correctness-gate.md)
records important limits: business-key uniqueness depends on rebuildable lookup views, Inventory
repair coordinates all participating Organization writers, and retained child state changes
replay cost substantially. Those limits must survive extraction rather than disappear behind
generic storage or projection interfaces.

The inspected archive contains explicitly authored projection production, staging and repair.
It does not contain a general projection code generator or common projector registration/runtime.
Its [event-sourcing design](https://github.com/oxface/modulith-foundry/blob/15d1ec6ce85f872e4547b37c33658dea76b25d03/docs/plans/event-sourcing.md#multiple-projections-and-explicit-operations)
proposed an explicit projector collection/coordinator; that is a candidate, not an implemented
library. Producing a projection from events remains compatible with the no-code-generation rule.

DDD affects how accepted batches, decision state and required views are coordinated much more
than the minimal registry/envelope table pair. A persisted registry record is not the business
aggregate. Multiple views can derive from one stream, and one consumer may use a rich wrapper
while another uses a decider directly. The registration utility should remain independent of
those domain choices.

## Recommended next comparison

The revised [E6 plan](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/library-extraction.md#e6-required-inline-views-and-bounded-repair)
makes aggregate loading, candidate-batch acceptance and required inline views explicit before
repair. First reimplement those behaviors in both active families, with native caller-owned
transactions and independently expected state. Compare the resulting obligations before
proposing a shared coordination interface. Repair is a subsequent reviewable capability;
async progress, checkpoints and projection revisions require their own real use cases.

Library candidates are accepted-batch bookkeeping, required-view version checks, explicit
projector coordination and bounded reconstruction mechanics. Template findings are editable
domain/decider/projector definitions, native mappings and explicit use-case wiring. The sample
must exercise both live reads and inline decision loading. None of these candidates becomes
a reusable mechanism solely because the archive or Marten supports it.

## Current storage finding

These findings support retaining a separate technical stream registry and shared
event table per module. They do not establish that the current mapping utility is a
complete event-sourcing library. Projection consistency, domain decisions, live
rehydration, rebuild safety and asynchronous progress remain separate capabilities.
Consumer-chosen payload column configuration is compatible with this separation;
the current `JsonElement` interface still specifically describes JSON payloads.

`EventStreamRecord` is a clearer name for registry storage than the earlier `StreamRow`:
it identifies durable stream metadata without implying a domain aggregate or a
Marten append handle. `StoredEventRecord` names its durable event envelope. The owner
approved proceeding with this naming; the active interfaces and standalone consumer now use
it. Future generic utilities remain owner-review proposals.

No new reusable mechanism was proven by this research. No runtime tests were run
for this document. Existing PostgreSQL proofs apply to the current implementation,
not to all Marten behaviors described here.
