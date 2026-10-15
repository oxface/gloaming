# D2.3a: local durable delivery through module Contracts

Owner selected the next proof with “proceed” after the
[D2.3 recommendations](../reports/d2-3-module-consistency-and-coordination.md), 2026-10-15.
Implementation is prepared on feat/ddd-sample-patterns for review; this does not authorize commit.

Reuse the existing two-line Sales reservation workflow and Inventory rules. Add an independently
executable broker-free host and public module-owned intake Contracts. Module intake owns fresh
scope/transaction/commit and returns committed receipt; relay publishers use only Contracts.
Existing native inbox/outbox processors, workers and workflow runner retain their responsibilities.
Keep the RabbitMQ sample and both modules' independent database commits.

Public consumer interfaces are IStockReservationIntake and IStockReservationOutcomeIntake, with typed
V1 command/outcome overloads and ReservationDelivery metadata. Contracts contain no EF or Gloaming
types; Sales.Contracts references Inventory.Contracts for Inventory-owned outcome types. No arbitrary
module operation runs inside a workflow continuation, and no receiver context leaks through intake.

Prove acceptance versus completion, original delivery/business identity, duplicate and concurrent
intake, admission, receipt commit failure, competing reservations, refusal/compensation behavior and
actual process death at retained handoff cuts. Reuse the native database clock for lease recovery.
Temporary database barriers belong only to tests. Add solution/CI/architecture wiring and consumer
instructions with the proof. Existing schemas and migrations remain unchanged.

The [report](../reports/d2-3a-local-durable-module-delivery.md) records fresh results separately from
research and historical WF1/WF2 results. Consumer-owned composition is a valid outcome; extract no
generic local bus, scheduler, saga API, transaction wrapper or new Gloaming adapter without repeated
evidence. Shared native transactions, cancellation protocols, broader transport compatibility and
D2.2 deferred local reactions are outside this increment.
