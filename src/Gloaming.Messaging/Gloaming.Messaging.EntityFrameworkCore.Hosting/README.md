# Optional EF messaging worker hosting

`Gloaming.Messaging.EntityFrameworkCore.Hosting` provides explicitly selected sequential
workers around the callable roles in
[Gloaming.Messaging.EntityFrameworkCore](../Gloaming.Messaging.EntityFrameworkCore/README.md).
It depends on that package and native EF Core, DI, Hosting and Logging abstractions. It
has no provider, transport, workflow, application-module or schema-discovery dependency.
Enqueue-only applications and callers supplying their own execution loop do not need it.

## Select a worker

Reference this package in the host and import its namespace:

```csharp
using Gloaming.Messaging.EntityFrameworkCore.Hosting;

// Register the owning context, dispatcher/processor, handler and native logging first.
services.AddOutboxWorker<OrdersDbContext>(
    new OutboxWorkerOptions(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1)));
services.AddInboxWorker<OrdersDbContext>(
    "orders.replies",
    new InboxWorkerOptions(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1)));
```

Either worker can be selected independently. The host owns its `IHostedService` lifecycle
and `BackgroundServiceExceptionBehavior`; use native host supervision for fatal faults.
Registration supplies no context, provider, dispatcher, processor, handler, logger, transport,
transaction, migration or trusted tenant/actor context. Missing required services fail
startup. Context establishment must satisfy the selected operation's own ordering rules;
in particular, a tenant-aware processor must establish context before beginning RLS work.

## Execution and failure contract

Each worker validates its bindings in a discarded startup scope, then invokes its callable
operation sequentially in a fresh async scope per attempt. Inbox startup also validates the
selected keyed handler. The scope is disposed before any polling delay. This adds no global
concurrency limit, ordering guarantee or workflow scheduler; multiple selected workers and
processes remain independent.

`IdleDelay` applies after `NoWork`; `FailureDelay` applies after ordinary operation failures.
Both must be positive and fit native `Task.Delay` bounds. Successful work immediately permits
the next attempt. Dependency/model/provider failures during resolution escape the loop.
Storage, durable retry eligibility, leases, transactions and failure classification remain
with the selected callable operation; the polling delay is not its durable retry schedule.

Unconfirmed outbox failure accounting or completion faults the outbox worker without another
poll. Unconfirmed inbox failure accounting similarly faults the inbox worker. The worker
logs its supervision diagnostic and lets the native host apply the selected fault policy.
Requested shutdown ends the worker normally. Ordinary failures log and retry in a new scope;
they never establish that an external publisher rejected the message. See
[outbox recovery](../docs/outbox-recovery.md) and
[inbox recovery](../docs/inbox-recovery.md) for the callable guarantees.

Cooperative cancellation reaches the operation and prevents subsequent attempts. A
non-cooperative publisher or handler can still hold shutdown; this package imposes no
deadline on arbitrary consumer code. Transport intake and delivery acknowledgments remain
consumer host responsibilities. See [process recovery](../docs/worker-recovery.md).

## Adoption and proofs

The six worker/options/registration types moved from the EF package into this package and
the `Gloaming.Messaging.EntityFrameworkCore.Hosting` namespace. Existing consumers add the
package reference and namespace import. Native logger categories follow the moved worker
namespace; adjust category-specific host filters if needed. Polling and supervision behavior
is retained. Database mappings, fixtures, schemas and migrations are unchanged.

Executable hosts include [the independent worker example](../samples/Delivery/README.md)
and [Wholesale](../../../samples/Wholesale/Worker/README.md). Existing provider and process
tests exercise startup validation, fresh scopes, cancellation and fatal supervision. The
package-only [hosting consumer](../../../tools/release/consumers/Hosting.cs) adopts both
workers with supplied callable roles and no database provider. The separate PostgreSQL
package consumer verifies its persistence/producer graph restores no Hosting abstractions.
The [slice report](../../../docs/reports/messaging-worker-hosting.md) records fresh results
and remaining sample consolidation work.
