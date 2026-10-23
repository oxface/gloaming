# E6.1 inline decision state and required views

Historical design/execution record. The active interface is superseded by
[the owner-approved native EF replacement](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/es2-native-ef-simplification.md).
Renamed-source links lead to current replacements; removed mechanisms link to their replacement scope.

2026-10-06. The owner-approved [scope](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/e6-1-inline-decision-state.md) is implemented
following checkpoint `abcd370` (E5.2.2 and E5.3), and was checkpointed separately as `1ae13d4`.
The results below are the original local executions, not remote CI or fresh runs for this
status update. T1 subsequently completed the event-free template rehearsal as `8ccf4c8`.
The next proposed capability is [ES1 bounded append](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/es1-bounded-event-append.md),
not projection repair. No new commit is authorized by this report.

## Outcome

Inventory receipts and Purchasing line changes now load persisted decision state instead of
replaying events. Each accepted batch explicitly stages its header advancement, event rows and
all required inline views in the caller's native module transaction. Stock Position has one
aggregate-shaped write view; Purchase Order has a write view and an independently evolved
summary. The caller still saves, commits, rolls back and disposes.

**No new reusable mechanism was proven.** No technical library or library interface changed.
The existing ownership, event-storage registration, codec and history utilities compose with
consumer-defined state and views. The implementation deliberately compares two concrete
aggregate families before proposing a shared projection abstraction.

## Domain and storage changes

Module-internal StockPositionState and PurchaseOrderState are the decision states. Existing
public history DTOs remain query/command results. Evolution folds recorded events into domain
state; historical readers use that evolution explicitly. Commands prepare typed events,
complete candidate state, result DTOs and serialized event/view payloads before attaching or
adding any row. Ordinary immutable local values suffice; no aggregate base, generic decider,
pending-event container or new per-context bookkeeping was needed.

Candidate arithmetic is checked before staging. Purchasing validates the total after the
complete batch, allowing an intermediate large total to be replaced by a representable final
candidate. An individual unrepresentable line amount still fails preparation. This establishes
arithmetic representability, without inventing another business constraint for the sample.

Native migrations add three consumer-owned tables:

| Table | State and purpose |
| --- | --- |
| `inventory.stock_position_current` | Structured stock-position write state. |
| `purchasing.purchase_order_current` | Structured purchase-order write state. |
| `purchasing.purchase_order_summary` | Independently maintained code, currency, line count, total and per-item amounts. |

Every table has an Organization/stream key, required stream relationship, explicit ownership
filter, version concurrency token and recorded timestamp. Structured state/amounts use native
JsonElement/jsonb mapping. Summary columns can be queried with native LINQ. Schema, provider,
row types, payload mapping and migrations remain consumer code; the storage library does not
register or populate these views.

The summary loads and evolves its own committed per-item amounts. A replacement updates the
selected item's amount and recalculates the final summary; it does not inspect the proposed
PurchaseOrderState. Required views are selected through explicit module-local projectors.
Registration helpers bind the selected projector once even when both commands and queries
are registered; no scanning or automatic handler discovery is introduced.

New IStockPositionQueries and IPurchaseOrderQueries expose committed-view reads through module
Contracts. Existing history Contracts, including their ReadCurrentAsync methods, keep their
live reconstruction semantics. The executable demonstrates both kinds of read and the
independent summary after actual command commits.

## Consistency and assurance

Loading observes the stream header and required views without tracking. Missing or behind
views, invalid headers and disagreeing timestamps fail as integrity errors. An ahead view
following an older header read is a concurrency outcome: command staging returns Conflict,
while committed-view queries propagate native DbUpdateConcurrencyException. Neither path
performs an implicit retry or promises a database snapshot across separate reads.

Native original version predicates on the header and every required view arbitrate writes.
Explicit native Attach/SetValues preserves observed versions. The consumer's narrow failure
classification now recognizes those required view types as well as its stream row, while an
unrelated catalog concurrency failure remains a fault. Caller rollback covers failures from
any participant, including a later save in the same transaction.

