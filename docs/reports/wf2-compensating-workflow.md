# WF2: compensating stock reservation workflow

This report preserves the initial consumer-only implementation and 474-case run. The current
owner-approved coordinator replacement is recorded separately in the
[follow-up report](wf2-sequential-saga-coordinator.md); those new library proofs supersede the
initial extraction finding without treating this earlier run as proof of the new mechanism.

Date: 2026-10-10. Consumer implementation authorized and complete for owner code review on
`feat/compensating-workflow`, based on fetched `origin/main` at `161a33e` (merged WF1).
At the implementation handoff, changes were unstaged and uncommitted and the empty index
was preserved. The owner has since staged review files; follow-up edits preserve that snapshot.
No push or PR was created. WF1 status records are corrected to its checkpoint and actual merge.

## Outcome

Sales now runs a durable two-line reservation process. Inventory reserves the first line
and replies; Sales persists that outcome and requests the second. Refusal of the second
persists compensation progress and queues release of the first. Only the acknowledged
committed release permits Compensated. Success retains both reservations. This is a bounded
allocation process; it does not introduce full order acceptance, shipping or payments.

Inventory owns reservation inputs, original decisions and release idempotency. Physical
on-hand stock is preserved; reserved quantity reduces availability for both decisions and
native EF filters. Compensation loads current stock version, preserving unrelated receipts
and issues. The receipt and reserved state/event changes commit with outgoing outcome and
inbox completion in the existing native Inventory transaction. Reserve retries under new
delivery IDs return the original decision, including after release; changed inputs fail.

Sales owns process transitions and semantic outcome matching. Process state, child release
attempts, next command and inbox completion commit in its own transaction. A refused release
becomes NeedsAttention with CompensationRefusal; explicit operator retry creates a new
delivery targeting the same reservation. Recorded attempts prevent an old refusal from
overwriting subsequent success. Native root-row versions arbitrate different deliveries.

The existing WorkflowDemo now routes both WF1 and WF2, still using separate module databases,
real RabbitMQ and the provided inbox/outbox workers. Finite setup installs additive migrations
and queues; startup checks them. Finite reservation start/read/retry commands are executable.
No shared DbContext, cross-module transaction or new transport/runtime dependency was added.

## Extraction assessment

**No new reusable mechanism was proven or extracted.** WF2 supplies the materially different
multi-step and compensating comparison missing from WF1. The comparison identifies these
obligations without creating speculative abstractions:

| Obligation | Exercised mechanism / ownership |
| --- | --- |
| Local transition, next command and completion commit together | Existing Gloaming inbox processor and typed outbox. |
| Retry, worker death and transport redelivery | Existing retained inbox, outbox lease/fencing and fresh-scope workers; consumer-owned transport acknowledgements. |
| Different messages update one process or stock aggregate | Native EF process versions and existing event-store version/position constraints. |
| Repeated business operation across different delivery IDs | Inventory-owned stable reservation identity, retained inputs and original decision. This is independent of inbox retention/deduplication. |
| Recognize the correct step and handle late outcomes | Consumer correlation/cause matching, named business decisions and per-attempt outcomes. |
| Determine and execute compensation | Consumer state machine selects release; Inventory implements its business effect. |
| Refused compensation and intervention | Consumer NeedsAttention, visible refusal and explicit retry; no generic operator policy. |

The initial assessment rejected a generic state/store wrapper around native EF. The owner's
follow-up identified a narrower candidate: ordered progress, expected-command/outcome matching
and compensation-attempt bookkeeping. [The concrete sequential-coordinator proposal](../plans/wf2-sequential-saga-coordinator.md)
now assesses that extraction while leaving business input validation and typed commands local.
Its interface and exact replacement scope await owner review; no new mechanism is implemented
or proven by the results below. Existing Gloaming mechanisms already concentrate transaction,
dispatch and scope behavior. Scheduling and poison handling remain separate future candidates.

## Review-worthy code

- [Business Contract](../../samples/Wholesale/modules/Sales/Sales.Contracts/IStockReservationOrders.cs)
  exposes start/read/explicit compensation retry without persistence/technical context types.
- Sales state machine (historical source, replaced by WF2 continuations),
  [operations/envelopes](../../samples/Wholesale/modules/Sales/Sales/ReservationOrders/StockReservationOrders.cs),
  [reply handler](../../samples/Wholesale/modules/Sales/Sales/ReservationOrders/StockReservationReplyHandler.cs),
  mapping and admission: business progress, explicit next actions and retained compensation attempts.
- [Inventory wire Contracts](../../samples/Wholesale/modules/Inventory/Inventory.Contracts/StockReservationMessages.cs),
  [business receipt](../../samples/Wholesale/modules/Inventory/Inventory/Reservations/StockReservation.cs),
  [handler](../../samples/Wholesale/modules/Inventory/Inventory/Reservations/StockReservationHandler.cs),
  mapping/admission: original decisions, whole-transaction reservation and idempotent release.
- [Stock aggregate](../../samples/Wholesale/modules/Inventory/Inventory/StockPositions/StockPositionAggregate.cs),
  evolution/codec/state and queries/filters: reserved quantity, availability and historical JSON compatibility.
