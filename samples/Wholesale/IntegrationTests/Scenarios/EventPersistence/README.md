> This is retained integration-test scenario documentation. The former standalone product
> executable was retired by S1. Use the [Wholesale product](../../../README.md) and its
> canonical API/worker/maintenance roles; this directory owns fixture/journey support only.
> Run proofs with `dotnet test --project samples/Wholesale/IntegrationTests/Wholesale.IntegrationTests.csproj`.

# Native event-history, append and inline-aggregate consumer

This executable uses Events.Serialization and the provided EventSourcing EF history reader in
module-owned live reads and explicit command writers. The EF reader reuses Events.History
integrity checks. Inventory reconstructs stock
positions and stages receipts/issues; Purchasing reconstructs orders and stages line changes.
Both use tenant-discriminated rows in separate schemas of one PostgreSQL
connection, with separate native migration histories. Inventory's command registration now
includes explicit outbox enqueue for accepted stock issues. No broker or worker registration
is needed for the existing default journeys; delivery is a separate opt-in operation.

```bash
dotnet test --project samples/Wholesale/IntegrationTests/Wholesale.IntegrationTests.csproj
```

For an existing stream, the separate [ES3 maintenance command](../../../Worker/Maintenance/stock-regeneration.md)
provides explicit stock-position regeneration, JSON progress, finite conflict retries,
Ctrl+C and restart. It runs independently of the default setup/command journeys.

The caller registers contexts/providers and tenancy explicitly, migrates each module, establishes
one immutable tenant scope per Organization, stages finite authored histories and saves through
native EF. A stock position and purchase order share the same stream GUID across two tenants;
Beta's authored quantities are twice Alpha's. The six durable JSON literals come from
[EventCodecDemo](../../../../../src/Gloaming.Events/samples/EventCodecDemo/Fixtures/README.md), unchanged. Recorded timestamps and the
quantity multiplier are demo metadata/policy, not library behavior.

The second journey opens/drafts different streams through business command Contracts, then
stages and commits a two-event batch for each. The caller visibly begins each native transaction,
saves, commits or rolls back, and disposes the operation scope. Fresh-context reads report the
committed result. No command independently saves, retries or publishes.

Expected output:

```text
wholesale-alpha: stock current=13.000, version-2=10.125, cutoff=10.125, before-open=none
wholesale-alpha: order current=62.50, version-2=31.25, cutoff=62.50, before-draft=none
wholesale-beta: stock current=26.000, version-2=20.250, cutoff=20.250, before-open=none
wholesale-beta: order current=125.00, version-2=62.50, cutoff=125.00, before-draft=none
inventory append: committed-version=3, on-hand=13.000
purchasing append: committed-version=3, total=62.50
inventory inline: committed-version=3, on-hand=13.000
purchasing inline: committed-version=3, total=62.50
purchasing summary: committed-version=3, lines=1, total=62.50
inventory issues: committed-version=5, on-hand=6.000, rejected=7
inventory rebuilt: version=1
purchasing rebuilt: version=1
```

Setup skips already-present streams for an ordinary sequential rerun. It has no concurrent
bootstrap guarantee. Command setup also skips completed streams on a sequential rerun;
the command protocol below provides expected-version protection for its explicit identity.
Use a disposable database; demonstration
setup is not account admission or business-key discovery. There is no cross-module atomic setup.

## Command contract

`AddStockPositionCommands()` and `AddPurchaseOrderCommands()` explicitly bind the internal
implementations after the caller registers the native contexts. History registration is needed
for explicit replay, not for command loading. Open/draft
requires expected version 0; receipts/line changes require a positive expected version and a
nonempty valid batch. The established tenant supplies ownership; DTOs carry no tenant or actor.

`IssueAsync(IssueStock, ...)` validates all positive quantities, loads the required
inline state at the requested version and accepts only a complete batch whose sum fits
current OnHand. InsufficientStock carries Available/Requested and leaves no staged rows.
Inventory owns this rule: stock leaving one position, with no reservation, catalog mutation
or cross-module sale. Historical evolution subtracts issued facts without applying current
eligibility or authorization again.

