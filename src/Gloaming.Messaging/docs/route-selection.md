# Selecting outbox routes

A module outbox can contain messages for several independently composed publishers. Set
`OutboxDispatchOptions.RouteKeys` on each PostgreSQL dispatcher when one publisher handles only
part of that outbox:

```csharp
services.AddPostgresOutboxDispatcher<SalesOutboxDbContext, InventoryPublisher>(
    new(TimeSpan.FromSeconds(10), TimeSpan.FromMilliseconds(100))
    {
        RouteKeys = ["inventory.reserve-stock", "inventory.release-reservation"],
    });
```

The existing options type is in `Gloaming.Messaging.EntityFrameworkCore`; native claim SQL is
in `Gloaming.Messaging.EntityFrameworkCore.Postgres`. No transport or sample dependency is added.
`IReadOnlyCollection<string>? RouteKeys` defaults to null, preserving selection of all routes.
An empty collection, null entry or whitespace entry throws `ArgumentException` during constructor
or registration validation, before database I/O. Keys are exact and case-sensitive; whitespace
is not trimmed. A dispatcher copies the supplied collection once at construction. Configuration
changes require a fresh dispatcher; changing a mutable input cannot retarget a live dispatcher.

PostgreSQL claims apply a parameterized `destination COLLATE "C" = ANY(@routeKeys)` predicate
before `FOR UPDATE SKIP LOCKED`. The predicate is omitted for null. Excluded rows retain attempts,
revision, lease and completion state. Disjoint publishers can progress concurrently. Existing
native lease completion, failure accounting and uncertain transport acceptance semantics remain.
A message for an unconfigured route waits until an eligible publisher is deployed; consumer
composition owns that deployment policy and operational monitoring.

This is a route claim selector, not a router, migration mechanism or authorization boundary.
It makes no owner/tenant security claim. Database credentials, grants and RLS still determine
which rows and operations are authorized. Arbitrary publisher code is trusted in its process.

Wholesale proved the burden when real profile updates and Inventory requests shared Sales's
outbox. Its restricted broker publisher, local reservation publisher and Directory relay now
select their own routes. Provider PostgreSQL proofs cover unchanged excluded rows, concurrent
disjoint selection, quoted literal parameterization, copied mutable input and invalid options;
product proofs cover restricted native broker and Directory executable entrypoints.
