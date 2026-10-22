# D2.3a: broker-free durable delivery through module Contracts

Date: 2026-10-15 (Europe/Kiev). Owner selected this executable increment with “proceed” after
the [D2.3 research](d2-3-module-consistency-and-coordination.md). Prepared on
feat/ddd-sample-patterns after merge checkpoint `5f90abe`, under the
[bounded scope](../plans/d2-3a-local-durable-module-delivery.md). Implementation remains subject
to owner review; no staging, checkpoint commit or publication is performed for this increment.

## Outcome

The [local workflow executable](../../samples/Wholesale/Worker/Local/README.md) delivers the
existing Sales reservation workflow through public Inventory and Sales intake Contracts without
a broker. Modules retain separate databases and independent native transactions. The existing
RabbitMQ workflow, business rules, migration identities and Gloaming implementations remain intact.

Successful intake means durable receipt, not completed business work. Each module validates its
existing admission rules, creates a fresh scope, receives through its typed inbox and commits before
returning. The existing outbox dispatcher records delivery completion only after that successful
return. A crash between those commits leaves retained work and causes an equivalent redelivery.
Inbox processing still commits participant decisions, semantic business receipts, outgoing replies
and incoming completion atomically. Sales' existing workflow owns progress and compensation.

This proves consumer-owned composition of existing reusable mechanisms. **No new reusable library
mechanism was proven or extracted.** There is no new bus, scheduler, saga engine, interceptor,
cross-module unit of work or arbitrary-call durability wrapper. The new sample demonstrates a
specific reliable handoff rather than making any in-process method call durable.

## Review-worthy interfaces and integration

- Inventory's [IStockReservationIntake](../../samples/Wholesale/modules/Inventory/Inventory.Contracts/IStockReservationIntake.cs)
  accepts typed ReserveStockV1/ReleaseStockReservationV1 and ReservationDelivery metadata.
- Sales' [IStockReservationOutcomeIntake](../../samples/Wholesale/modules/Sales/Sales.Contracts/IStockReservationOutcomeIntake.cs)
  accepts the existing typed Inventory outcomes and the same delivery metadata. Sales.Contracts now
  explicitly references Inventory.Contracts; neither Contracts assembly depends on EF or Gloaming.
- ReservationDelivery carries the stable delivery ID, Organization, order/cause identity and optional
  diagnostic context. The public methods return Task: successful completion documents the committed
  receipt boundary; the caller already owns the delivery identity. Final business progress remains
  available through the existing Sales read Contract.
- Inventory's [intake implementation](../../samples/Wholesale/modules/Inventory/Inventory/Reservations/StockReservationIntake.cs)
  and Sales' [outcome intake](../../samples/Wholesale/modules/Sales/Sales/ReservationOrders/StockReservationOutcomeIntake.cs)
  resolve fresh contexts through native IServiceScopeFactory. Their singleton registration retains
  no context, actor or tenant accessor. A sender's initialized operation is not reused.
- The [relay publishers](../../samples/Wholesale/Worker/Local/Delivery/ReservationPublishers.cs)
  depend only on public Contracts and the existing publisher/envelope contract. They map explicit V1
  routes and payloads to the public methods. Producer identity is fixed by each receiving entry point;
  no caller-controlled producer fallback or private-context service crosses the Contract.
- [Composition](../../samples/Wholesale/Worker/Local/LocalWorkflowComposition.cs) registers four existing
  native inbox/outbox workers, module databases and module registration extensions. Finite
  [setup](../../samples/Wholesale/Worker/Local/LocalWorkflowSetup.cs) applies existing migrations;
  ordinary startup requires setup and never creates or seeds business storage.

Trusted host wiring assigns access to these integration entry points. They are not an authentication
boundary for arbitrary callers in the process. Existing producer/Organization/payload/causation
admission is reused. No new external endpoint or authorization policy is implied.

## Fresh proofs

[Delivery tests](../../samples/Wholesale/IntegrationTests/Reservations/Local/DeliveryTests.cs) use PostgreSQL only:

- Both-line success and second-line refusal followed by acknowledged compensation retain existing
  results. Physical OnHand is unchanged; successful reservations reduce availability.
- A public call made from an already initialized foreign sender scope persists the correctly admitted
  receiver envelope in an independent scope. Inventory stock and Sales progress remain unchanged
  until their respective inbox processors run. Receipt and final outcome are distinct.
- Six concurrent equivalent intakes retain one incoming row. A new delivery ID for the same business
  reservation returns its original decision without reserving again. An equal historical reply with
  a new reply delivery ID preserves its original command causation and does not repeat a Sales turn.
  Conflicting content under the same delivery identity is rejected.
- A deferred PostgreSQL constraint trigger raises only at intake commit. The public method fails,
  no receiver row survives, the producer retains outgoing work and a fresh attempt delivers it.