Both modules inject `IEventStore<TAggregate>` and configure the provided EventStore base once.
Handlers use GetForWritingAsync, domain Create/operations and AppendAsync; bindings no longer
repeat lookup/family/version/transaction orchestration. The library owns those validations,
identities, positions, one batch clock sample and aggregate bookkeeping.
Domain decisions, pure reducers, codec registration, aggregate state and native completion
remain explicit consumer code. [StockIssueJourney](StockIssueJourney.cs) uses a separate stream:
load 13 at version 3, issue 4 and 3 to reach 6 at version 5, then reject issue 7 in a fresh scope.

Commands return Changed, NotFound or Conflict; issues also return InsufficientStock. Changed contains proposed business state, not
durable success. A competitor can still win before saving. Native EF preserves the original
header version in its UPDATE predicate; owned stream keys arbitrate competing creation.
Native composition helpers classify only stream/required-view concurrency and the known PostgreSQL
header/position unique constraints. Other faults retain their actual meaning.

Configure TimeProvider once; the library records one UTC timestamp per batch, without regression.
Module registration uses TimeProvider.System when no clock is supplied; this finite demo configures
a scoped deterministic clock. Business command Contracts take no timestamp. Stage at most one batch per
stream in an operation context; tracked headers detect repeats before and after save. Manual
clearing/detaching tracking mid-operation is outside this recipe. Different
streams may participate in the same module transaction. On failure, roll back and discard the
entire context/proposal; a retry requires a fresh scope and an explicit new decision. An ambiguous
commit error does not establish rollback or safe retry. Caller-controlled commit is the success
boundary. [AppendJourneys](AppendJourneys.cs) makes that ownership visible.

## Inline aggregate state and native read shapes

Register `AddStockPositionQueries()` / `AddPurchaseOrderQueries()` explicitly to read committed
inline state. Inventory owns stock_position_current; Purchasing owns purchase_order_current.
ReadSummaryAsync derives its current result from checked PurchaseOrder state. The legacy
purchase_order_summary table, mapping, migrations and seeded rows remain historical data;
ordinary commands do not maintain it and current queries ignore it. The caller's append
transaction saves headers, events and the one aggregate state together. Explicit model
declarations and both native save overrides validate tracked state/metadata/event ranges;
a detached required state cannot silently leave events alone.
This contract excludes bypass SQL/bulk saves and does not validate a manually altered state body.
ReadAvailableAsync uses StockPositionFilters over mapped onHand state and scoped native joins,
with server-side filtering rather than historical payload replay.
The availability catalog remains a separate state-stored demonstration.

Commands read module-internal decision state from the inline aggregate and check that state
against the observed header. Missing/behind views or mismatched timestamps fail before
staging; append never repairs them. A view ahead of the header observed earlier is a concurrency
outcome, since a competitor can commit between reads. Command loading returns Conflict;
view queries throw native DbUpdateConcurrencyException. Queries do not silently retry or promise
a database snapshot across their separate reads.

Complete domain evolution, arithmetic validation, event encoding and state payload preparation
finish before tracked rows change. Purchasing validates its final total after the complete batch;
its current summary is derived from that state. The aggregate base collects only
new accepted facts; historical reconstruction collects none. Existing pure reducers are shared
within each module. No discovered projector registry is added.

Inline commands validate their decision state and captured version, rather than certifying
all historical facts on each edit. Privileged historical corruption can therefore leave an inline
command usable while explicit replay fails. Stop affected writes and investigate known corruption;
this slice adds no automatic corruption discovery or repair. Consumers retain that admission policy.

## Live and temporal read contract

The host calls only business Contracts. Module implementations require established tenancy
and use named EF ownership filters on both headers and events. Missing/foreign streams return
null. Current reads capture a positive committed header version; at-version reads require a
positive version within that head. Before-first time reads return null; time means recorded
time rather than event occurrence or commit order.

