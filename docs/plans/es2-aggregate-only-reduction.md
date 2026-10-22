# ES2 aggregate-only reduction

Historical design/execution record. The active interface is superseded by
[the owner-approved native EF replacement](es2-native-ef-simplification.md).
Renamed-source links lead to current replacements; removed mechanisms link to their replacement scope.

Status: **owner-approved and implemented**, 2026-10-07. The owner approved the concrete
public changes and Purchasing replacement below. This supersedes broader projection/catch-up
proposals. Implementation and executable proofs await complete-change-set review; follow-up
edits remain unstaged and existing index entries are preserved. No commit is authorized.
See [the reduction results](../reports/es2-single-stream-rebuilding.md#aggregate-only-reduction-2026-10-07).

## Consumer model and ownership

For registered aggregate writes, one append-only stream has exactly one persisted aggregate
state representing its event head. Domain decisions produce heterogeneous facts; the library
stages their ordered envelopes, the advancing header and that state. Native SaveChanges and
commit remain explicit consumer operations. Preserve the independent raw counter contract
without required state, as resolved in ADR 0006; "every stream" here means registered aggregate
streams, not removing previously supported raw adoption.

Native `{Aggregate}Queries` and reusable IQueryable `{Aggregate}Filters` provide read shapes,
joins, predicates and paging over mapped committed state. Explicit live reconstruction uses
the existing consumer history readers and reducers. It produces an in-memory result without
pending command facts or persistence. The same reducer and captured prefix must agree with
the saved aggregate state. No generic live-projector engine, reader extraction or implicit
catch-up is introduced.

Rebuilding is a separate terminal maintenance lane, reconstructing the aggregate state from
the full retained prefix and preparing a replacement at the same head. Keep existing safe
online per-stream admission: shared before writer loading, exclusive before maintenance
capture. Native optimistic predicates still arbitrate competing appends. A library maintenance worker is still planned and needed eventually; it is deferred to
simplify this change, not dismissed as unnecessary. Consumers can invoke repair/backfill from
their own reconciliation jobs today and own scheduling, scaling and online/offline orchestration.
The selected library gate still defines the supported online safety contract. Defer
queue/table/BackgroundService interfaces to a separately reviewed capability with restart/retry proofs. No maintenance runs in normal loading or appending.

## Reviewed public surface

Keep IEventStore<TAggregate>.GetForWritingAsync and AppendAsync, EventAppendResult,
InlineAggregateAdapter<TAggregate,TRow>, IInlineStateRecord, the heterogeneous stored envelope,
record adapter and lower-level raw appender contract. Typed module contexts remain explicit;
there is no unqualified DbContext registration.

Remove protected ConfigureRequiredProjection and public InlineEventProjection<TEvent,TRow>.
ConfigureRequiredInlineState<TStream,TStoredEvent,TState>(model, streamType) registers the one
aggregate state; remove its isMainState flag and reject a second state for the same family.
Keep ConfigureMainState's existing name for this change rather than add another naming sweep.

Replace IInlineProjectionRebuilder<TAggregate> with IAggregateRebuilder<TAggregate>:

```csharp
public interface IAggregateRebuilder<TAggregate> where TAggregate : class
{
    Task<AggregateRebuildResult?> RebuildAsync(
        Guid id, CancellationToken cancellationToken = default);
}

public sealed record AggregateRebuildResult(long Version, DateTimeOffset RecordedAt);
```

The result still describes staged state, not a commit. ProjectionCount disappears because
there is one aggregate state. AddEventStore<TAggregate,TStore> aliases IEventStore and
IAggregateRebuilder; its TStore constraint changes accordingly. Preserve the shared scoped
instance and explicit context choice. The existing store may forward the maintenance role
to a separate internal implementation, without putting replay/replacement orchestration in
normal append code or adding a public configuration framework.

PreparedEventAppend remains available for raw adoption and internal store use. It prepares
all payloads, keys, identities, positions and one timestamp before tracking. AppendAsync
continues hiding it from command handlers. Remove multi-projection containers and rebuilding
flags from ordinary state preparation; do not collapse preparation and tracking into a loop
that can fail halfway through encoding. No rename of consumer Stage* methods or result types
is needed: those already communicate caller-owned save/commit.

No new runtime dependency is needed. Keep EF Relational/DI abstractions in the EF adapter,
the concrete optional Postgres gate in its provider package and optional event codecs/history
in their independent family. Domain rules, reducer correctness, payload schemas, tenancy,
read projections and maintenance admission remain consumer-owned.

## Purchasing replacement included in scope

Retain purchase_order_summary mapping, migrations, historical seed rows and literal fixtures.
Stop treating that row as a required library projection or writing it during ordinary commands.
ReadSummaryAsync derives the same current Contract values from PurchaseOrderCurrentRow state.
Current main state remains JSONB with no payload/schema replacement. This existing single-ID
query may materialize the checked state and derive its summary; it does not establish server-side
filtering/paging over a calculated total. Any future list filter must use an explicitly tested
mapped expression, not call PurchaseOrderState.Total within an IQueryable.

Replace tests/demonstration assertions that claim atomic secondary-row advancement with
aggregate-only append/rebuild and derived-summary assertions. Preserve demonstrations and
their entry points, explaining the replacement. Tests of the old secondary write guarantee
become historical checkpoint evidence, not guarantees falsely retained in the new slice report.
The retained summary table is legacy data, not a current read model. Do not silently drop it,
regenerate migrations or change archived evidence.

## Exact file/behavior map

Paths beginning EF/ are within
src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/.

| Files | Reviewed replacement scope |
| --- | --- |
| EF/EventStore.cs | One aggregate-state binding/observation/candidate; remove projection collections and secondary configuration; forward rebuild to separate implementation. Preserve raw loading, identity/version checks, transaction association and single-use append. |
| EF/InlineAggregateAdapter.cs | Retain main adapter/state record; remove InlineEventProjection. |
| EF/InlineProjectionStorage.cs | Ordinary row lookup, key mapping and append preparation; remove repair flags and repair-only prepared-row fields. |
| EF/AggregateRebuilder.cs (new), EF/PreparedAggregateRebuild.cs (new) | Separate full replay, replacement preparation/staging and exact native-save association for one state. Share native key/lookup validation; retain original EF concurrency values. |
| EF/IInlineProjectionRebuilder.cs (replace with EF/IAggregateRebuilder.cs) | Narrow maintenance role and two-field staged result above. |
| EF/RequiredInlineStateExtensions.cs | One mandatory state registration, no isMainState option/participant iteration; explicit ordinary append versus exact prepared maintenance save checks. Reject unprepared state-only writes. |
| EF/EventStreamRebuildState.cs, EF/EventStreamWriteGate.cs | Separate admission bookkeeping from prepared replacement validation; simplify one-state association while preserving existing online protocol and transaction/key restrictions. |
| EF/EventStreamReplay.cs, EF/EventAppender.cs, EF/PreparedEventAppend.cs | Preserve replay metadata/raw append contracts and safeguards; adjust references to simplified admission association as needed. No speculative replacement abstraction. |
| EF/EventStoreServiceCollectionExtensions.cs | Register the renamed maintenance role; retain scope/clock/conflict checks and typed consumer construction. |
| samples/Wholesale/modules/{Inventory/Inventory/StockPositions/StockPositionStore.cs,Purchasing/Purchasing/PurchaseOrders/PurchaseOrderStore.cs} | Configure one aggregate state and replay/gate; remove Purchasing Summary binding. Domain decisions and reducers unchanged. |
| samples/Wholesale/modules/Purchasing/Purchasing/PurchaseOrders/{InlineViewMapping.cs,PurchaseOrderQueries.cs,PurchaseOrderInlineProjection.cs} | Retain legacy summary entity mapping; remove required-secondary registration/load; derive current summary from checked aggregate state. |
| samples/Wholesale/modules/{Inventory/Inventory/StockPositions/StockPositionRebuilding.cs,Purchasing/Purchasing/PurchaseOrders/PurchaseOrderRebuilding.cs} | Select IAggregateRebuilder; keep public module maintenance Contracts and explicit admission/staging. |
| samples/Wholesale/IntegrationTests/Scenarios/EventPersistence/{RebuildJourney.cs,README.md}; samples/Wholesale/modules/README.md | Preserve executable entry points; describe aggregate regeneration and query-derived summary, rather than library-managed secondary replacement. |
| samples/Wholesale/IntegrationTests/EventPersistence/{AppendTests.cs,AppendTests.InlineViews.cs,AppendTests.RequiredInlineState.cs,AppendTests.Rebuilding.cs,AppendTests.StateDependent.cs,HistoryReadTests.cs} | Preserve append/history/consumer guarantees; replace secondary-specific expectations with new aggregate/query behavior, retaining failure/rollback/fresh-context proofs. |
| src/Gloaming.EventSourcing/tests/EventSourcingPostgresTests/{RebuildConsumer.cs,RebuildTests.cs,GuardTests.cs,RegistrationTests.cs,GateTests.cs} | Update public role and one-state proof setup; preserve real PostgreSQL writer/maintenance coordination, prefix, rollback and recovery coverage. |
| Family/EF/Postgres READMEs and capability docs; docs/design.md; ADR 0006/0007; ES2 brief, extraction plan and ES2 slice report | Distinguish removed secondary guarantees, retained mechanisms, new executions and checkpoint history. Link the reviewed reduction; document strict loads, raw adoption, live consumer reconstruction and deferred worker/catch-up/multi-stream directions. |

Do not delete secondary rows, migrate schemas, alter fixture bytes, remove demos, reset
checkpoints/index entries or modify archive/T1. The optional Postgres project, gate tests,
TypeScript diagnostic and existing registrations remain; this is a targeted reduction, not
wholesale git restoration. No job table, package addition or worker is in this file map.

## Errors and required verification

Retain configuration errors for missing/duplicate state mapping, wrong family/context/provider,
invalid native transaction and unsupported complete keys. Retain integrity errors for malformed
replay, unreadable or behind normal state and ahead rebuild state; retain native optimistic
conflicts for expected-version/competing writes. Maintenance can repair missing/unreadable or
wrong same-version state through full replay, without decoding the old body. Reject changed
headers/events or unprepared/tampered maintenance saves. Failures require rollback and a fresh
operation context; no automatic retry or hidden save/commit.

Verification must prove state-dependent command eligibility, rejected/encoding-failed
batches before tracking, state/head equivalence, competing writers, maintenance exclusion,
rollback and fresh recovery on PostgreSQL. Preserve heterogeneous raw counter adoption and
event-free T1. Verify derived Purchasing summary against full historical reconstruction,
tenant isolation and preserved seeded history. Run relevant existing consumer/provider suites,
architecture/build/style checks, pending-model checks and frozen archive verification.
Report actual complexity removed after implementation; do not claim a smaller module merely
because code moved files. The reduction narrows the existing rebuilding mechanism rather than proving a new reusable
mechanism. Report its new consumer proofs separately from historical multi-view executions.
