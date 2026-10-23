# E5.2.2 expected-version append and caller-owned transactions

2026-10-06. The owner-authorized [append scope](../plans/e5-2-2-native-event-append.md)
was owner-reviewed and checkpointed as `abcd370`, together with E5.3, after E5.2.1
checkpoint `4f4d5b2`. The results below describe this increment. [E6.1](e6-1-inline-decision-state.md)
subsequently replaces ordinary command history replay with required inline state.

## Outcome and extraction finding

Inventory opens stock positions and stages positive receipt batches. Purchasing drafts
orders and stages line-change batches. Both deliberately decide typed events, reconstruct
the expected prefix, evolve proposed state and encode the complete batch before changing
tracked rows. The consumer starts the native module transaction, saves, commits or rolls
back, and disposes the operation scope. Command methods never independently save or publish.

**No new reusable mechanism or Foundry public interface was proven.** Native EF's original
version predicate, owned keys and caller-controlled transactions supply the write mechanism.
Existing Events.Serialization, Events.History and EF ownership utilities serve both writers
without library changes, new dependencies or migrations.

The two staging implementations have substantial repeated protocol structure. This is an
extraction candidate, but a shared implementation would currently need to parameterize
module rows, history loading, stream types, domain evolution and outcomes around ordinary EF
operations. Keep the exercised implementation editable and reassess it with E6's required
participants before proposing an interface. Domain decisions and business Contracts remain
consumer-owned regardless. No generic event store, aggregate base or transaction wrapper
was introduced merely to remove this repetition.

| Area | Finding |
| --- | --- |
| Library | Existing codec/range/ownership mechanisms compose with two native writers. No technical library source changed. |
| Template | Explicit native transaction, command registration/staging, narrow fault classification and fresh-scope recovery are proven editable recipes. Materialized output remains E10. |
| Sample | Both modules gain populated business command Contracts and deliberate event decisions. The executable performs real command/commit/read journeys independently of HTTP or messaging. |
| Deferred candidates | Shared staging protocol and required participant coordination need further evidence; no automatic event collection, retry, publishing or audit policy was extracted. |

## Command and persistence boundaries

Creation means expected version 0, never a persisted version. Existing-stream requests require
a positive expected version and nonempty valid batch. DTOs carry stream identity and business
values; the immutable established tenant supplies ownership. Actor/audit integration is not
silently attached. Command Contracts expose Staged, NotFound and Conflict business outcomes.
Staged carries proposed state/version only; success is durable only after the caller's commit.

Writers require established tenancy and an active native transaction. Existing owned headers
must have the expected stream type/version and a valid reconstructable prefix. UTC batch
recorded time cannot regress against that header; equal timestamps are accepted. Event
identities, contiguous positions, payloads and proposed state are prepared before tracking.

Attaching the observed header before changing its version preserves the original expected
version in native EF's UPDATE predicate. The caller's transaction commits header and envelopes
together. Competing creation is arbitrated by the owned header key; event positions also have
an owned unique index. The existing tenant write validator still runs explicitly in the
consumer's native save overrides.

Preflight stale versions return Conflict, but a competitor can win after preparation. Native
composition helpers recognize DbUpdateConcurrencyException only when all nonempty entries
are that module's stream headers. PostgreSQL unique failures are conflicts only for the exact
owning schema's header primary key or stream-position constraint. Event-ID collisions, check
failures, catalog concurrency and other storage faults retain their actual meaning.

The supported recipe is one staged batch per stream per operation context. Native tracked
headers detect repeated staging before and after save. Owner review rejected a separate
StagedStreamIds set in each DbContext: preserving the restriction after a deliberate tracker
clear added command-lifecycle bookkeeping without strengthening concurrency or atomicity.
Manual clearing/detaching mid-operation is outside the recipe. Different streams may
share one caller-owned module transaction and multiple explicit saves. After failure/rollback,
discard the context and proposed results. A retry requires a fresh scope, current state and
another explicit business decision. Native savepoints are not a tracker-recovery contract.

This slice persists event-sourcing facts deliberately produced by module decisions. It does
not add domain-event collection or integration-event mapping/delivery. The removed stream-ID
set was operation bookkeeping, not an event collection; those later capabilities do not need
that set or automatic DbContext event discovery.

## Executable usage and independent proofs

[AppendJourneys](../../samples/Wholesale/IntegrationTests/Scenarios/EventPersistence/AppendJourneys.cs) visibly opens/
drafts, saves and commits in separate module operations, then appends two facts per stream.
Fresh-context business reads report stock version 3/on-hand 13 and order version 3/total 62.50.
These command streams are separate from the earlier authored compatibility histories. The
executable now emits six lines: four fixture reads and two committed command results. The six
durable JSON literals remain unchanged; finite seed helpers remain setup, not the command API.
The ordinary sequential rerun skips completed demo streams and is not concurrent bootstrap.

The new **43-case** PostgreSQL append suite uses real contexts, module migrations and Contracts:

- Independent readers see no created stream or new batch during staging or after an uncommitted
  save; they see the whole proposal after commit. Initial batches produce head 3/stock 5 or
  order total 37.50. Both facts are persisted, including equal recorded timestamps.
- Two independent writers prepare at the same head before the winner saves. Exactly one full
  creation/batch commits; the loser gets a narrowly classified native conflict and leaves no
  extra envelopes. The winning append produces head 5/stock 20 or order total 100. SQL observation
  confirms the original expected version and tenant are present in the header update.