Consumer native queries select the maximum qualifying version for an intermediate cutoff, then
the provided reader fetches
its complete ordered prefix. A cutoff at/after header update time targets the full captured
head so a missing tail cannot appear as an earlier state. Selection, ordering and ownership
predicates execute in PostgreSQL before materialization. Equal recorded timestamps preserve
version order.

The provided reader validates positions and stream/event timestamp consistency using the complete
mapped key and parameterized SQL. Each module supplies decoding and evolves consumer-owned state.
It saves nothing and has no publisher, event handler or
pending-event collection. Purchasing results wrap reconstructed lines in a read-only collection.

Error boundaries remain visible: argument errors for invalid versions, EventHistoryException
for selected-range integrity, InvalidDataException for header inconsistency, EventDecodingException
for codec failures and InvalidOperationException for invalid domain sequences/values. Native
database faults and cancellation propagate. No retry or exception-to-null fallback is added.
These are consumer error policies, not a new Foundry error protocol or HTTP response policy.

## Scope and proofs

[The read suite](../../EventPersistence/HistoryReadTests.cs) runs actual migrations,
launches this executable and checks all twelve output lines. Native EF command
interception observes SQL and coordinates a commit between header capture and event selection;
no production test hook is present. A later append is excluded from that captured read and
included by a fresh read. [The append suite](../../EventPersistence/AppendTests.cs)
proves competing prepared writers, tenant isolation, visibility only after commit, header/
envelope faults, rollback across two saves and fresh-context recovery. Deterministic races
prepare both writers at the same head before saving the winner; no timing sleeps are used.
Cancellation proof is before commit dispatch, not recovery from an ambiguous in-flight commit.

[Inline-view proofs](../../EventPersistence/AppendTests.InlineViews.cs) deny actual PostgreSQL
event SELECT permission while allowing editing, check derived two-item totals/replacement,
reject missing/lagging/mistimed aggregate state, coordinate a commit between header/state reads
and reject invalid/overflowing batches before tracking. The append matrix
also checks committed aggregate state and SQL failures of header/events/state. This is consumer protocol coverage, not another EF
feature matrix.

[State-dependent proofs](../../EventPersistence/AppendTests.StateDependent.cs) check
whole-batch issue eligibility, zero-stock completion, isolated tenants, stale/damaged/ahead
observations, competing eligible decisions, required-view/event faults and fresh-context
redecision. Historical E6 secondary-view assertions were replaced by the reviewed aggregate-only
contract; earlier successful multi-view executions remain historical checkpoint evidence.

Ordinary read consistency assumes atomically committed, append-only histories. Arbitrary
privileged updates/deletes can invalidate that assumption; selected-range success does not
certify excluded rows. Full replay is unbounded in history length and no snapshot or capacity
claim is made. The as-of query's index/performance needs depend on actual workloads.

The availability catalog remains a separate state-stored Inventory demonstration. This
increment does not make it an event projection. One required inline aggregate and explicit
bounded repair were the scope of that checkpoint. Outbox/inbox and explicit accepted-change
audit are now exercised by the finite journey below. The retained E6
migrations create empty state tables; they do not automatically populate them for existing streams.
The finite seed now adds matching main state from the unchanged literals and
saves each module in its own explicit native transaction. Command-created streams establish
state through the provided store. Historical reconstruction remains available separately. Provider-specific jsonb mapping is sample code; other DBMSs are unproven.

[Composition](DemoComposition.cs), [fixture recipe](DemoJourneys.cs),
[module ownership](../../../modules/README.md), [append findings](../../../../../docs/reports/e5-2-2-native-event-append.md),
[inline-view findings](../../../../../docs/reports/e6-1-inline-decision-state.md).
See [the store report](../../../../../docs/reports/es1-library-write-store.md) for fresh save-boundary
omission/corruption, preparation-conflict, query translation and recovery proofs. T1 already
provides an event-free generated composition; event template presets remain deferred.