- Invalid Organization, order correlation and payload fail before receipt.
- Competing Inventory processors handle two placements against the same expected stock version.
  One order completes and one receives a business refusal; committed stock does not oversell.
- An intentionally simulated admitted compensation refusal leaves NeedsAttention and retained
  reserved stock. It does not pretend successful intake or repeated retries guarantee release.

[Process proofs](../../samples/Wholesale/IntegrationTests/Reservations/Local/ProcessRecoveryTests.cs) launch and
kill the actual executable, with no RabbitMQ fixture or connection. Temporary triggers in disposable
databases hold native operations at three cuts:

| Cut | Evidence before death | Recovery and final observation |
| --- | --- | --- |
| Receiver inbox insert before commit | Another connection sees no receipt or stock reservation. | Process death rolls back intake. Original sender work survives; replacement delivers and processes it. |
| Inventory effect committed, Sales delivery acknowledgment pending | Inventory has reserved stock while the Sales outgoing completion transaction is blocked. | Replacement uses the real lease clock and redelivers the same command; receipt/business effects remain singular. |
| Sales reply intake committed, Inventory reply acknowledgment pending | Inventory result and outgoing reply exist; Sales has durably received it. | Replacement redelivers the retained reply without repeating workflow progress. |

Every case ends with two reservation receipts, two command inbox rows, two reply inbox rows, final
Sales Version 3, each stock Version 3 and two reserved units per stock. Outboxes complete and an
interrupted outgoing delivery records at least two attempts. Tests wait for real eligibility;
they do not rewrite leases, identities or production state to manufacture recovery.

An additional executable test proves missing setup fails startup, startup creates no Sales tables,
finite setup applies migrations without seeding reservations, and a configured worker host starts.

[Architecture checks](../../tests/ArchitectureTests/SampleModuleBoundaryTests.cs) now include the local
composition, forbid relay dependencies on module implementations or EF, retain Contracts' prohibition
on technical library/context dependencies, and verify the host has no RabbitMQ assembly reference.
CI runs the new suite in the Messaging lane with its dependent path/fixture mappings.

## Verification

Fresh results for this increment:

| Check | Result |
| --- | --- |
| Local workflow PostgreSQL, intake and actual process proofs | 14 passed, zero failures/skips |
| Existing RabbitMQ workflow consumer regression | 62 passed, zero failures/skips |
| Architecture boundaries | 90 passed, zero failures/skips |
| **Total tests** | **166 passed** |
| Full solution build | Zero warnings/errors |
| Style/analyzer verification | Passed |
| CSharpier including generated source | 739 files checked |
| Archive preservation | All 800 original files verified |
| Changed Markdown links, whitespace and index preservation | Passed |

Logs are `/tmp/d2-3a-build.log`, `/tmp/d2-3a-local-tests.log`,
`/tmp/d2-3a-workflow-tests.log`, `/tmp/d2-3a-architecture-tests.log`, `/tmp/d2-3a-style.log`,
`/tmp/d2-3a-analyzers.log` and `/tmp/d2-3a-csharpier.log`. These fresh results are separate from
the D2.3 source research and historical WF1/WF2 runs. An early incomplete test run was stopped
while correcting its barrier cleanup and command/reply identity assumptions; only the completed
passing run above is verification evidence.

The index manifest remains
`67234b3094ff6017dcb4548f7136857967685939c4f5eb0234d0d1de62d36b9c`.
All research and implementation changes remain unstaged. The conflict-resolution merge was
already committed separately as `5f90abe`; no new commit or push is performed here.

## Findings and remaining limits

Library: existing durable intake, leased dispatch, locked processing and continuation persistence
compose without a broker. No runtime library files changed. The small repeated intake transaction
setup is still consumer integration in one sample, without independent evidence for a general helper.
Template: no new preset, source snapshot or creation behavior. Sample: fixed V1 routes, typed mapping,
producer trust, Organization admission, statuses, semantic identity and compensation remain policy.
The host can use two physical databases; none of the evidence depends on a shared transaction.

Changing a command delivery ID is distinct from resending a reply delivery. A workflow reply must
still reference an operation the workflow requested; semantic reservation idempotency does not
authorize an unrequested command or transition. The public intake may durably retain a structurally
valid outcome that later fails workflow-state validation. Accepted receipt is not promised completion.

This is a typed reservation handoff, not byte-preserving generic wire forwarding or a general local
transport. Existing inbox retention/retry and outbox lease requirements still apply. Permanent poison
work, operational redrive and retention policy are not solved by the new host. Cancellation before
an ambiguous commit cannot prove rollback; stable delivery identity and fresh recovery remain required.
Only one pending workflow request is supported. No cancellation/expiry race, parallel join, durable
operator-signal receipt or arbitrary direct-call recovery is added. Shared native transactions remain
a separately selected proof requiring an actual atomic-acceptance invariant. D2.2 is still deferred.
