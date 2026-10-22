# O1: Transactional outbox and callable dispatch

Status: owner authorized implementation of the revised interface/scope on 2026-10-08;
implementation is undergoing line-by-line review.
Base: `0740cd3`, on `codex/transactional-outbox`. The existing event, template and CI
checkpoints remain intact. Authorization to implement does not authorize a commit.

## Implementation review follow-up

The owner authorized implementation after reviewing XML documentation, typed construction,
clearer names and a compiled weak-registry exploration. The current interface uses RouteKey
and optional TenantKey; the ordinary EF handler is ExportRequestCommands. Dispatch outcomes
retain NoWork, Published and ClaimLost with documented acceptance/completion meanings.
Equivalent XML coverage in older Gloaming families remains follow-up work outside this review.

Typed construction is one static generic factory returning the existing non-generic envelope:

```csharp
public static OutgoingMessage FromPayload<TPayload>(
    Guid messageId,
    string routeKey,
    string messageName,
    int schemaVersion,
    TPayload payload,
    JsonSerializerOptions serializerOptions,
    string? tenantKey = null
);
```

The caller supplies explicit JSON policy. Native options can include converters/type metadata;
there is no ambient default, second registry or generic argument on EF/publishers/workers.
Already encoded Events.Serialization payloads continue using the JSON constructor. The HTTP
adopter exercises typed construction and preserves its existing wire JSON naming policy.
A separate JsonTypeInfo overload is not necessary for this bounded change.

### Internal enqueue registry

One internal ConditionalWeakTable keys evidence by the actual OutboxMessageRecord object.
Its value holds the original envelope, exact native transaction and DbContextId. Find accepts
only the original context instance/pool lease. MessageId is not a global registry key.
The table stores no routing, tenant admission, dispatcher lifecycle or business state.

The concrete production change map is:

- Add internal OutboxEnqueueRegistry with Register/Find. No DI service or extra package.
- OutboxMessageRecord.Create copies only durable fields. Remove EnqueuedMessage,
  EnqueueTransaction and the internal factory's transaction parameter.
- EfOutbox.Enqueue creates the row, registers its original evidence and tracks it.
- ValidateOutboxChanges obtains evidence from the registry, preserves envelope/exact-transaction
  checks and also requires the original context lease. Consumer save overrides stay explicit.
- Rename CLR mapping/property references in the new migration target models and current
  snapshots. Physical destination/owner_key columns and migration operations remain unchanged.
  This is a pre-checkpoint CLR rename; it adds no new database migration.
- Rename the ordinary EF application handler and its source/references to ExportRequestCommands.

Weak row keys avoid accumulating detached records in a long-lived context dictionary.
Evidence persists while the row is referenced, including after failed saves. Validation does
not remove it: subsequent SQL may fail and the same transaction may retry. Garbage collection
handles eventual cleanup; immediate cleanup on save/detach/rollback/disposal is not promised.
There are no save-success callbacks, pool reset hooks or ChangeTracker.Clear subscriptions.
ContextId includes its pool lease without retaining a separate strong context reference.
The original transaction is retained for the row's lifetime, as in the previous design.

The extra cost is one registration object and an indirect lookup. This is a small internal
refactor supporting the existing save contract, not a memory/performance optimization or
new messaging capability. New native PostgreSQL proofs exercise same-transaction retry after
SQL failure, detach/reattach, cross-context rejection and a reused pool lease. The earlier
32-line prototype was compilation-only evidence; current runtime results belong in the report.

Constructor validation still inspects configured EF metadata without opening a connection.
Transaction/pending-change checks remain at dispatch invocation. Transport routing belongs to
the publisher; a database handoff must commit receiver intake before returning acceptance.