## Explicit single-stream maintenance

[RebuildJourney](RebuildJourney.cs) is now part of the executable. A trusted local host selects
AddStockPositionRebuilding/AddPurchaseOrderRebuilding, establishes admitted tenancy, invokes
aggregate-specific maintenance Contracts in fresh explicit transactions, saves/commits and
uses a fresh context for the next command. Existing demos/fixtures remain intact. There is no
HTTP admin endpoint, permission model or automatic repair during ordinary reads.

Inventory and Purchasing each reconstruct one main JSONB aggregate state. Purchasing summaries
are native query projections of that state; the dormant summary entity/table is removed by a new
migration. Writing and maintenance are independent implementations and registrations. The store
binds only state/event mappings; the rebuilder injects the module's provided-reader implementation
and retains pure evolution.
The library validates the captured prefix, applies one state replacement and checks exact native
save participation. Appends and repair both change a header concurrency stamp. A stale writer or
repair loses at native SaveChanges; operations do not block before loading. Repair preserves event
version and timestamps and never rewrites retained facts. Roll back/dispose and reload after a
fault. These guarantees rely on the explicit native transaction and both save guards, not arbitrary
SQL or external writers. Module ownership, authorization and final completion remain consumer policy.

[Library contract](../../../../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/README.md)
records setup, native errors and recovery limits. A maintenance worker remains planned and needed
eventually; consumers can host reconciliation now and own scheduling, scaling and locking/windows.
Ordinary reads/writes remain strict; no silent repair, snapshot catch-up or automatic retry.

## Mixed historical and current schemas

```bash
dotnet test --project samples/Wholesale/IntegrationTests/Wholesale.IntegrationTests.csproj
```

With the same disposable connection setting, the separate
[SchemaEvolutionJourney](SchemaEvolutionJourney.cs) seeds unchanged v1 stock fixtures, appends
one current v2 receipt, then explicitly rebuilds through the module maintenance Contract and
native save/commit. Fresh live and inline reads agree:

```text
inventory schemas: retained-v1=10.125, current-version=4, inline=15.000, rebuilt=15.000
```

Inventory's [v1 transformation](../../../modules/Inventory/Inventory/StockPositions/ReceiptV1ToV2.cs)
renames quantity to receivedQuantity while preserving other fields. Production CLR/domain
Quantity, decisions, reducers and inline-state JSON are unchanged. Only newly encoded receipts
use schema 2; opening/issue events retain schema 1. Retained rows still contain their exact old
payload/schema. The journey compares complete event rows before/after rebuilding and permits
ordinary sequential reruns; it adds no concurrent bootstrap guarantee.

The [PostgreSQL proofs](../../EventPersistence/AppendTests.Upcasting.cs) cover mixed-schema
live/temporal reads, missing/corrupt aggregate reconstruction, rejected old payloads before
tracking, native repair failure/rollback, unchanged retained event facts and fresh recovery.
The executable flag is exercised against PostgreSQL. No automatic read repair or data migration
is installed. Shape-only decoding compatibility does not require rebuilding already correct
state. Readers must support the new schema before newer writers are enabled; rollout and
projection-meaning changes remain consumer responsibilities.

## Transactional stock-issue notification

The explicit `--outbox` journey uses the same state-dependent Inventory command. One accepted
issue batch maps to a consumer-owned StockIssueRecordedV1 and queues it alongside events,
stream version and main inline state in the caller's transaction. Rejection queues nothing;
rebuilding/replaying old facts has no external effects. Existing fixtures, migrations and
default output remain intact; a new forward migration adds the module's outbox table.

```bash
dotnet test --project samples/Wholesale/IntegrationTests/Wholesale.IntegrationTests.csproj
```