- Owned Inventory/Sales contexts, registrations, additive migrations and current model snapshots:
  no previous migration was rewritten. Reservation receipts are owned business data, not new
  required projection participants or an extracted rebuilding capability.
- [Native executable](../../samples/Wholesale/WorkflowDemo/Program.cs), routing/composition/pollers and
  [usage](../../samples/Wholesale/WorkflowDemo/compensating-reservations.md): existing workers and
  explicit command/reply transport with intake commit before acknowledgement.
- [Behavior proofs](../../samples/Wholesale/WorkflowDemo.Tests/ReservationTests.cs),
  [transaction faults](../../samples/Wholesale/WorkflowDemo.Tests/ReservationFailureTests.cs),
  [races/process recovery](../../samples/Wholesale/WorkflowDemo.Tests/ReservationRecoveryTests.cs),
  fixture and extended database barrier. No new durable script or test project was needed.

The [scope map](../plans/wf2-compensating-workflow.md) names all changed behavior. Existing CI
paths and the Messaging test invocation already cover the extended project; no CI change is
needed. Module implementations retain Contracts-only peer references. No library runtime
code/interface or template preset changed; T1 remains independently event/messaging-free.

## Fresh verification

Local SDK 10.0.401 with PostgreSQL 18.6 and RabbitMQ 4.3.6 through the Podman socket.
MSBuild used two nodes; process/HTTP tests used DOTNET_USE_POLLING_FILE_WATCHER=1 to avoid
the previously identified machine-wide inotify limit. These are verification conditions,
not new runtime promises.

| Suite | Passed |
| --- | ---: |
| WorkflowDemo (33 new WF2 cases, 29 WF1 regression cases) | 62 |
| Existing event-persistence consumers | 190 |
| Existing architecture boundaries | 81 |
| Existing Wholesale HTTP/profile/audit consumers | 105 |
| Existing module persistence consumers | 36 |
| Total distinct cases across these suites | 474 |

WF2 proves successful reservations; first refusal; all three second-line refusals and real
acknowledged compensation; stable start identity; semantic reserve/release retries across
delivery IDs; conflicting/duplicate/late replies; tenant and operation matching; preserved
intervening receipts/issues; native availability filtering; and old inline JSON lacking the
new optional field. Live history equals current reserved/released state.

Eight Inventory fault cases independently reject receipt, event, outgoing outcome or inbox
completion for reserve and release. Two Sales fault cases reject the next outgoing action.
These prove rollback of the complete local step and fresh-scope recovery, including queued
compensation. Deterministic PostgreSQL barriers observe waiting writers: competing semantic
reserve/release commands mutate stock once; distinct orders cannot over-reserve; concurrent
second-line refusals queue one compensating command.

A real child host is killed after Inventory commits release but before Sales commits its
acknowledgement. Restart recovers retained reply without another reservation release/event.
Another child-process proof exercises finite reserve-order/read-order commands with the worker.
The explicit operator-refusal/retry proof uses a simulated release refusal to isolate Sales
policy; it is not evidence of a real Inventory refusal in that broker journey. Real business
refusals in the other paths are distinguished from simulation in the tests.

The initial expanded run exposed overly narrow test exception expectations: native inbox
completion raises PostgresException; competing event inserts can hit the position uniqueness
constraint before EF's versioned stream update. Assertions now inspect the supported native
check/uniqueness failures, and verify rollback and fresh retry. No library protocol was changed
to satisfy the tests. The final complete 62-case run passed; all 29 WF1 cases passed too.

Active solution build succeeds with zero warnings/errors. Archive verification retains all
800 original files from `15d1ec6`; archived source/fixtures and unrelated worktrees are unchanged.
Native semantic style and analyzer verification pass. CSharpier checks all 599 active files,
including generated migration metadata. `git diff --check` passes and the index remains empty.
Hosted CI has not run for this uncommitted branch.

## Limits and historical evidence

This consumer supports two distinct lines sequentially. Successful reservations remain held;
consumption/shipping, cancellation, expiry/timeouts, parallel lines and wider fulfilment are
deferred. Unknown release is a refusal, not a durable cancellation tombstone. Original reserve
refusals are final under their identity; retrying a new decision requires a new reservation.

No operator authorization UI, compensation scheduler, reconciliation query, attempt cap,
quarantine/redrive, retention policy or generic saga DSL is supplied. Database failures leave
Releasing/pending inbox work and retry logs; a business refusal records NeedsAttention. The
sample only establishes admitted finite Organization keys and a non-anonymous actor. Producer
authentication, queue permissions, malformed-delivery/DLQ policy and deployment remain consumer-owned.
Privileged SQL can violate consistency; the protocol cannot undo manual corruption or fence
writers bypassing the event-store/transaction contract.

WF1/O1/I1/W1/OBS1 reports retain their historical library/telemetry guarantees. This run supplies
new compensating-workflow evidence, not a fresh OTLP-export, throughput, exactly-once or alternate
provider guarantee. The archive remains reference material and was not tested or promoted.
B1's retained-source feed remains the next recommended runtime slice after owner review;
ES3 maintenance orchestration and B2 new-service bootstrap remain distinct pending work.