References: [.NET weak-key semantics](https://learn.microsoft.com/en-us/dotnet/api/system.runtime.compilerservices.conditionalweaktable-2?view=net-10.0),
[EF context and pool-lease identity](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.dbcontext.contextid?view=efcore-10.0).

## Capability and concrete evidence

Persist explicitly mapped outgoing work in the same native EF transaction as its owning
business change. A separately invoked dispatcher claims committed work, calls a consumer
publisher outside the claim transaction, and records completion or retry delay only while
its lease remains valid. Delivery can repeat; this does not promise exactly-once effects.

The archive supplies three genuinely repeated implementations:

- [Inventory relay](../../archive/proof-sample/modules/Inventory/Inventory/Messaging/InventoryOutboxRelay.cs)
  publishes typed outcome/reference topics.
- [Sales relay](../../archive/proof-sample/modules/Sales/Sales/Messaging/SalesOutboxRelay.cs)
  sends directed reservation, release and replenishment commands.
- [Purchasing relay](../../archive/proof-sample/modules/Purchasing/Purchasing/Messaging/PurchasingOutboxRelay.cs)
  publishes created/rejected replenishment outcomes.

Each repeats the same row lifecycle, JSONB storage, PostgreSQL candidate claim, lease token,
attempt increment and guarded completion/backoff. Their row factories and publisher switches
encode business contracts, correlation and routing; those differences remain consumer-owned.
[ReserveStockHandler](../../archive/proof-sample/modules/Inventory/Inventory/Reservations/ReserveStockHandler.cs)
commits its state/history, operation, inbox, audit and outbox together. Its application protocol
is historical evidence, not authorization to extract all those participants in O1.
See [the archived comparison](../../archive/proof-sample/docs/plans/messaging-reuse.md).

Deleting the proposed library would restore claim/lease/fencing/recovery SQL and technical
envelope/transaction validation in every adopter. Enqueue alone would remove much less
complexity; the recoverable callable dispatch is what earns this extraction.

## Package and existing-library overlap

Owner follow-up, 2026-10-08: name both EF Core and PostgreSQL explicitly, group inbox/outbox
in the Messaging family and keep actual transport configuration with the consumer.

Propose three packages under `src/Gloaming.Messaging/`, plus family-local README/docs/tests:

- `Gloaming.Messaging`: the immutable validated `OutgoingMessage` and callable
  `IMessagePublisher` interface, using BCL JSON only. A consumer's publisher can reference
  this contract without EF or Npgsql. This is a used interface package, not an empty core
  or a transport runtime, and it has no dependency on other Gloaming families.
- `Gloaming.Messaging.EntityFrameworkCore`: provided record, relational model configuration,
  enqueue/save validation, typed-context producer/dispatcher contracts and a simple optional
  hosted worker. References the Messaging contract and native EF/DI/hosting/logging packages.
- `Gloaming.Messaging.EntityFrameworkCore.Postgres`: PostgreSQL model specialization (JSONB,
  database-time defaults), provider-specific registration and callable claim/complete/defer
  implementation. References the EF layer and the existing Npgsql EF provider.

The middle package contains used functionality, not merely a marker. It promises no working
second provider. PostgreSQL keeps its own SQL, lease protocol and clock behavior; no public
generic SQL dialect, lease repository or provider factory is proposed. Another provider can
implement the existing dispatcher interface and supply its native model configuration when
actual adoption justifies it. Any new provider still needs its own transaction/recovery proofs.

Inbox is a separate capability in this family, not part of O1. The current recommendation
is to add its proven EF/PostgreSQL mechanism to the same corresponding packages in the next slice,
with independent model/DI opt-ins. Outbox-only use will not require inbox tables/handlers;
inbox-only use will not require a publisher/outbox/worker. Both can compose in a consumer
transaction. Split storage packages later if an actual dependency or deployment difference
justifies it, rather than creating separate empty Inbox/Outbox projects now.

No Rebus/RabbitMQ or other transport implementation, bus configuration, subscriptions or
endpoint creation is planned for the library family. Consumer-owned native publishers/receivers
adapt into the reviewed interfaces. More convenience abstractions must remove demonstrated
duplication; no generic bus, message router or handler registry is introduced.

Explicit EF-package dependencies: native `Microsoft.EntityFrameworkCore.Relational`,
`Microsoft.Extensions.DependencyInjection.Abstractions`,
`Microsoft.Extensions.Hosting.Abstractions` and `Microsoft.Extensions.Logging.Abstractions`.
The PostgreSQL package adds `Npgsql.EntityFrameworkCore.PostgreSQL`, using existing central pins.
Use native EF SQL identifier quoting/parameterization. No new runtime dependency
on Events, EventSourcing, Persistence, Tenancy, ActorIdentity, Rebus, Aspire or module Contracts.
No provider-neutral dispatch guarantee is proposed now. Native provider utilities remain
available to the PostgreSQL implementation without leaking claim objects into consumer code.

Consumers can reuse `Gloaming.Events.Serialization` for exact durable name/schema registration,
native JSON encoding and old queued-message decoding/upcasting. They explicitly copy its
encoded identity/payload into the outgoing transport envelope. No second registry or upcaster
is added, and callers using direct JSON remain valid. An integration contract is not automatically
the aggregate's domain fact. RouteKey and delivery identity are not event-stream metadata.
The dispatcher passes retained message identity/content to the publisher; it does not upcast
or rewrite queued payloads automatically. A publisher choosing a wire-schema upgrade must
explicitly map that wire identity and compatibility policy. O1 reuses encoding, without
claiming a new queued-message schema rollout proof.

Keep storage separate: an append-only domain fact is retained history, while an outgoing
record carries mutable delivery lifecycle. No dispatch columns, publisher hooks or automatic
outbox behavior are added to the existing event store. Caller-owned transactions compose
the two when a business command deliberately needs both.

Consumers can apply `Gloaming.Persistence.EntityFrameworkCore` ownership/filter validation to
the provided row's optional opaque `TenantKey`. O1 does not duplicate that mechanism, infer
tenancy, grant admission or establish actor attribution. The standalone adopter has no owner
or context-library dependency. Module isolation comes from typed DbContext registrations,
not a shared bare `DbContext` or global publisher binding.

### Module ownership, inbox direction and tenancy

The repository's central composition is a modular monolith. Each adopting module registers
its own context, schema/table, publisher and optional worker. A receiving module owns a
different inbox and native business transaction; a host does not register one universal
outbox/inbox context. Stable integration contracts cross module boundaries, not EF entities
or sender domain-event types. An in-process durable adapter can eventually commit the
receiver's inbox before reporting publication success; a broker adapter can replace that
transport when a module moves to a service. These alternatives do not create a shared
cross-module transaction or atomically acknowledge both sides.

The next inbox proposal must distinguish two operations:

1. Receipt-only deduplication: handle a delivery and commit its receipt with business effects,
   then acknowledge the broker. There is no separately queued handler worker in this model.
2. Durable intake: retain the complete incoming envelope and deduplication identity in the
   receiver's database, commit, then acknowledge. A separately callable processor and optional
   worker later decode/dispatch a registered consumer handler and commit business effects,
   processing completion and any new outbox messages together. This is the owner's preferred
   queued-processing direction, not an already-supported capability.

The archived ReserveStockHandler above demonstrates receipt-only processing: it commits a
processed receipt together with the reservation decision and outgoing outcome. Its receipt
does not retain a complete incoming payload or queue a second worker. That is useful historical
evidence for deduplication, not evidence for the proposed durable-intake/processing protocol.
Do not build both modes automatically; assess the queued model first with a concrete receiving
module and keep receipt-only mode deferred unless an adopter actually needs it.

For durable intake, acknowledgement after database failure is forbidden; acknowledgement
failure after a successful intake commit can redeliver. The receiver must recognize the
already-committed envelope without scheduling duplicate work. Lease expiry/competing handler
completion, unknown schemas, poison messages, retention and deduplication scope need explicit
proofs. A processor must not mark completion in a different transaction from its local effects.
External effects still require an outbox or separate idempotency contract. No proposed handler
registry or inbox public types are smuggled into O1.

A RabbitMQ inbox sample must demonstrate native manual acknowledgement after committed
intake, rollback without acknowledgement, commit-before-ack failure/redelivery, and independent
processing recovery. A receipt-only example acknowledges after committed business effects
instead. Publisher confirms and consumer acknowledgements are independent; neither confirms
that the receiver's eventual business workflow completed.
See [RabbitMQ acknowledgements and confirms](https://www.rabbitmq.com/docs/confirms) and
[its reliability guidance](https://www.rabbitmq.com/docs/reliability).

Keep `Gloaming.Persistence.EntityFrameworkCore` as the existing reusable ownership utility;
do not move messaging under it or duplicate ownership checks in this family. Messaging-specific
EF code belongs here. No existing package relocation/rename is part of O1. Likewise, reuse
the Events.Serialization codec optionally at consumer mapping/handler boundaries, without
renaming it or adding delivery lifecycle to Events.History.

Tenantless and tenant-scoped messages are both valid. Tenant routing/admission is consumer
policy; `TenantKey` is optional transport metadata and local row ownership, not proof of
authority. Scoped producers populate it from the established operation and apply the existing
ownership utilities. Dispatch preserves it without binding the whole table to one tenant.
Future intake validates trusted producer/tenant metadata against the receiving contract and
creates a fresh admitted tenant operation for each handler. Merely carrying a tenant in JSON
is insufficient authorization. No mandatory Tenancy, ActorIdentity or EventSourcing dependency
is introduced; the Inventory adopter exercises their explicit composition.

## Consumer journeys

Inventory explicitly maps an accepted stock issue into `StockIssueRecordedV1`. Its existing
command remains state-dependent; rejection must produce neither an append nor an outbox row.
After event append, the owning module enqueues the integration message. The caller retains
the existing transaction/save/commit journey:

```csharp
await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
var result = await stockCommands.IssueAsync(request, cancellationToken);
// The owning command enqueues only on success; it never publishes.
await database.SaveChangesAsync(cancellationToken);
await transaction.CommitAsync(cancellationToken);
```

Inside that owning command, once the current aggregate and append result are available:

```csharp
OutgoingMessage outgoing = messages.StockIssueRecorded(aggregate, requested, appendResult);
outbox.Enqueue(outgoing);
```

`messages` is consumer mapping code, using the existing codec. It supplies a stable delivery
ID, logical destination, message identity and admitted owner. Delivery ID is generated once
for the accepted outgoing row and retained through every retry; semantic command idempotency
and ambiguous producer commits remain consumer responsibilities.

A new independent `src/Gloaming.Messaging/samples/OutboxDemo` uses ordinary EF state: submit a draft export request
and enqueue a directed `RenderExportV1` command in one transaction. It uses direct JSON, no
event-sourced aggregate, no tenant/access model and no template/module layout. A rejected
submission or competing stale edit emits no outgoing work. This varies transaction
participants, state model, ownership and serialization, rather than merely renaming facts.

Inventory uses a consumer-owned RabbitMQ publisher and native receiving fixture; the
independent adopter uses HTTP. This exercises genuinely different acceptance obligations:
the RabbitMQ adapter waits for publisher confirmation and detects unroutable publication
using native mandatory/return behavior; the HTTP adapter waits for its documented successful
response. Consumer source owns topology, persistence settings and bounded transport operations.
Neither result implies a completed downstream workflow. O1's RabbitMQ receiving fixture
uses manual acknowledgement only to observe transport delivery; it is not a durable inbox
or proof of receiver business effects. The next inbox slice supplies the full commit/ack
sample above. See [RabbitMQ data safety](https://www.rabbitmq.com/docs/confirms).

Dispatch is another operation with a fresh scoped context:

```csharp
await using var scope = services.CreateAsyncScope();
var dispatcher = scope.ServiceProvider
    .GetRequiredService<IOutboxDispatcher<InventoryDbContext>>();
OutboxDispatchResult result = await dispatcher.DispatchNextAsync(cancellationToken);
```

No ambient producer transaction may be active during dispatch. The consumer invokes this
operation explicitly, handles failures and decides when to call it again. Alternatively, an
opt-in library worker performs that same invocation in fresh scopes. It adds no startup
migration, producer-side publication or hidden business save.

The simple worker is sequential: resolve one dispatcher in a new scope, dispatch one row,
dispose the scope, and repeat. It delays when no eligible work exists and logs/delays after
publication/storage failures. Startup validates configured dependencies; host cancellation
stops new work and propagates to the current dispatch. Model/provider setup errors must not
be silently retried as transient publication errors. No batch processing, lease renewal,
leader election, scheduled inbox processing or transport connection lifecycle is added.
Callable dispatch remains usable without registering any worker; no module scans or global
publisher/context binding are used.

## Reviewed public types

`OutgoingMessage` and `IMessagePublisher` live in `Gloaming.Messaging`; the provider-independent
EF types live in `Gloaming.Messaging.EntityFrameworkCore`, with PostgreSQL implementations
and registration in `.Postgres`. The context generic selects
the owning database and DI binding; it does not restrict payloads to one CLR message type.

```csharp
public sealed class OutgoingMessage
{
    public OutgoingMessage(Guid messageId, string routeKey, string messageName,
        int schemaVersion, JsonElement payload, string? tenantKey = null);

    public static OutgoingMessage FromPayload<TPayload>(Guid messageId, string routeKey,
        string messageName, int schemaVersion, TPayload payload,
        JsonSerializerOptions serializerOptions, string? tenantKey = null);

    public Guid MessageId { get; }
    public string RouteKey { get; }
    public string MessageName { get; }
    public int SchemaVersion { get; }
    public JsonElement Payload { get; }
    public string? TenantKey { get; }
}

public interface IMessagePublisher
{
    Task PublishAsync(OutgoingMessage message, CancellationToken cancellationToken);
}
```

The EF Core package provides:

```csharp
public interface IOutbox<TDbContext> where TDbContext : DbContext
{
    void Enqueue(OutgoingMessage message);
}

public interface IOutboxDispatcher<TDbContext> where TDbContext : DbContext
{
    Task<OutboxDispatchResult> DispatchNextAsync(
        CancellationToken cancellationToken = default);
}

public enum OutboxDispatchResult
{
    NoWork = 1,
    Published = 2,
    ClaimLost = 3,
}

public sealed record OutboxDispatchOptions(TimeSpan LeaseDuration, TimeSpan RetryDelay);

public sealed record OutboxWorkerOptions(TimeSpan IdleDelay, TimeSpan FailureDelay);
```

The envelope constructor validates identity/route/schema/optional tenant key and owns
one JSON clone, so it remains usable after the source document is disposed. Get-only
properties prevent record `with` mutation from bypassing that contract. Its constructor
body is abbreviated above. Typed construction uses explicit consumer JSON options and returns
the same non-generic envelope; already encoded payloads use the constructor directly.

`Published` means publisher success and a successful guarded database completion.
`ClaimLost` can follow external success when lease expiry/takeover prevents completion;
another delivery remains possible. `NoWork` means no eligible row was claimed at that moment,
not a globally empty backlog. Publisher failures are deferred while the lease is valid and
then rethrown to the caller, rather than silently logged or converted into business failure.

The provided EF entity has a private materialization constructor and public getters/private
setters: `MessageId`, `RouteKey`, `MessageName`, `SchemaVersion`, `Payload`, `TenantKey`,
`QueuedAt`, `AvailableAt`, `DispatchedAt`, `LeaseToken`, `LeaseUntil`, and `Attempts`.
`MessageId`/lease token are GUIDs, attempts is `long`, payload is `JsonElement`, timestamps
are UTC `DateTimeOffset`; lease/dispatch timestamps and tenant key are nullable. It is named
`OutboxMessageRecord`. Applications do not implement record adapters or mutate its lifecycle.

Public concrete implementations for direct composition:

```csharp
public sealed class EfOutbox<TDbContext>(TDbContext database)
    : IOutbox<TDbContext> where TDbContext : DbContext;

public sealed class PostgresOutboxDispatcher<TDbContext>(
    TDbContext database, IMessagePublisher publisher, OutboxDispatchOptions options)
    : IOutboxDispatcher<TDbContext> where TDbContext : DbContext;
```

These declarations abbreviate bodies, not extra public operations. Claims and lease tokens
remain implementation details; no Prepare/Stage handle or public claim-management protocol.

The public helpers are:

```csharp
public static EntityTypeBuilder<OutboxMessageRecord> ConfigurePostgresOutbox(
    this ModelBuilder model, string schema, string table);
public static EntityTypeBuilder<OutboxMessageRecord> ConfigureOutbox(
    this ModelBuilder model, string schema, string table);
public static void ValidateOutboxChanges(this DbContext database);

public static IServiceCollection AddPostgresOutbox<TDbContext>(
    this IServiceCollection services) where TDbContext : DbContext;
public static IServiceCollection AddPostgresOutboxDispatcher<TDbContext, TPublisher>(
    this IServiceCollection services, OutboxDispatchOptions options)
    where TDbContext : DbContext
    where TPublisher : class, IMessagePublisher;

public static IServiceCollection AddOutboxWorker<TDbContext>(
    this IServiceCollection services, OutboxWorkerOptions options)
    where TDbContext : DbContext;
```

Common model/save helpers belong to EF `OutboxModelExtensions`. Its `ConfigureOutbox` maps
the provided shape using native EF; the PostgreSQL wrapper specializes payload/defaults/
provider annotations and is the supported setup entry for PostgreSQL consumers. Common
helpers alone do not promise supported dispatch on an arbitrary relational provider.
Provider model/DI helpers belong to `PostgresOutboxModelExtensions` and
`PostgresOutboxServiceCollectionExtensions`; the worker helper belongs to EF
`OutboxWorkerServiceCollectionExtensions`. Producer/dispatcher registrations are scoped.
The worker uses a private hosted implementation and fresh scoped dispatcher resolution.
The dispatcher helper resolves
the specified publisher for that typed context, rather than registering one global
`IMessagePublisher` and letting the last module win. Producer registration does not require
dispatch/publisher registration, and dispatch registration does not activate a hosted worker.
Dispatch/worker options are likewise associated with the selected typed context registration,
not overwritten through one shared global options instance. One provided outbox table per
owning context is the supported model; multiple module contexts can use distinct schemas/tables.

## Native model/save setup

```csharp
var row = model.ConfigurePostgresOutbox("inventory", "outbox_messages");
row.HasTenantOwnership(item => item.TenantKey!, () => RequiredOrganizationKey,
    "OrganizationScope");

// In both existing native SaveChanges overrides:
this.ValidateTenantChanges(() => RequiredOrganizationKey);
this.ValidateEventStreamChanges();
this.ValidateOutboxChanges();

services.AddPostgresOutbox<InventoryDbContext>();
services.AddPostgresOutboxDispatcher<InventoryDbContext, InventoryRabbitMqPublisher>(
    new OutboxDispatchOptions(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5)));

// Optional host registration; the module does not enable a worker implicitly.
services.AddOutboxWorker<InventoryDbContext>(
    new OutboxWorkerOptions(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(5)));
```

Schema/table are explicit consumer choices. First-version columns and the provided record
shape are fixed, with JSONB payload, positive schema version, nonnegative attempts, a globally
unique message ID within this outbox table and an eligible-work index. Schema migrations
remain native consumer source. Ordinary owner-filtered EF reads stay available.

Enqueue checks the validated envelope, model and transaction before adding one tracked
row with its already owned JSON. It requires an explicit transaction on that exact context.
The save guard checks inserted records against their enqueue transaction and rejects unsupported tracked mutation
of durable envelopes/lifecycle. Native ownership validation separately checks the owner.
Both save overrides are required; this does not fence raw SQL/bulk writes or bypassed guards.
The guard does not infer which business changes require a notification or validate payload
meaning. Multiple enqueues are separate calls inside the same caller-owned transaction;
there is no separate batch abstraction or partial-batch retry contract.

## Transaction isolation and coordination

Atomicity and business isolation are separate obligations. Outbox enqueue participates in
the transaction already belonging to the typed DbContext; it does not open, replace or raise
the producer's isolation level. ReadCommitted is the sample baseline. Business eligibility
still needs the existing version/concurrency predicates and constraints; enqueue alone does
not make a read/decide/write sequence safe against competing business changes.
[PostgreSQL's isolation contract](https://www.postgresql.org/docs/18/transaction-iso.html)
describes the different visibility and serialization-failure behavior. A producer may choose
RepeatableRead or Serializable through native EF, with whole-operation retry owned by the
consumer. O1 will add ordinary-EF atomicity/rollback proofs at those levels, without claiming
that every event-sourcing/business query has newly been certified at every isolation level.
The library does not retry or reinterpret serialization failures as business rejection.

For an event-sourced consumer, compose event append, its required main state and outbox in
one context/transaction, followed by the caller's explicit save and commit. Existing
`ValidateTenantChanges`, `ValidateEventStreamChanges` and proposed `ValidateOutboxChanges`
run in the same native save overrides, each enforcing its own declared obligation. No
package starts another producer transaction or dispatches during save/commit. Prove a fault
in every included participant through that concrete composition.

No common `Gloaming` root package, transaction coordinator, ambient unit of work or global
context is proposed. Native DbContext/IDbContextTransaction are already the shared transaction
interface. A repeated reference-equality check is not enough reason to introduce a compulsory
cross-family runtime. `Gloaming.Messaging` holds messaging-specific contracts only, and other
families stay independently adoptable. A future shared helper needs concrete complexity and
proof obligations beyond forwarding native EF calls.

Multiple DbContexts sharing a server/connection string do not automatically share a transaction.
O1 claims only a single owning context/transaction. Native shared-connection enlistment is
possible, but [EF requires the shared connection and transaction](https://learn.microsoft.com/en-us/ef/core/saving/transactions#cross-context-transaction).
Cross-module transaction setup, nested ownership and coordinated save/commit remain a separate
reviewed capability rather than a side effect of common package names.

## Dispatch mechanics, ownership and limits

1. Derive the selected table from supported native model metadata. Reject a non-Npgsql
   provider and missing/incompatible configuration before publication. Values are parameters;
   native provider quoting handles schema/table identifiers.
2. Atomically select/update at most one eligible row using `FOR UPDATE SKIP LOCKED`, a fresh
   lease token and database-clock expiry. Increment retained attempts. The short native claim
   ReadCommitted transaction commits before invoking the publisher. It never includes business writes.
3. Publish the retained identity/destination/payload through the consumer publisher. Hold no
   database row lock or transaction across network work. Lease duration is explicit, with no
   automatic renewal or global concurrency cap.
4. On success, mark dispatched only if token, undispatched status and unexpired lease still
   match. On publisher failure, schedule the fixed configured retry delay and release only
   that same valid claim, then propagate the exception. Stale claimants cannot rewrite work
   owned by a successor. Completion SQL failures propagate without treating acceptance as undone.

Completion and defer also use short dispatcher-owned ReadCommitted transactions. Dispatch
rejects an existing native/enlisted/ambient transaction rather than joining a business unit
of work. Changing the dispatch transaction isolation is not an option in O1.

`QueuedAt`, initial availability, lease expiry and retry eligibility use PostgreSQL's clock,
not the dispatching replica's wall clock. These are technical times, not event recorded time
or global commit ordering. Candidate ordering is best effort; there is no stream/tenant FIFO.
[PostgreSQL documents SKIP LOCKED as suitable for queue consumers, with an inconsistent view](https://www.postgresql.org/docs/18/sql-select.html#SQL-FOR-UPDATE-SHARE).
[clock_timestamp is current database time rather than transaction-start time](https://www.postgresql.org/docs/18/functions-datetime.html#FUNCTIONS-DATETIME-CURRENT).

The producer owns context, operation/tenant establishment, business changes, mapping, save,
commit/rollback and disposal. The dispatcher owns only its short claim/completion/defer SQL;
the host owns its fresh operation scope, publication adapter, invocation and retry scheduling.
The dispatcher deliberately drains its configured module table across owners using explicit
technical SQL; EF query filters do not provide that privileged operation's authorization.
Consumers restrict who may invoke it. A publisher needing tenant-scoped business work creates
a separate admitted operation rather than rebinding the dispatch context.

Cancellation before claim publishes nothing. Cancellation after claim leaves the lease for
expiry and propagates; acceptance may already have occurred. No unbounded or non-cooperative
publisher is advertised as cancellable merely because the interface accepts a token. Transport
shutdown and stalled-confirmation bounds are tested for the sample's selected native adapters;
they are not guarantees for every implementation of IMessagePublisher. Process death or loss
of the completion write can repeat delivery with the same identity. Receivers own idempotency.

Errors: argument errors for empty IDs/names/destinations, invalid schema/JSON/owner or durations;
`InvalidOperationException` for provider/model/transaction misuse; native EF/Npgsql errors for
uniqueness/storage/concurrency failures; original publication errors after guarded defer;
`OperationCanceledException` for cancellation. If publication failure and defer storage both
fail, preserve both failures with `AggregateException`. Do not reinterpret any of these as a
business rejection or silently reset/retry the producer context. Recovery uses a fresh scope.

## Exact implementation file/behavior scope for review

| Files | Proposed change |
| --- | --- |
| `src/Gloaming.Messaging/Gloaming.Messaging/{Gloaming.Messaging.csproj,OutgoingMessage.cs,IMessagePublisher.cs,README.md}` | Add used messaging contracts with immutable payload ownership and no EF/provider/other-family dependencies. |
| `src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore/{Gloaming.Messaging.EntityFrameworkCore.csproj,IOutbox.cs,IOutboxDispatcher.cs,OutboxDispatchOptions.cs,OutboxWorkerOptions.cs}` | Add used EF contracts and native dependencies; dispatch result enum lives with its interface. No Npgsql or other-family dependency. |
| Same project's `{OutboxMessageRecord.cs,OutboxModelExtensions.cs,EfOutbox.cs,OutboxWorker.cs,OutboxWorkerServiceCollectionExtensions.cs}` | Provided relational shape/enqueue/save guard and an internal optional sequential hosted worker using fresh dispatcher scopes. No transport or generic SQL dialect. |
| `src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/{Gloaming.Messaging.EntityFrameworkCore.Postgres.csproj,PostgresOutboxModelExtensions.cs,PostgresOutboxDispatcher.cs,PostgresOutboxServiceCollectionExtensions.cs}` | Reference the EF package; specialize JSONB/time/model configuration and implement private PostgreSQL claims/fencing/backoff and typed scoped composition. |
| Family `{README.md,docs/capabilities.md}` and package `README.md` | Self-sufficient setup, guarantees, PostgreSQL-only status, privilege/duplicate/cancellation limits and deferred context. |
| `src/Gloaming.Messaging/tests/MessagingTests/{MessagingTests.csproj,MessageTests.cs}` | Exercise envelope identity/schema validation, owned JSON lifetime and provider-free contract adoption. No hypothetical handler/bus abstraction tests. |
| `src/Gloaming.Messaging/tests/OutboxPostgresTests/{OutboxPostgresTests.csproj,ProducerTests.cs,DispatchTests.cs,RegistrationTests.cs,WorkerTests.cs,OutboxConsumer.cs}` | Public-interface producer/dispatcher/configuration and real hosted-worker tests, including fresh scopes, failure recovery and cooperative shutdown. Share existing PostgreSQL test support. |
| `samples/Wholesale/modules/Inventory/Inventory/{Inventory.csproj,InventoryDbContext.cs,InventoryRegistration.cs}` | Explicit outbox dependency/model, existing tenant validation reuse, both save guards and typed producer registration. No automatic dispatch registration. |
| Inventory `StockPositions/StockPositionCommands.cs`; new `Messaging/StockIssueMessages.cs`; `Inventory.Contracts/StockIssueRecordedV1.cs` | Explicitly map and enqueue only accepted stock issues; integration DTO remains consumer-owned and independent of library types. Existing open/receive/rebuild behavior has no automatic publication. |
| Inventory `Migrations/{new AddTransactionalOutbox.cs,new AddTransactionalOutbox.Designer.cs,InventoryDbContextModelSnapshot.cs}` | Add only `inventory.outbox_messages` through a forward native migration. Preserve every previous migration/fixture. |
| `samples/Wholesale/EventPersistenceDemo/{Program.cs,DemoComposition.cs,new OutboxJourney.cs,new InventoryRabbitMqPublisher.cs,EventPersistenceDemo.csproj,README.md}` | Add an explicit `--outbox` journey, consumer-owned RabbitMQ topology/publisher and callable dispatch. Preserve default journeys and authored fixtures/output. Broker URL supplied explicitly; no new Aspire graph or template preset. |
| `samples/Wholesale/EventPersistenceDemo.Tests/{new AppendTests.Outbox.cs,new OutboxDispatchTests.cs,EventPersistenceDemo.Tests.csproj}`; new `tests/Support/RabbitMqFixture.cs` | Real PostgreSQL/RabbitMQ proofs: append/main-state/outbox atomicity, tenant policy, rejection/races/rollback/recovery, confirms, unroutable publication, duplicate stable identity and effect-free rebuild. A receiving observer is not an inbox business proof. Preserve prior semantic tests. |
| `src/Gloaming.Messaging/samples/OutboxDemo/{OutboxDemo.csproj,Program.cs,DemoJourneys.cs,ExportDbContext.cs,ExportRequestCommands.cs,HttpCommandPublisher.cs,README.md,Migrations/*}`; `src/Gloaming.Messaging/samples/OutboxDemo.Tests/{OutboxDemo.Tests.csproj,AdoptionTests.cs}` | Add the ordinary state-stored/direct-JSON executable adopter, module-free composition, native migration and real transaction/delivery proofs. No event-sourcing/context-library reference. |
| `tests/ArchitectureTests/{ArchitectureTests.csproj,AssemblyDependencyTests.cs,AdoptionDependencyTests.cs,SampleModuleBoundaryTests.cs}` | Prove core/EF/Postgres dependency direction, transport-free libraries, module-typed registration and intentional adoption. Contracts remain free of technical dependencies. |
| `Directory.Packages.props`, `ModulithFoundry.slnx`, `.github/workflows/ci.yml`, `README.md`, `docs/development.md`, extraction plan and O1 report | Pin native Hosting/Logging abstractions and sample/test-only RabbitMQ.Client/Testcontainers.RabbitMq dependencies. Add a Messaging family lane for core/EF/PG/worker, standalone and focused broker adoption proofs; existing Wholesale event checks remain in EventSourcing. No archived broker/topology suite revival. |

No archive edits, checkpoint reset, migration/fixture replacement, sample removal, template
snapshot update, library broker dependency or changes to existing library interfaces are in scope.
If inspection during implementation requires an additional replacement, return it for review.
The stable choice of transaction ownership/provider-specific mechanics receives an ADR only
after interface/scope approval; the proposal is not an accepted decision yet.

## Required proofs

- Uncommitted work is not dispatchable; commit publishes one complete envelope. Producer
  rollback, cancellation, later outbox SQL failure and competing stale business writes leave
  neither partial business changes nor outgoing work; fresh contexts can retry/redecide.
- Inventory append/header/main-state/outbox commit together. Insufficient stock emits no
  event or outgoing row. Rebuilding/replaying old facts enqueues/publishes nothing.
- Producer ownership reuses current filters/save validation. Dispatch preserves exact owner,
  alias/version/payload/ID. Two typed contexts and custom schema/table names do not cross-wire.
- Payload lifetime survives source-document disposal and fresh-context JSONB round trip;
  the ordinary adopter needs no codec, event store, Access or tenancy dependencies.
- Competing PostgreSQL dispatchers do not share an active claim. Held rows are skipped;
  expired leases are recoverable. An old publisher finishing after takeover cannot complete
  or defer its successor's claim, including expiry without takeover.
- Actual HTTP acceptance followed by completion failure is repeated after expiry with
  identical message identity/content. Abandoned claims are recoverable in a fresh context.
  These prove the retained-state recovery protocol, not a new abrupt-process-kill test or
  exactly-once receiver effects; archived crash tests remain historical evidence.
- RabbitMQ acceptance waits for native confirmation; unroutable/rejected/timed-out
  publication cannot mark dispatched. Confirmed acceptance followed by completion failure
  can redeliver the identical envelope. Native sample transport owns bounded cancellation,
  channel/connection handling and manual receiver acknowledgement, without claiming an inbox.
- Two module-typed contexts, publishers and optional hosted workers do not resolve each
  other's dependencies or dispatch tables. The worker uses a new scope per operation, recovers
  after publication/storage failure, waits on no eligible work and stops cooperatively.
  Invalid provider/model setup fails explicitly rather than spinning a retry loop.
- Publication failure defers only its valid claim; retry eligibility follows the database
  clock. Cancellation/unknown payload/storage failure cannot falsely mark dispatched.
- Native synchronous/asynchronous saves require the enqueue transaction and correct model;
  misuse fails before accepted writes. Relevant existing event, ownership, architecture,
  style/analyzer/build and event-free external template proofs remain passing.
- Ordinary-EF producer commit/rollback is atomic at ReadCommitted, RepeatableRead and
  Serializable; preserve the requested native isolation. Serialization conflicts propagate
  for consumer-owned fresh-operation retry. Dispatcher phases use explicit ReadCommitted
  and reject producer/native/enlisted/ambient transaction overlap.

New executions belong in the O1 slice report. The frozen archive's broker/crash results are
historical evidence only; no new reusable mechanism has been proven by this proposal.

## Implementation follow-up

The owner authorized starting the revised scope and may revisit it after reviewing code.
No public inbox types are included. The implementation retains the proposed surface; PostgreSQL
registration validates the provider-specific model, while direct EfOutbox is the reusable EF
enqueue implementation. Model inspection and short independent transactions remain in the
PostgreSQL dispatcher; claims/lease tokens stay private. Native migrations are scaffolded and
then formatted to repository conventions, preserving every old migration and fixture.

JSONB tests compare semantic values to the original JSON, because PostgreSQL normalizes its
lexical representation. Retries of the stored envelope retain identical content/identity.
Npgsql's serialization-conflict wrapper propagates unchanged; the native unique event-position
constraint can also arbitrate losing event writers. Those failures are not hidden retries.

Inventory's native publisher sets CorrelationId from the stock-position ID as an explicit
sample grouping policy. MessageId remains the delivery identity. No CausationId is invented
where the command contract carries no incoming message/command ID. The inbox proposal must
define deduplication scope and metadata meaning explicitly; correlation/causation alone cannot
identify one delivery. Current setup/limits live in the
[Messaging family](../../src/Gloaming.Messaging/README.md), and fresh proof results belong in
[the report](../reports/outbox1-transactional-dispatch.md).

## Deferred next capabilities

Durable inbox intake/delivery deduplication, callable transactional processing and optional
worker in the same Messaging family, with independent opt-ins and a real module-to-module
RabbitMQ commit/ack/recovery sample; receipt-only inbox mode if useful; receiver-owned semantic
idempotency; in-process durable module handoff; additional consumer-owned transport examples;
retention and
identity-preserving redrive; poison/attempt-cap policy; lease renewal; batch/parallel dispatch;
per-stream/tenant ordering; other providers; encryption/secret delivery; automatic domain-event
collection; actor/initiator propagation; audit; async projections; cross-module transactions
and durable workflow/process managers. They remain useful directions with their own proof
obligations, rather than speculative options or implicit support in O1. Template messaging
presets and production transport/shutdown guarantees remain separately reviewed work.