The journey explicitly owns native RabbitMQ connection/channel, durable queue declaration,
persistent publication, confirmation tracking and mandatory routing. The publisher has a
five-second publication bound; the transport remains sample source. After native save/commit,
one separately resolved privileged dispatcher publishes at most one eligible module row.
It can select older retained work when a backlog exists. No library bus, startup migration,
automatically enabled worker or immediate producer publication is installed.

MessageId survives retries. The sample creates one business CorrelationId per operation scope to group
related work/messages; that is not a deduplication key. No incoming command ID exists here, so it
does not invent a CausationId. Owner metadata is retained, but trusted receiver tenant
admission remains consumer policy. Library dispatch drains the module table across owners
without rebinding its context or treating a query filter as privileged authorization.

The three PostgreSQL/RabbitMQ proofs cover confirmed acceptance followed by completion
failure and repeat delivery, mandatory unroutable failure/recovery and this finite executable.
Their manually acknowledged receiver is a transport observer, not an inbox business handler.
The separate `--inbox` journey below demonstrates committed durable intake before ack and
independently committed business processing. See [Messaging capabilities](../../../../../src/Gloaming.Messaging/docs/capabilities.md)
and [new versus historical evidence](../../../../../docs/reports/outbox1-transactional-dispatch.md).

## Durable stock-issue command intake

```bash
dotnet test --project samples/Wholesale/IntegrationTests/Wholesale.IntegrationTests.csproj
```

The explicit [InboxJourney](InboxJourney.cs) publishes an IssueStockV1 command, validates the
configured producer/schema/Organization, commits retained intake and only then acknowledges
its native RabbitMQ delivery. Separate processing evolves the stock aggregate, saves facts,
stages an explicit accepted-change audit, queues StockIssueRecordedV1 and completes the inbox
row in one local transaction. A handled
not-found/conflict/shortage instead completes with StockIssueDeclinedV1 and no new stock facts.
The module initializes a fresh admitted tenant scope before business queries; a TenantKey or
producer header is not authentication. The finite demo admits only wholesale-alpha/beta from
its configured demo.stock-commands source; production trust/broker permissions remain host policy.

The receiver establishes a separate ActorIdentity scope with its trusted System identity
`inventory.stock-issue-worker`. No human initiator is present in the current wire contract.
The internal InventoryAudit wrapper gathers actor/tenant, clock and generated entry ID. The named StockIssuedDetailsV1 record keeps payload fields and its version constant together;
the business call supplies stock ID/version/quantity only.
Each owning DbContext selects JSONB through native EF mapping; native module mapping owns tenant visibility and its
opt-in HasSubjectTimelineIndex helper and its native index customization.
Audit retains stock identity, version and quantity without correlation, causation or trace
IDs. The executable prints the committed action/actor and reply identity/cause.
`inventory.audit_entries` is module-owned through a
new forward migration and native tenant filter/save validation. Direct commands, seeds,
refusals, duplicate deliveries and rebuilding do not gain automatic accepted-change audits.
Classification, authorization, query visibility and retention remain consumer policy.

Incoming correlation is carried to the reply; causation is the incoming MessageId. Direct
commands create a conversation per operation scope and omit invented causation. Missing
incoming correlation remains absent in the reply. The receiver and optional processor are explicitly registered; existing default/browser behavior
does not subscribe to a broker or enable a worker. New forward migrations preserve older ones.

Fourteen real PostgreSQL/broker cases cover accepted/refused decisions, independent commit
requirements and header/event/state/audit/reply/completion rollback, fresh recovery, tenant
admission, populated pre-audit migration preservation and this executable. Read the
[Inbox contract](../../../../../src/Gloaming.Messaging/docs/inbox.md) for native row-lock processing
and remaining operational limits, and the [audit contract](../../../../../src/Gloaming.Auditing/docs/transactional-audit.md)
for explicit participation and its consumer obligations.

Optional message diagnostic context and native telemetry setup are documented in the
[Messaging observability contract](../../../../../src/Gloaming.Messaging/docs/observability.md).