- Stale/future expected versions, missing/foreign identity, invalid scope/transaction/commands,
  non-UTC/regressing time and damaged history cannot prepare a valid append. Failed preparation
  leaves no tracked changes. A foreign-only identity is NotFound; another tenant may create
  its own stream with that GUID without changing the first tenant's history.
- Real header, first-envelope, later-envelope, event-ID and position faults roll back the complete
  batch. Known position collisions are conflicts; other injected faults are not. A catalog
  DbUpdateConcurrencyException also remains outside event conflict classification.
- An append saved first and a different stream whose second save fails both roll back in the
  same native transaction. Repeated staging is rejected before and after save through tracking. A
  pre-cancelled commit is rolled back, and a fresh context subsequently commits a valid append.

The competing-writer proof deterministically prepares both at the same head; it does not rely
on simultaneous network saves, lock timing or sleeps. Faults are privileged test setup, with
no production hooks. Cancellation is before commit dispatch, not an ambiguous in-flight commit.
No fake publisher/store or generic EF transaction matrix was added. Existing codec/range/read
proofs are reused rather than duplicated.

## Fresh verification

The initial complete implementation verification passed without failures or skips:

| Suite | Cases |
| --- | ---: |
| PostgreSQL event-history and append consumer | 82 (39 existing read cases, 43 new append cases) |
| Existing PostgreSQL/HTTP consumer | 97 |
| Architecture | 61 |
| Independently adoptable codec/history consumer | 16 |
| **Total** | **256** |

The event suite starts disposable PostgreSQL **18.6** Testcontainers, runs actual migrations
and launches the built executable to check all six lines. The full **37-project** solution
built with zero warnings/errors. Native style and analyzers passed; CSharpier checked **275
files**. Both native EF pending-model checks reported no changes. Archive integrity verified
all **800 original files** unchanged. Local Markdown checking resolved **669 links in 66
active documents**. Tracked and new files passed whitespace checks after documentation updates.

After owner review removed StagedStreamIds, the revised **82 PostgreSQL cases and 61 architecture
cases** passed again without failures/skips. The repeated-staging proof now checks native
tracking before and after save, followed by rollback and fresh-context recovery. The full
solution build, style, analyzers, 275-file formatter check, both pending-model checks, archive
integrity and local links/whitespace also passed again. The HTTP and independent codec results
in the table are from the preceding full-slice verification, not reruns after this revision.

These are fresh local results, not observed remote CI or checkpoint hook results. Prior
PersistenceDemo, Aspire/browser, other core-library and archived architecture executions
remain historical; those unaffected suites were not rerun here. Archived stores supplied
comparison evidence, not guarantees for this implementation.

## Review and remaining gaps

Start with [Inventory command Contracts](../../samples/Wholesale/modules/Inventory/Inventory.Contracts/IStockPositionCommands.cs)
and [Purchasing command Contracts](https://github.com/oxface/modulith-foundry/blob/ae2909cd2e8f46a8b4dc0da71cc1c4a3ab6d52b4/samples/Wholesale/modules/Purchasing/Purchasing.Contracts/IPurchaseOrderCommands.cs).
Review [stock decisions](../../samples/Wholesale/modules/Inventory/Inventory/StockPositions/StockPositionDecisions.cs)
and [purchase decisions](https://github.com/oxface/modulith-foundry/blob/ae2909cd2e8f46a8b4dc0da71cc1c4a3ab6d52b4/samples/Wholesale/modules/Purchasing/Purchasing/PurchaseOrders/PurchaseOrderDecisions.cs)
with their evolution helpers. Then compare [Inventory staging](../../samples/Wholesale/modules/Inventory/Inventory/StockPositions/StockPositionCommands.cs)
and [Purchasing staging](https://github.com/oxface/modulith-foundry/blob/ae2909cd2e8f46a8b4dc0da71cc1c4a3ab6d52b4/samples/Wholesale/modules/Purchasing/Purchasing/PurchaseOrders/PurchaseOrderCommands.cs),
their native AppendFailures helpers, registration and tracked-header checks.
[Caller composition](../../samples/Wholesale/IntegrationTests/Scenarios/EventPersistence/AppendJourneys.cs) and
[PostgreSQL proofs](../../samples/Wholesale/IntegrationTests/EventPersistence/AppendTests.cs) accompany them.

This completes the implemented initial E5 read/append scope, pending owner review.
[E5.3 model registration](e5-3-event-storage-registration.md) now extracts shared technical
mapping, independently of append coordination; its own report records new interface/adoption
proofs. This slice's append results above remain separate evidence. E6 must
prove required views/repair; audit and reliable messaging require later explicit participants.
Availability is still a separate state-stored catalog, not an event projection. No atomic
cross-module transaction, reservation lifecycle or complete purchase-order lifecycle is claimed.

The protocol protects an explicit tenant/stream identity, not stock-item/location or purchase-code
uniqueness across streams. Privileged SQL can bypass append-only assumptions. Full replay is
unbounded; snapshots, workload capacity and as-of index tuning need evidence. PostgreSQL is
the proven provider, not a provider-neutral guarantee. Ambiguous commit recovery, idempotent
workflow receipts, automatic retry and publication are not provided. Materialized template
output and bootstrap CLI remain E10.