An ordinary inline edit validates its required state, candidate and observed header. It does
not certify excluded history or detect every possible privileged, self-consistent view
mutation. Command preparation checks summary amount/count/total consistency; committed summary
reads do not replay facts or certify their equivalence to the write view. Explicit live reads
continue to validate the recorded prefix. Two former damaged-history append cases now make this
changed assurance explicit: inline edits can proceed from intact views while live reads reject
the deliberately damaged history.

## Fresh executable evidence

The real PostgreSQL Testcontainers event suite now has **111 cases**, up from 82. Its existing
writer, tenant, cancellation, visibility and fault cases also observe the new committed views.
The increment adds 26 focused cases and three required-view fault cases:

- Both modules edit successfully under a real database role denied SELECT on their events
  table; their explicit live reader fails with PostgreSQL permission error `42501`.
- Purchasing's two-item replacement produces independently expected line count and total.
  The final-batch case accepts replacement of a temporary overflowing total with final total 3.
- Each required-view write can fail; rollback leaves all previously committed views, events
  and header intact, and a fresh-context retry succeeds.
- Missing, behind and timestamp-damaged views reject staging and committed queries without
  tracked mutations or silent repair.
- Deterministically committing between header and view reads exercises the ahead-view path
  for commands and queries in both modules. Competing native writers preserve one winning batch.
- Later invalid input, stock quantity overflow, line-product overflow and final-total overflow
  leave no partially accepted proposal. Existing missing/tenantless and foreign-tenant cases
  cover committed-view Contracts as well as live/command behavior.
- The actual child executable emits the original history/journey lines plus three committed
  inline/summary lines, verified against literal expected values.

The tests reuse the existing native contexts, migrations and command observation, extending
our protocol assertions rather than duplicating EF's transaction or query behavior. No fake
store, framework test matrix or new architecture parser was added.

| Fresh check | Result |
| --- | --- |
| Root solution build, single MSBuild node | 40 projects; zero warnings/errors. |
| EventPersistenceDemo.Tests | 111 passed. |
| HttpIdentityDemo.Tests | 97 passed. |
| PersistenceTests | 6 passed. |
| PersistenceDemo.Tests | 36 passed. |
| EventStorageDemo.Tests | 10 passed. |
| ArchitectureTests | 65 passed. |
| Inventory and Purchasing native pending-model checks | No changes detected. |
| CSharpier including generated files, semantic style and analyzers | Passed. |
| Active Markdown file links and whitespace | Passed. |
| Frozen archive manifest | All 800 original files verified. |

These are **325 passed cases**, with no skipped cases across the six listed suites. The five
PostgreSQL suites were rerun because they share the fixture changed below. Prior library,
browser, runtime, broker and archive test results are historical evidence, not fresh E6.1
executions. The final formatting check includes native generated migrations and snapshots;
semantic checks target the active solution. Archive source remains unchanged.

The first event test process terminated with an internal CLR error before completing any
cases; its cause was not established. The isolated rerun completed 110 cases successfully,
with one setup failure (`53300`, too many clients): each disposable database retained its own
idle Npgsql pool. The shared test fixture now disables pooling for these disposable connections.
Production connection configuration is unchanged. All five affected PostgreSQL suites passed
after this fixture change; no product retry or increased server connection limit masks a failure.

## Extraction comparison

| Candidate | Finding after the concrete implementations |
| --- | --- |
| Required-view version/timestamp checks | Similar checks exist, but required cardinality and integrity policy are consumer choices. A shared wrapper would currently move a small comparison without hiding meaningful complexity. Keep local and reassess with repair. |
| Accepted-batch bookkeeping | Detached immutable state, events and serialized candidates make the boundary explicit. No shared pending-event state machine was needed. |
| Projector execution | Stock folds one state; Purchasing separately folds aggregate lines and summary amounts. A generic fold or registry adds an interface without owning the actual selection, evolution or persistence policy. |
| View staging | Native Attach/SetValues with observed tokens is sufficient. No hidden save, transaction adapter or required custom session is justified. |
| Repair coordination | Not implemented here. A future bounded repair proof may earn shared mechanics; ordinary replay is insufficient evidence. |

This finding rejects an immediate generic projection library; it does not conclude that
projection and repair can never yield reusable mechanisms.

