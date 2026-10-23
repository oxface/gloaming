# Inventory event persistence proofs

This directory owns test setup and finite child-process journeys for
[Wholesale integration tests](../../README.md). The public product uses the
[Worker](../../../Worker/README.md) and its native Inventory commands. The focused
[EventStorageDemo](../../../../../src/Gloaming.EventSourcing/samples/EventStorageDemo/README.md)
shows independent Gloaming adoption without Wholesale.

## Explicit composition and history

[DemoComposition](DemoComposition.cs) selects Inventory services, a scoped deterministic clock
and tenancy. [DemoJourneys](DemoJourneys.cs) migrates Inventory and imports the unchanged
[Inventory literals](../../../../../src/Gloaming.Events/samples/EventCodecDemo/Fixtures/README.md)
in separate admitted Organization scopes. The same stock-position GUID exists for Alpha and
Beta; Beta quantities are doubled by consumer-owned fixture policy.

[HistoryReadTests](../../EventPersistence/HistoryReadTests.cs) checks current/version/as-of
reads, actual SQL predicates, tenant isolation, captured-head consistency, malformed histories,
native schema constraints and forward migration from the existing availability catalog.
The `--scenario history` child executes fixture import, append, issue and repair through
live module Contracts; its expected six lines are asserted by the native process test.
Only Inventory fixture literals are copied into this test executable. Purchasing literals
remain with Events codec/history proofs and are not rewritten or deleted.

## Commands and inline state

[AppendJourneys](AppendJourneys.cs) opens a separate position and commits a two-receipt batch.
The caller explicitly begins the native transaction, stages commands, saves and commits.
`Changed` is a proposed state until commit; rejected commands do not track partial candidates.
Inventory owns quantity validation, stock eligibility and the recorded business facts.

[AppendTests](../../EventPersistence/AppendTests.cs) retains commit visibility, complete-winner
concurrency, tenant isolation, invalid staging, rollback across multiple saves and native error
classification. [Inline state proofs](../../EventPersistence/AppendTests.InlineViews.cs) cover
editing with event reads denied, damaged/missing state, a racing head/state read and invalid or
overflowing complete batches. [Required state proofs](../../EventPersistence/AppendTests.RequiredInlineState.cs)
check the model/save protocol. [Stock decisions](../../EventPersistence/AppendTests.StateDependent.cs)
check complete-batch issue acceptance/rejection and fresh-context redecision.

[The library contract](../../../../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/README.md)
owns event-store guarantees. Caller admission, transactions, retries and ambiguous-commit
reconciliation remain consumer responsibilities. The availability catalog is separate
state-stored data, not an event projection.

## Explicit single-stream maintenance

[RebuildJourney](RebuildJourney.cs) invokes Inventory rebuilding inside an explicit caller
transaction, then makes a fresh stock decision. [Rebuilding tests](../../EventPersistence/AppendTests.Rebuilding.cs)
retain missing/behind/corrupt state, rollback, competing append, foreign-tenant and replay-fault
proofs. The independent library [ledger tests](../../../../../src/Gloaming.EventSourcing/tests/EventSourcingPostgresTests/RebuildTests.cs)
cover JSONB replacement failure and fresh repair without Wholesale.

The real [stock-regeneration command](../../../Worker/Maintenance/stock-regeneration.md) owns
existing-target maintenance, progress, cancellation and restart. Its
[tests](../../EventPersistence/StockRegenerationTests.cs) retain actual process death,
conflict and indeterminate-commit outcomes. Rebuilding is not a general projection engine.

## Mixed historical and current schemas

[SchemaEvolutionJourney](SchemaEvolutionJourney.cs) and
[upcasting tests](../../EventPersistence/AppendTests.Upcasting.cs) combine retained v1 receipts
with current writes, live history, inline reads and repair without rewriting stored envelopes.
The `--scenario schema-evolution` child remains a bounded process proof. Alias/schema mappings
and receipt evolution belong to Inventory; Gloaming supplies explicit codec/upcaster composition.

## Transactional stock-issue notification

[Outbox proofs](../../EventPersistence/AppendTests.Outbox.cs) check that accepted event/state,
explicit audit and notification commit together, failures roll them back, and business rejection
emits nothing. [Outbox dispatch tests](../../EventPersistence/OutboxDispatchTests.cs) exercise
native claims and broker delivery. [Gloaming messaging](../../../../../src/Gloaming.Messaging/README.md)
records the protocol and its delivery limits.

## Durable stock-issue command intake

[Inbox tests](../../EventPersistence/InboxDispatchTests.cs) retain committed intake, duplicate
receipt, refusal replies, native failures and retry/recovery. Inventory explicitly admits its
producer and Organization before business access; inbox identity supplies deduplication, not
authorization. [Reservation workers](../../../Worker/README.md) compose independent Sales and
Inventory commits through public Contracts over local or broker transport.

## Scope

Inventory tests cover sample business decisions; independent library ledger/raw-storage tests
cover reusable persistence guarantees. Retained module migrations and literal payloads are owned
compatibility inputs. Full historical replay remains unbounded in stream length;
these proofs make no production capacity, snapshot or external-pooler guarantee.
