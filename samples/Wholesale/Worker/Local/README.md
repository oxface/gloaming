# Local durable reservation workflow

The Worker local-delivery mode runs the existing Sales-owned reservation workflow and Inventory decisions without
a broker. Sales and Inventory keep separate databases and transactions. Four existing Gloaming
workers relay each module's outbox and process each module's inbox; no new scheduler or library
delivery engine is added. The [broker mode](../Broker/compensating-reservations.md) runs another explicit transport topology
from the same Worker assembly.

## Handoff and ownership

The [command publisher](Delivery/ReservationPublishers.cs) calls Inventory's public
[IStockReservationIntake](../../modules/Inventory/Inventory.Contracts/IStockReservationIntake.cs).
The reply publisher calls Sales'
[IStockReservationOutcomeIntake](../../modules/Sales/Sales.Contracts/IStockReservationOutcomeIntake.cs).
Neither relay accesses a receiver DbContext or table. Composition and finite setup own infrastructure
wiring; Sales continues to own the saga, states and compensation rules.

The typed V1 Contracts carry delivery identity, Organization, order/cause IDs and diagnostic context.
Sales.Contracts now explicitly references Inventory.Contracts for its outcome types. Both Contracts
remain free of EF and Gloaming dependencies. These are trusted in-process integration entry points,
not externally authenticated APIs. The module binds producer identity and validates its existing
business admission rules. Transport wiring controls which components receive these capabilities.

Each intake opens a fresh native scope and transaction, calls the existing inbox and returns only
after commit. It does not execute the business operation. The receiver's inbox worker later commits
its decision, business idempotency receipt, completion and outgoing reply together. Delivery retries
retain message identity; business retries also preserve reservation identity and original inputs.
The public intake does not reuse the sender's initialized tenant/actor scope.

```mermaid
sequenceDiagram
    participant S as Sales transaction
    participant R as Existing outbox relay
    participant I as Inventory public intake
    participant W as Existing Inventory inbox worker
    S->>S: Save process and outgoing command; commit
    R->>I: Typed reservation delivery
    I->>I: Retain inbox row; commit
    I-->>R: Durable receipt
    R->>R: Mark outgoing delivery complete
    W->>W: Decide, retain result and reply; commit
    Note over S,W: Reply follows the same durable handoff to Sales
```

Successful intake means receipt, not a confirmed order. Read the Sales contract for final progress.
One reservation refusal may decline the order or request compensation. A compensation refusal remains
NeedsAttention and requires explicit operator policy. Repeated delivery does not guarantee eventual
business success, and a timeout does not prove a participant made no change.

## Run

Use two disposable PostgreSQL databases and configure both connection strings. No RabbitMQ setting
or broker is required. Setup applies existing module migrations; worker startup never migrates or seeds.

```sh
export ConnectionStrings__Sales='Host=localhost;Database=local_sales;Username=postgres;Password=local'
export ConnectionStrings__Inventory='Host=localhost;Database=local_inventory;Username=postgres;Password=local'
dotnet run --project samples/Wholesale/Worker -- --transport local --role setup
dotnet run --project samples/Wholesale/Worker -- --transport local --role seed --StockId 8c6e282e-d9ec-455a-8fab-d114c176a7cd
dotnet run --project samples/Wholesale/Worker -- --transport local --role seed --StockId 40e38f42-6f51-416b-bdec-d9e84a5d1575
dotnet run --project samples/Wholesale/Worker -- --transport local --role start --OrderId 38418f77-e5b9-4b28-bc83-e46e5a47d8e8 --FirstStockId 8c6e282e-d9ec-455a-8fab-d114c176a7cd --SecondStockId 40e38f42-6f51-416b-bdec-d9e84a5d1575 --SecondQuantity 6
dotnet run --project samples/Wholesale/Worker -- --transport local --role run
```

In another terminal with the same environment, use `--role read --OrderId ...`, or explicitly invoke
`--role retry-compensation --OrderId ... --OperationId ...` after a refused compensation.
Retain/reuse that operation ID for equal retries; a new operator decision needs a new ID.
Sales retains the original result through its [receipt adoption](../../modules/Sales/command-receipts.md),
so a repeated old retry cannot signal a later pause. The default Organization is
wholesale-alpha; wholesale-beta is also admitted. Seed creates five units at version 2. The example
requests two first-line units and six second-line units, reaching Compensated. Quantities of two on
both lines reach Reserved. Stock IDs must be new. LeaseSeconds controls the native publication lease
and defaults to 10; no forced-expiry or process-fault switches are supplied.

## Verification and limits

```sh
dotnet test --project samples/Wholesale/IntegrationTests/Wholesale.IntegrationTests.csproj
```

Tests use PostgreSQL only and launch the actual executable for process-death proofs. Disposable
database triggers hold intake before commit, command completion after Inventory's effect, and reply
completion after Sales intake. Killing and replacing the host demonstrates native lease recovery,
deduplicated receipt and one set of business effects. Further cases cover commit-time intake failure,
concurrent intake, semantic retries, stale/equal outcomes, competing processors, admission, refusal,
compensation and explicit setup. The compensation-refusal case deliberately simulates an admitted
Inventory business outcome; it does not claim to reproduce every cause of a real release refusal.

Routing and schema support are the fixed reservation V1 messages above; this is typed consumer
mapping, not a transparent general transport. Delivery and processing remain separate commits,
and in-process placement does not create cross-module atomicity. Existing inbox retention/retry,
outbox lease, workflow version and operator-signal limitations still apply. Poison work requires
consumer intervention policy. Parallel workflow branches, expiry, cancellation and shared native
transactions are outside this example.

See the [workflow contract](../../../../src/Gloaming.Messaging/docs/workflows.md) for transaction ownership and supported local delivery composition.
