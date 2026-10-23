# E5.2.1 native EF history reads

2026-10-06. The owner reviewed and approved the complete 60-file
[read/mapping increment](../plans/e5-2-1-native-event-history.md), checkpointed as `4f4d5b2`
after E5.1 checkpoint `4cc12a1`. The working tree was clean immediately after committing.

## Outcome and extraction finding

Inventory and Purchasing now use Events.Serialization and Events.History through actual
module-owned PostgreSQL readers. They capture a tenant-scoped stream head, select and order
rows through native EF, validate the returned range, check header timestamps, decode and evolve
business state explicitly. Their public Contracts carry business results without EF/JSON or
technical identity/context types.

**No new reusable mechanism or Foundry public interface was added.** Existing range validation
now removes repeated integrity traversal in two real database readers, while JSON dispatch
retains its independent codec boundary. This supports retaining the small separate History
library for owner review; it does not freeze future package divisions. Row mappings, query
selection, stream/header policy, tenant relationships and domain evolution stay consumer-owned.
The repeated reader shape is a future comparison point, not a reason to add a generic store now.

| Area | Finding |
| --- | --- |
| Library | Existing metadata validator and JSON codec compose with actual EF materialized ranges; no library source changed. |
| Template | Native contexts, mappings, migration histories, explicit registration, tenant establishment and read recipes are editable consumer setup. Materialized output remains E10. |
| Sample | Inventory gains stock-position reconstruction; Purchasing gains populated implementation/Contracts projects and purchase-order reconstruction. A native executable reads both in two tenant scopes. |
| Rejected scope expansion | No generic aggregate repository, query DSL, mandatory event base, schema discovery, automatic save/retry or messaging worker was needed. |

## Stored model and read guarantees

Each module owns an event-stream table and durable envelope table in its schema. Headers have
owner/identity/type, positive version and creation/update timestamps. Envelopes have owned
event/stream identities, positive position/schema version, durable name, recorded time and
native jsonb payload. No global sequence, dispatch columns or generic metadata bag was added.

A composite owner/stream primary key permits colliding stream GUIDs in different tenants.
The owned stream-position unique index and composite foreign key enforce same-tenant references
and position uniqueness. The existing ownership utility supplies named filters and explicitly
called save validation. Neither mapping nor schema selection is populated by Foundry.

Current reads capture a materialized untracked header before event queries. Version selectors
reject zero/future versions instead of clamping them. Intermediate time selectors find the
maximum qualifying version within that head, then fetch its complete prefix. Equal times
preserve stream order. A cutoff before creation returns no state; a cutoff at/after update
must read the captured head so a missing tail cannot masquerade as an earlier result.

Selected rows must form the exact prefix. The first event must match header creation time;
when the prefix reaches the captured head its last event must match update time. Header time
order and positive head are checked separately. Integrity outside the selected range remains
uncertified. Range, header, codec, domain and native database/cancellation errors retain their
separate boundaries; reads neither save nor trigger external work.

The positive version constraint, duplicate-position rejection and tenant reference constraint
are tested through the actual migrations. Forward Inventory migration preserves preexisting
catalog rows, and Purchasing can migrate before or after Inventory without disturbing them.
Native EF scaffolding was used; migration bodies were normalized to file-scoped namespaces,
static column-name arrays and repository formatting without changing scaffolded operations.
Both snapshots match their runtime models.

## Consumer and independent expectations

The executable registers contexts/providers and tenancy explicitly, migrates, stages finite
histories and saves each module through native EF. It calls business Contracts for reconstruction.
Six existing durable fixture literals are reused unchanged; timestamps and Beta's doubled
quantities are authored demo policy. [The guide](../../samples/Wholesale/IntegrationTests/Scenarios/EventPersistence/README.md)
records commands, expected output and setup limitations.

| Read | Inventory | Purchasing |
| --- | ---: | ---: |
| Alpha current, head 3 | On-hand 13, delivery `DELIVERY-2` | One replacement line, quantity 5, total 62.50 |
| Alpha version 2 | On-hand 10.125 | Quantity 2.5, total 31.25 |
| Alpha first-change cutoff | Version 2, on-hand 10.125 | Version 3, both equal-time facts, total 62.50 |
| Within lifetime before first change | Version 1, zero on-hand | Version 1, no lines, zero total |
| Before creation | No state | No state |
| Beta current, same stream GUID | On-hand 26 | Quantity 10, total 125 |

Tests also assert independently known item/location identities, base unit, order code,
supplier/currency, latest reference and result timestamps. Replay repeats without accumulated
state or tracked rows. There is no command, publisher or pending-event implementation to
simulate with an effect counter.

A native EF interceptor observes executed tenant/stream/version/time predicates and ordering.
It coordinates a controlled atomic fixture append after header capture and before the event
query, without production hooks or timing sleeps. Both readers return the captured version 3;
a fresh read sees version 4, with stock 18 or order total 87.50. This proves the read boundary
under an append-only atomic-write assumption, not a production expected-version writer.

