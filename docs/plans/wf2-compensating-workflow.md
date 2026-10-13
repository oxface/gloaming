# WF2: compensating stock reservations

Status: consumer implementation authorized on 2026-10-10, on
`feat/compensating-workflow` from merged WF1 at `161a33e`. No Rootbolt interface change
was proposed or authorized by the original consumer brief. The owner subsequently requested
the [sequential coordinator assessment and concrete interface proposal](wf2-sequential-saga-coordinator.md).
The owner approved that follow-up interface/scope and requested implementation. The optional
coordinator and Sales adoption are implemented for code review; [fresh evidence](../reports/wf2-sequential-saga-coordinator.md)
is separate from the original consumer-only run. Existing staged review files remain preserved.

## Journey and ownership

Sales starts a two-line stock reservation process, with distinct stock positions. Inventory
reserves the first line in its own transaction; its reply advances Sales and queues the
second reservation. If the second is refused, Sales queues release of the first reservation.
Only the committed release outcome permits Sales to declare compensation complete. Successful
orders retain both reservations; shipping, consuming reservations and order cancellation are
outside this slice. This bounded order allocation replaces the tentative order-acceptance
example: the active sample has no order acceptance domain to exercise yet.

The public consumer journey is `IStockReservationOrders.StartAsync`, `ReadAsync`, and an
explicit `RetryCompensationAsync` for a refused release. Stock quantities and identities are
business Contracts. Inventory owns reserve/release messages and outcome codes. Sales owns
business status, correlation and reply validation. The optional coordinator owns requested
command identities, sequential progress and compensation attempts. There is no
shared transaction or distributed rollback. Each local transition, inbox completion and
next outgoing message commit together through the existing Rootbolt inbox processor.

Reservation identity is distinct from delivery identity. Inventory retains original inputs
and reserve outcome, including refusals. Equal retries return that original decision without
reserving again; changed inputs fail. Release targets reservation identity, uses current stock
state and remains idempotent after intervening writes. Duplicate reserve after release cannot
resurrect it. Tenant-scoped keys and native version predicates protect competing deliveries.

## Exact change map

| Area | Files / behavior |
| --- | --- |
| Inventory Contracts | New reservation command/outcome records and explicitly numbered result enums; existing history adds optional reserved quantity and calculated availability. |
| Inventory stock state | State, events, evolution, codec, aggregate and available queries/filters account for reservations without reducing physical on-hand stock. Historical payloads default reserved quantity to zero. |
| Inventory reservation operations | New owned reservation receipt/entity, EF mapping and inbox handler. Reserve/release join the processor transaction; retain semantic decisions and emit outcomes. Registration adds an explicit subscription. |
| Sales Contracts and process | New start/read/retry Contract, persisted two-line business inputs with optional SequentialSaga progress, EF mapping, operations and reply admission/handler. Existing WF1 remains executable. |
| Persistence | Additive owned Inventory/Sales migrations and current snapshots; previous migrations and archive remain unchanged. |
| WorkflowDemo | Extend existing composition, native RabbitMQ routes/intake and hosted subscriptions; finite reservation start/read/retry commands. Startup remains separate from setup. |
| WorkflowDemo.Tests | Real PostgreSQL/RabbitMQ success and compensation, semantic retries, conflicts, failures, races and child-process death/recovery. Reuse existing fixtures and process harness. |
| Documentation | Current roadmap/status, executable instructions, local Messaging capability context and dated WF2 report. |

## Proof and extraction gate

Prove reserve/release state, event append, outcome and inbox completion are atomic; failure
of Sales transition/next command leaves neither committed. Prove stock availability excludes
held reservations, native filters work, and compensation preserves unrelated receipts/issues.
Use real PostgreSQL for competing writers and rollback, and actual host death/restart at a
compensation boundary. Late/duplicate replies must not produce another step or release.

Compare WF1/WF2 after implementing the consumer. Existing native EF concurrency, inbox
transactions, outbox publication and worker scopes are already mechanisms. Promote a new
mechanism only if concrete duplication is meaningfully reduced without absorbing business
transitions, compensation or admission. Any proposed library interface requires owner review
before implementation. Scheduling, poison recovery, generic saga DSLs, transport abstractions,
multi-line parallel orchestration and template presets remain outside this bounded slice.