| Area | Finding |
| --- | --- |
| Library | Existing opt-in utilities compose; no new reusable mechanism or public technical interface was proven. |
| Template | Editable domain state, explicit projectors/required views, native mappings/migrations, query registration and caller transaction orchestration are exercised recipes. Materialized template output remains E10. |
| Sample | Two actual aggregate families use persisted decision state; Purchasing demonstrates an independent required summary and both modules retain explicit historical reads. |

## Review map

Read the domain state/evolution, detached preparation and required-view protocol together:

- Inventory [state](../../samples/Wholesale/modules/Inventory/Inventory/StockPositions/StockPositionState.cs),
  [commands](../../samples/Wholesale/modules/Inventory/Inventory/StockPositions/StockPositionCommands.cs),
  [former projector](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/es2-native-ef-simplification.md)
  and [queries Contract](../../samples/Wholesale/modules/Inventory/Inventory.Contracts/IStockPositionQueries.cs).
- Purchasing [state](https://github.com/oxface/modulith-foundry/blob/ae2909cd2e8f46a8b4dc0da71cc1c4a3ab6d52b4/samples/Wholesale/modules/Purchasing/Purchasing/PurchaseOrders/PurchaseOrderState.cs),
  [commands](https://github.com/oxface/modulith-foundry/blob/ae2909cd2e8f46a8b4dc0da71cc1c4a3ab6d52b4/samples/Wholesale/modules/Purchasing/Purchasing/PurchaseOrders/PurchaseOrderCommands.cs),
  [former projector](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/es2-native-ef-simplification.md),
  [former summary](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/es2-native-ef-simplification.md)
  and [queries Contract](https://github.com/oxface/modulith-foundry/blob/ae2909cd2e8f46a8b4dc0da71cc1c4a3ab6d52b4/samples/Wholesale/modules/Purchasing/Purchasing.Contracts/IPurchaseOrderQueries.cs).
- Native [Inventory mapping](../../samples/Wholesale/modules/Inventory/Inventory/StockPositions/InlineStateMapping.cs)
  and [migration](../../samples/Wholesale/modules/Inventory/Inventory/Migrations/20261006135521_AddStockPositionCurrent.cs),
  [Purchasing mapping](https://github.com/oxface/modulith-foundry/blob/ae2909cd2e8f46a8b4dc0da71cc1c4a3ab6d52b4/samples/Wholesale/modules/Purchasing/Purchasing/PurchaseOrders/InlineStateMapping.cs)
  and [migration](https://github.com/oxface/modulith-foundry/blob/ae2909cd2e8f46a8b4dc0da71cc1c4a3ab6d52b4/samples/Wholesale/modules/Purchasing/Purchasing/Migrations/20261006135521_AddPurchaseOrderViews.cs).
- [Focused proofs](../../samples/Wholesale/IntegrationTests/EventPersistence/AppendTests.InlineViews.cs),
  [extended append proofs](../../samples/Wholesale/IntegrationTests/EventPersistence/AppendTests.cs),
  [executable journeys](../../samples/Wholesale/IntegrationTests/Scenarios/EventPersistence/AppendJourneys.cs)
  and [shared disposable-database fixture](../../tests/Support/PostgreSqlFixture.cs).

## Remaining gaps

New migrations create empty view tables and preserve existing event tables and historical
migration sources. No automatic backfill is performed. Older fixture streams have history but
no inline views, so ordinary commands reject them until explicit maintenance establishes the
required views. Command-created streams establish their views through the actual protocol.

E6.2 must separately scope bounded persisted-view repair, writer exclusion, identity discovery
and creation admission. Neither module has a repair operation yet. Stream IDs remain the
command/query identity; no business-key get-or-create or uniqueness policy is added. The
independent StockCatalog availability view remains state-stored and is not automatically
maintained by Stock Position events.

Audit/event/view atomicity, cross-module transactions, messaging, asynchronous projections,
global committed progress, checkpoint snapshots, view revisions, previews and online rebuild
remain later capabilities. The PostgreSQL payload mapping and migrations prove this consumer,
not provider neutrality for every relational database.