Privileged fault setup damages selected ranges, payloads or header timestamps. Ordinary reads
fail at their intended boundary; valid earlier reads and the other tenant remain readable.
Missing creation within the stream lifetime fails instead of returning a before-first result.
Cancellation and unavailable event tables propagate their native failures; a fresh read works
after restoration.

The existing HTTP application still uses Inventory's separate state-stored catalog and applies
the new forward migration. It registers no history query. Inventory's owning module now has
explicit event-library references even for a host selecting only catalog registration; no
catalog-only assembly independence is claimed. Sales/Access remain state-stored. The isolated
EventCodecDemo remains a minimal library adoption/compatibility consumer, not the authoritative
business module implementation.

## Fresh verification

The final affected suites passed with no failures or skips:

| Suite | Cases |
| --- | ---: |
| New PostgreSQL event-history consumer | 39 |
| Existing PostgreSQL/HTTP consumer | 97 |
| Architecture | 61 |
| Independent codec/history consumer | 16 |
| **Total** | **213** |

The new suite launches the built native executable against a disposable PostgreSQL 18.6
Testcontainer and checks all four output lines. PostgreSQL proofs use actual migrations.
Architecture extends existing ArchUnitNET rules to Purchasing/Contracts and the new consumer;
no restored-project parser or custom graph mechanism was added. CI's existing persistence
lane includes the new suite; commit hooks remain container-free.

The complete **37-project** active solution built with zero warnings/errors. Native style and
analyzers passed for the full solution and then for the final amended test assertions. Both
native EF pending-model checks reported no changes. CSharpier checked **265 files**, and archive
integrity verified all **800 original files** unchanged. Local Markdown checking resolved
**640 links in 64 active documents**; tracked and all 40 new files passed whitespace checks.

The first event-history execution found missing catalog registration in two migration-test
setups; corrected setup passed. The initial HTTP execution found one remaining single-migration
assertion; the final full 97-case run passed after replacing it with the module's complete
applied set. Two HTTP migration assertions were updated in total. No failing result is presented
as verification of the final change set.

The checkpoint freshly passed all required hooks: **237 active cases** across ten
container-free suites and **21 archived architecture cases**, with no failures or skips.
Active/archive native style and analyzers, the 265-file formatter check and commitlint passed.
The independent core/HTTP library suites were rerun by those hooks. PostgreSQL suites retain
the implementation results above; they were not rerun during checkpointing.

These are fresh local executions, not observed remote CI results. Older PersistenceDemo suites
and Aspire/browser results remain historical. Archived readers supplied comparison evidence,
not current storage guarantees. No provider-neutral or production deployment claim follows
from this slice.

## Review and remaining gaps

Review the business Contracts first:
[Inventory queries](../../samples/Wholesale/modules/Inventory/Inventory.Contracts/IStockPositionHistory.cs),
[stock result](../../samples/Wholesale/modules/Inventory/Inventory.Contracts/StockPositionHistory.cs),
[Purchasing queries](https://github.com/oxface/modulith-foundry/blob/ae2909cd2e8f46a8b4dc0da71cc1c4a3ab6d52b4/samples/Wholesale/modules/Purchasing/Purchasing.Contracts/IPurchaseOrderHistory.cs)
and [order result](https://github.com/oxface/modulith-foundry/blob/ae2909cd2e8f46a8b4dc0da71cc1c4a3ab6d52b4/samples/Wholesale/modules/Purchasing/Purchasing.Contracts/PurchaseOrderHistory.cs).

Then review [Inventory reader](../../samples/Wholesale/modules/Inventory/Inventory/StockPositions/StockPositionHistoryReader.cs)
and [Purchasing reader](https://github.com/oxface/modulith-foundry/blob/ae2909cd2e8f46a8b4dc0da71cc1c4a3ab6d52b4/samples/Wholesale/modules/Purchasing/Purchasing/PurchaseOrders/PurchaseOrderHistoryReader.cs)
with their native mappings and consumer evolution. Review
[the executable recipe](../../samples/Wholesale/IntegrationTests/Scenarios/EventPersistence/DemoJourneys.cs),
[PostgreSQL proofs](../../samples/Wholesale/IntegrationTests/EventPersistence/HistoryReadTests.cs)
and [native command observation](../../samples/Wholesale/IntegrationTests/EventPersistence/QueryObservation.cs).
Native migrations/snapshots, explicit registrations and finite seed helpers accompany those files.

Read consistency assumes atomically committed append-only histories; arbitrary privileged
updates/deletes are not a snapshot guarantee. Header/selected-range checks do not certify
excluded rows. Replay remains unbounded in stream length; index/performance tuning and snapshots
need workload evidence. The new native model is PostgreSQL-specific and alternatives are unproven.

[E5.2.2](e5-2-2-native-event-append.md) now implements expected-version staging and caller-owned
native save/commit, with new competing-writer, write-fault, rollback and fresh-context proofs.
Those results are separate from this checkpoint's read evidence above. Finite fixture setup
does not provide that protocol. Required views and repair remain E6, reliable messaging
E7, and materialized template/bootstrap E10. Rich command aggregates, reservations and complete
purchase lifecycle are not established by read-only evolution.
