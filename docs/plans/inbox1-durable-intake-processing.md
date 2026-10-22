# I1: Durable inbox intake and transactional local processing

Status: interface and scope owner-approved, 2026-10-08; implementation complete and
left unstaged for line-by-line review. [Fresh execution report](../reports/inbox1-durable-intake-processing.md). Base `beafa4e` on `codex/durable-inbox`; O1 is checkpointed as
`2d7a865` and merged through PR #1. This proposal adds one capability to the existing
Messaging family. Interface approval and approval of the eventual complete commit are separate.

## Evidence and complexity worth concentrating

The archive's Inventory `ReserveStockHandler` validates transport identity, selects a receipt,
checks its delivery fingerprint, makes a state-dependent decision, adds outgoing work and
saves the receipt with the business effects. Sales outcome handlers repeat producer/identity
checks and receipt handling. `InventoryInboxReceipt` has MessageId, OrganizationId, Fingerprint
and ProcessedAt. Those are processed receipts, not retained incoming work; Rebus owns delivery
and retries. Archived code and fixtures remain unchanged and are not new proof results.

The active Inventory publisher retains MessageId and groups stock-issue notifications by
stock position. Its native RabbitMQ observer only acknowledges a delivery. The independent
OutboxDemo sends RenderExportV1 over HTTP and considers a successful response accepted; it
currently has no durable receiver. Both can repeat publication after acceptance if recording
outbox completion fails. These callers expose a concrete receiving-side obligation.

I1 concentrates unique intake, retained envelope comparison, local processing exclusion,
atomic completion and retry eligibility. Transport adapters still establish trust and acknowledge
delivery. Domain handlers still decide and modify their own module data. Extracting only an
acknowledgement wrapper would leave the difficult database protocol duplicated.

## Consumer journeys

Intake uses the receiving module's explicit native transaction. ReceiveAsync executes its
insertion immediately on that transaction; it does not call SaveChanges or commit. Its return
value is provisional until commit. A broker adapter must not acknowledge on that return alone.

```csharp
// Consumer transport adapter: validate the allowed producer, contract and tenant first.
IncomingMessage message = ParseAndAdmit(delivery);
await using var transaction = await database.Database.BeginTransactionAsync(
    IsolationLevel.ReadCommitted, cancellationToken);
InboxReceiveResult result = await inbox.ReceiveAsync(
    "inventory.issue-stock", message, cancellationToken);
await transaction.CommitAsync(cancellationToken);

// Same native channel and delivery tag; conflict/failure never reaches this line.
await channel.BasicAckAsync(
    delivery.DeliveryTag, multiple: false, cancellationToken: cancellationToken);
```

An HTTP adapter uses the same flow and returns acceptance after commit. AlreadyReceived is
also accepted after commit. A typed identity conflict maps to an explicit HTTP conflict or
consumer-selected broker rejection/quarantine policy, rather than a successful duplicate.

Processing is a separate operation with its own fresh scope and native transaction. Unlike
producer enqueue/intake, this operation owns SaveChanges and commit so a worker cannot forget
to include inbox completion. Its handler makes local changes and may enqueue an outgoing reply.

```csharp
// Callable directly, or invoked by the optional sequential worker.
await using var scope = services.CreateAsyncScope();
var processor = scope.ServiceProvider
    .GetRequiredService<IInboxProcessor<InventoryDbContext>>();
InboxProcessingResult result = await processor.ProcessNextAsync(
    "inventory.issue-stock", cancellationToken);
```

```csharp
// Consumer handler, registered only for this module/subscription.
public async Task HandleAsync(IncomingMessage message, CancellationToken cancellationToken)
{
    IssueStockV1 command = DecodeAndValidate(message);
    EstablishAdmittedOrganization(message, command);
    replyMetadata.Initialize(message); // Consumer-owned correlation/causation mapping.
    StockPositionChangeResult result = await commands.IssueAsync(
        new(command.StockPositionId, command.ExpectedVersion, [new(command.Quantity)]),
        cancellationToken);
    // Accepted issues already enqueue StockIssueRecordedV1 in the existing command path.
    // Business refusal becomes a consumer-owned StockIssueDeclinedV1 reply, then returns.
    EnqueueDeclineWhenRequired(result, message);
    // Processor performs the final native save/completion/commit.
}
```

The handler receives the retained wire envelope and explicitly chooses decoding, admission
and dispatch. No reflection-based CLR discovery, inferred routing, payload-generic drilling or
mandatory Events codec is introduced. One consumer handler may switch among explicitly supported
message names; a generic per-DTO handler registry is deferred until a caller needs it.

## Proposed public surface

The provider-free `Gloaming.Messaging` package adds an immutable incoming envelope:

```csharp
public sealed class IncomingMessage
{
    public IncomingMessage(
        Guid messageId,
        string producerKey,
        string messageName,
        int schemaVersion,
        JsonElement payload,
        string? tenantKey = null,
        string? correlationId = null,
        string? causationId = null);

    public Guid MessageId { get; }
    public string ProducerKey { get; }
    public string MessageName { get; }
    public int SchemaVersion { get; }
    public JsonElement Payload { get; }
    public string? TenantKey { get; }
    public string? CorrelationId { get; }
    public string? CausationId { get; }
}
```

ProducerKey is a stable producer identity assigned by the receiving adapter after checking
its trusted transport configuration; it is not authentication supplied by Gloaming. SubscriptionKey
is the receiver's stable logical processing registration, supplied to ReceiveAsync rather than
accepted from untrusted payload data. Neither is necessarily a physical broker queue.

OutgoingMessage's existing constructor and FromPayload method gain trailing optional
`string? correlationId = null, string? causationId = null` and matching get-only properties.
MessageId remains a nonempty Guid. CorrelationId and CausationId are optional opaque nonblank
strings, not deduplication keys. TenantKey remains optional and does not admit a tenant. Payload
ownership remains a JsonElement clone; JSONB stores values rather than original lexical bytes.
No general metadata bag or common Gloaming execution context is added.

Core also adds `InboxMessageConflictException : Exception` with SubscriptionKey, ProducerKey
and MessageId properties and constructor
`InboxMessageConflictException(string subscriptionKey, string producerKey, Guid messageId)`.
It never exposes the
retained payload or tenant details in its message. It identifies delivery identity reused with
different envelope content, not a rejected business decision.

The existing EF package adds:

```csharp
public interface IInbox<TDbContext> where TDbContext : DbContext
{
    Task<InboxReceiveResult> ReceiveAsync(
        string subscriptionKey,
        IncomingMessage message,
        CancellationToken cancellationToken = default);
}

public enum InboxReceiveResult { Queued = 1, AlreadyReceived = 2 }

public interface IInboxHandler<TDbContext> where TDbContext : DbContext
{
    Task HandleAsync(IncomingMessage message, CancellationToken cancellationToken);
}

public interface IInboxProcessor<TDbContext> where TDbContext : DbContext
{
    Task<InboxProcessingResult> ProcessNextAsync(
        string subscriptionKey,
        CancellationToken cancellationToken = default);
}

public enum InboxProcessingResult { NoWork = 1, Processed = 2 }
public sealed record InboxProcessingOptions(TimeSpan RetryDelay);
public sealed record InboxWorkerOptions(TimeSpan IdleDelay, TimeSpan FailureDelay);
```

Queued means this transaction inserted retained work. AlreadyReceived means an equivalent
retained delivery exists, pending or completed. NoWork means no eligible unlocked row for
this subscription was found, not that the backlog is empty. Processed means local effects
and completion committed. Business refusal may count as Processed when the handler records
or replies to that refusal. Handler exceptions are failures and propagate.

Provided `InboxMessageRecord` has public getters/private setters for SubscriptionKey,
ProducerKey, MessageId, MessageName, SchemaVersion, Payload, TenantKey, CorrelationId,
CausationId, ReceivedAt, AvailableAt and nullable ProcessedAt. There is no public constructor
or lifecycle mutation API. It has no transaction, claim token or in-memory execution fields.

Model and DI extensions follow O1's existing explicit setup:

```csharp
// InboxModelExtensions, in Gloaming.Messaging.EntityFrameworkCore.
public static EntityTypeBuilder<InboxMessageRecord> ConfigureInbox(
    this ModelBuilder model, string schema, string table);
public static void ValidateInboxChanges(this DbContext database);

// PostgresInboxModelExtensions, in the provider package.
public static EntityTypeBuilder<InboxMessageRecord> ConfigurePostgresInbox(
    this ModelBuilder model, string schema, string table);

// InboxServiceCollectionExtensions, in the EF package.
public static IServiceCollection AddInboxHandler<TDbContext, THandler>(
    this IServiceCollection services, string subscriptionKey)
    where TDbContext : DbContext
    where THandler : class, IInboxHandler<TDbContext>;

// PostgresInboxServiceCollectionExtensions, in the provider package.
public static IServiceCollection AddPostgresInbox<TDbContext>(
    this IServiceCollection services) where TDbContext : DbContext;
public static IServiceCollection AddPostgresInboxProcessor<TDbContext>(
    this IServiceCollection services, InboxProcessingOptions options)
    where TDbContext : DbContext;

// InboxWorkerServiceCollectionExtensions, in the EF package.
public static IServiceCollection AddInboxWorker<TDbContext>(
    this IServiceCollection services, string subscriptionKey, InboxWorkerOptions options)
    where TDbContext : DbContext;
```

All context arguments require DbContext; THandler requires class and
IInboxHandler<TDbContext>. Handler registration uses native keyed DI by SubscriptionKey,
within the typed context contract; duplicate/conflicting bindings reject rather than silently
choosing a handler. Multiple subscriptions can select different handlers without a second
untyped DbContext registration. Intake requires no handler/publisher/worker. Processing needs
a handler for the requested subscription; worker startup validates that binding and model.
Each attempt receives a fresh scope; direct callers follow the same scoped recipe.
Concrete PostgreSQL intake/processor and hosted-worker implementations are internal; DI can
register them without adding public implementation constructors or an SQL adapter contract.

ValidateInboxChanges rejects tracked insert/update/delete of the provided record. Intake and
completion use supported parameterized SQL, not tracked lifecycle edits, so no weak registry
or transaction handle is needed for inbox rows. This guard does not sandbox privileged SQL.
Existing outbox validation additionally checks its new retained correlation/causation values.
All new/changed public contracts receive XML documentation, including transaction ownership.

| Condition | Public behavior |
| --- | --- |
| Empty ID/key/name, null/undefined JSON, nonpositive schema, blank optional metadata or invalid delay | ArgumentException/ArgumentOutOfRangeException as appropriate; null required objects use ArgumentNullException. No intake/handler work occurs. |
| Unsupported provider/model, missing/conflicting handler binding, unsupported transaction/isolation or pending processing changes | InvalidOperationException with a configuration/operation diagnostic. |
| Retained identity reused with incompatible content | InboxMessageConflictException; no new work and no successful acknowledgement/HTTP acceptance. |
| Handler, EF concurrency, database or cancellation failure | Original exception propagates; failures before commit roll back local processing. A separate retry-scheduling failure is reported together with the original failure. Commit faults may be ambiguous as described below. |
| Commit response lost | Database outcome may be ambiguous. Retry from a fresh context; retained identity/completion decides whether work remains. No assertion of rollback after an ambiguous commit response. |

## Storage, ordering and concurrency guarantees to prove

One receiving module maps one inbox table in its own schema. The primary key is
`(subscription_key, producer_key, message_id)`. TenantKey is not part of that key: changing
tenant metadata under the same delivery identity must conflict, not create new work. Two
subscriptions may legitimately retain the same delivery independently. Two producers have
separate delivery namespaces. Renaming those keys changes deduplication/dispatch meaning
and requires an explicit consumer migration; they are not implementation class names.

Intake requires an explicit native ReadCommitted transaction, with no enlisted/ambient
transaction. Use INSERT ON CONFLICT DO NOTHING RETURNING, then a separate statement to
compare a pre-existing row. This separate snapshot matters for a concurrent winning insert.
Compare MessageName, SchemaVersion, Payload, TenantKey, CorrelationId and CausationId using
PostgreSQL JSONB equality and null-safe scalar equality. Formatting/property order and equal
JSONB numeric values are equivalent; array order and changed values are not. This compares
envelopes, not consumer-defined semantic operation identities. I1 rejects other intake
isolation levels explicitly rather than claiming snapshot behavior it has not proved.

Processing begins its own native ReadCommitted transaction on a clean context. It selects
one eligible row for the requested subscription with FOR UPDATE SKIP LOCKED, retaining that
row lock through handler execution, native SaveChanges, completion SQL and commit. The
ordering is best effort by AvailableAt/ReceivedAt and delivery key; FIFO is not promised.
Inbox exclusion does not protect different deliveries modifying the same business aggregate;
consumer native constraints/concurrency tokens still arbitrate those changes.

Handlers perform bounded local database work on the injected owning context. They may
enqueue outbox work in that transaction. They must not commit, roll back, replace the transaction,
start a cross-module transaction or perform external effects as part of this protocol. The
processor checks its transaction association before final save/completion; trusted consumer
code and privileged native bypasses remain outside any sandbox guarantee. Another context's
effects are not made atomic merely because it is resolved in the same scope.

On handler/save failure, roll back the whole processing transaction. Schedule a positive
RetryDelay using database time in a separate small transaction, conditional on the row still
being unprocessed. That update cannot reverse a successor's completion. Failure to schedule
retry propagates alongside the original error. No persisted attempt counter, stack trace or
poison threshold is added. Cancellation rolls back without forcing a retry write; connection
loss releases the database lock and leaves work pending. Dispose the failed context.

This replaces the earlier inbox suggestion of expiring claims/stale-token fencing. Outbox
publication still needs that protocol because transport acceptance happens outside a local
transaction. Inbox handlers here only make local transactional changes, so I1 does not add
lease duration, renewal or a public ClaimLost result. A lost connection/abandoned transaction
recovery proof replaces an inbox stale-lease proof. Long-running external rendering belongs
to a separate consumer capability, not this locked handler.

Incoming/queued payloads are decoded only by the consumer. Unsupported contracts, invalid
payloads or failed admission are visible errors; no implicit schema upgrade or silent completion.
Retention is deferred: completed identities remain present so retries remain deduplicated.
Unavailable PostgreSQL, SQL errors and native EF concurrency errors propagate. The library
does not silently replay handlers through an execution strategy or promise exactly-once
external effects.

## Concrete adopters and ownership

**Inventory:** add an opt-in IssueStockV1 integration command containing StockPositionId,
ExpectedVersion and Quantity, with Organization metadata validated against an allowed producer
and trusted Organization mapping. A module-owned inbox handler establishes a fresh tenant
context and calls the existing IssueAsync command. Accepted work preserves existing event/
inline-state/outbox behavior. NotFound, Conflict and InsufficientStock map to a module-owned
StockIssueDeclinedV1 reply with a stable reason; neither creates stock facts. A consumer-owned
scoped metadata holder carries inherited correlation and immediate incoming MessageId causation
to reply construction without adding messaging types to IStockPositionCommands. Ordinary
direct commands retain stock-position correlation and no invented causation.

The new consumer wire shapes are `IssueStockV1(Guid StockPositionId, long ExpectedVersion,
decimal Quantity)` and `StockIssueDeclinedV1(Guid MessageId, string OrganizationKey,
Guid StockPositionId, long ExpectedVersion, StockIssueDeclineReason Reason,
decimal? Available, decimal? Requested)`. Decline reasons are NotFound = 1, Conflict = 2 and
InsufficientStock = 3. The incoming command does not duplicate tenant/message identity in its
payload; the adapter validates that metadata before intake and the handler checks admission
before business queries. Recorded and declined replies use the existing stock-issues route.

The explicit EventPersistenceDemo --inbox journey owns RabbitMQ topology, transport metadata,
manual acknowledgements and bounded producer/tenant allow-list policy. No browser/runtime
composition registration is activated by default. No Sales/Purchasing business model is changed.
The allow-list is finite sample policy, not a claim that a producer header authenticates a
sender. Production broker/endpoint permissions and producer-to-Organization admission remain
consumer responsibilities. Operational inbox selection runs across that module's queue;
ordinary tenant-protected business reads happen only after admitted context establishment.

**Independent Rendering receiver:** new InboxDemo uses ordinary EF RenderJob state and direct
JSON, without Events, EventSourcing, Tenancy, ActorIdentity or Persistence. It accepts the existing
OutboxDemo RenderExportV1 over HTTP, commits intake before responding and separately creates a
local RenderJob. Rendering owns that queued job; recording it is not claiming a PDF was rendered.
ExportRequestId is the consumer's semantic job identity: identical repeated commands with a new
delivery ID do not create another job; incompatible pages are a consumer failure. This receiver
registers inbox only, with no outbox table/publisher/dispatch services of its own.
It decodes an explicit receiver-local wire DTO matching exports.render v1; it does not
reference the Exports implementation assembly. The composition host alone references both
applications. Shared business Contracts may be chosen by a consumer without adding them to Gloaming.

A small MessagingDemo console composes the existing Exports sender and the new Rendering receiver
with separate contexts/schemas/migration histories and native RabbitMQ adapters. It demonstrates
commit-outbox, confirmed publication, commit-intake, manual ack and later processing. The modules
can instead use separate databases; no shared connection/transaction is required. InboxDemo's
HTTP journey proves a different acceptance boundary; Inventory proves tenant admission and
event/required-state/reply atomicity. Consumer contracts and routing stay outside Gloaming.

## Exact file/behavior change map

Paths below are relative to the repository. No archived file, existing migration, literal fixture
or generated-template source is replaced. Generated new migration IDs are assigned by native EF
tooling during implementation; their table/column changes below are the reviewed scope.

| Files | Proposed change |
| --- | --- |
| `src/Gloaming.Messaging/Gloaming.Messaging/{IncomingMessage,InboxMessageConflictException}.cs` | New incoming envelope and typed incompatible-redelivery error. |
| `src/Gloaming.Messaging/Gloaming.Messaging/OutgoingMessage.cs` | Optional retained correlation/causation constructor/factory arguments and properties. |
| `src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore/{IInbox,IInboxHandler,IInboxProcessor,InboxMessageRecord,InboxProcessingOptions,InboxWorkerOptions,InboxModelExtensions,InboxServiceCollectionExtensions,InboxWorker,InboxWorkerServiceCollectionExtensions}.cs` | Typed intake/handler/processor contracts, result enums beside interfaces, mapping/save guard, keyed handler setup and opt-in fresh-scope hosting. |
| `src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore/{OutboxMessageRecord,OutboxModelExtensions}.cs` | Retain/map/validate new optional metadata using the existing enqueue registry; no registry, outbox transaction or lease behavior change. |
| `src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/{PostgresInbox,PostgresInboxProcessor,PostgresInboxModelExtensions,PostgresInboxServiceCollectionExtensions}.cs` | PostgreSQL intake/conflict comparison, locked transactional processing/retry and typed registration. Private SQL helpers may be separate internal files if used by both operations. |
| `src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/PostgresOutboxDispatcher.cs` | Read correlation/causation in claim return and envelope reconstruction; retain existing claim/completion protocol. |
| `samples/Wholesale/modules/Inventory/Inventory.Contracts/{IssueStockV1,StockIssueDeclinedV1}.cs` | Consumer-owned incoming DTO and business refusal reply/reasons; no technical-library Contracts dependency. |
| `samples/Wholesale/modules/Inventory/Inventory/Messaging/{StockIssueInboxHandler,StockIssueMessageAdmission,InventoryMessageContext}.cs` | Trusted command processing, native business call and reply metadata, with explicit admission responsibility. |
| `samples/Wholesale/modules/Inventory/Inventory/{InventoryDbContext,InventoryRegistration}.cs`, `Messaging/StockIssueMessages.cs` | Inbox mapping/save guard and opt-in receiver registration; correlation/causation in explicit outgoing mapping. |
| `samples/Wholesale/modules/Inventory/Inventory/Migrations/` | One new forward migration: inventory inbox and nullable outbox correlation/causation columns; update current snapshot. Preserve previous migrations/designers. |
| `samples/Wholesale/IntegrationTests/Scenarios/EventPersistence/{Program,InboxJourney,InventoryRabbitMqReceiver,InventoryRabbitMqPublisher}.cs` | Explicit --inbox journey and native transport metadata/ack policy; publisher carries retained correlation/causation and supports recorded/declined replies. Existing default journeys remain. |
| `src/Gloaming.Messaging/samples/OutboxDemo/HttpCommandPublisher.cs`, `src/Gloaming.Messaging/samples/OutboxDemo/Migrations/` | Carry optional wire metadata; new forward outbox-metadata migration/current snapshot, preserving originals. |
| `src/Gloaming.Messaging/samples/InboxDemo/InboxDemo.csproj`, `{Program,InboxDemoHost,RenderDbContext,RenderExportHandler,RenderJob}.cs`, `README.md`, `Migrations/` | New ordinary EF inbox-only receiver with explicit HTTP setup/acceptance and optional worker; native design-time factory in RenderDbContext.cs and InitialRendering migration/snapshot. |
| `src/Gloaming.Messaging/samples/MessagingDemo/MessagingDemo.csproj`, `{Program,MessagingJourney,RabbitMqExportPublisher,RabbitMqRenderReceiver}.cs`, `README.md` | Finite native RabbitMQ journey composing the separately owned sender/receiver contexts; adapters remain consumer source. |
| `src/Gloaming.Messaging/tests/MessagingTests/MessageTests.cs`, `tests/OutboxPostgresTests/{ProducerTests,DispatchTests,EnqueueLifecycleTests}.cs`, new `tests/InboxPostgresTests/` | Envelope/metadata guard/round-trip tests and real PostgreSQL intake, processing, recovery, binding and hosting proofs. All test-directory paths in this row belong to src/Gloaming.Messaging. |
| `samples/Wholesale/IntegrationTests/EventPersistence/InboxDispatchTests.cs`, new `src/Gloaming.Messaging/samples/InboxDemo.Tests/`, existing `src/Gloaming.Messaging/samples/OutboxDemo.Tests/` | Tenant/event/reply transaction tests, ordinary EF/HTTP metadata/adoption and real two-module broker failure/recovery proofs. |
| `tests/ArchitectureTests/` | Assert inbox-only/outbox-only opt-in and module/context boundaries with new projects; template omission remains. |
| `ModulithFoundry.slnx`, `.github/workflows/ci.yml` | Include new projects and run their proofs in the existing Messaging lane; ensure each broker class runs once. No unrelated CI logging/path-filter cleanup. |
| Messaging family/package READMEs and `docs/capabilities.md`; sample READMEs and `samples/Wholesale/modules/README.md` | Current setup, ownership, guarantees, error semantics and deferred features after approval/implementation. |
| `docs/plans/library-extraction.md`, this brief, `docs/design.md`, ADR/report under `docs/adr/` and `docs/reports/` | Record review, resulting settled protocol and separately dated new evidence. Do not mark proposed behavior supported prematurely. |

No new Gloaming package or package dependency is needed. Existing EF relational/DI/hosting,
Npgsql, RabbitMQ.Client, native Kestrel and PostgreSQL/RabbitMQ Testcontainers are sufficient.
The exact final added/modified file list accompanies line-by-line implementation review.

## Verification and completion criteria

- Real PostgreSQL intake: rollback leaves no retained work; same identity/content is recognized
  pending and completed; racing committed/rolled-back arrivals retain one row; changed contract,
  payload, tenant or correlation/causation conflicts. Exercise JSONB formatting/object-order/
  numeric equivalence, ordered arrays and separate producer/subscription identities.
- Processing: competing processors cannot execute the same retained row simultaneously;
  another eligible row remains selectable. Handler/save/completion-write failures before commit leave no business
  effects/completion/outgoing work. Positive retry delay permits other work. A fresh scope
  recovers; cancellation and a terminated database connection release the lock. A resumed old
  connection cannot commit stale local effects. Distinct deliveries still obey native business
  concurrency, and incomplete local work is never marked processed.
- Inventory: accepted command atomically stores facts, inline aggregate, outgoing reply and inbox
  completion; insufficient/stale/missing stock produces the explicit business reply only. Trusted
  admission and context isolation hold across tenants. No metadata field itself grants access.
  Outgoing replies inherit correlation and set causation to the incoming MessageId.
- Native RabbitMQ: rollback intake before ack redelivers; closing the channel after committed
  intake but before ack redelivers without duplicate work; sender completion failure repeats the
  same delivery; acked intake survives handler failure and processing recovers without broker
  redelivery. HTTP success follows committed intake, not completed rendering.
- Independent registration: inbox-only model/services work without an outbox or publisher;
  outbox-only use needs no inbox or handler. Two typed contexts/subscriptions select their own
  mappings/handlers. Fail unsupported provider/model, missing/conflicting handler, invalid input,
  existing/ambient processing transaction and tracked lifecycle mutation visibly.
- Run existing Messaging and affected event/consumer/architecture checks, relevant whole-solution
  build/style/analyzers/formatting, documentation links, frozen-archive verification and T1 generated
  omission checks. CI must run the new executable proofs. No archive broker suites are revived.

This file records inspected callers and a researched proposal, not executed new runtime proofs.
The finished slice report will distinguish those proofs from O1 and archived evidence.

## References and deferred scope

RabbitMQ distinguishes publisher confirmation from consumer acknowledgement and requeues
unacknowledged deliveries after channel/connection loss. Native channel acknowledgement therefore
stays visible after the consumer's database commit. [RabbitMQ confirmations](https://www.rabbitmq.com/docs/confirms),
[native .NET guide](https://www.rabbitmq.com/client-libraries/dotnet-api-guide).

PostgreSQL documents queue-oriented SKIP LOCKED and ReadCommitted's concurrent ON CONFLICT
visibility. Those support the proposed row-lock processing and separate duplicate comparison
statement; they are not a substitute for I1's actual concurrent executions.
[SELECT](https://www.postgresql.org/docs/18/sql-select.html),
[isolation](https://www.postgresql.org/docs/18/transaction-iso.html),
[INSERT](https://www.postgresql.org/docs/18/sql-insert.html).
JSONB equality follows stored JSON values, not lexical identity.
[JSON types](https://www.postgresql.org/docs/18/datatype-json.html).
Native EF transaction composition and retry-strategy compatibility remain visible.
[EF transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions).

Defer receipt-only mode, generic ack/transport adapters, typed DTO routing, automatic upcasting,
retention, poison/dead-letter management, attempt caps, operator redrive, parallel/batch workers,
global ordering, external-effect inbox handlers, distributed transactions, other providers,
workflow engine, tracing/actor propagation and template messaging presets. Consumer scheduling
and transport policies can evolve independently. No event-sourcing projection or maintenance
capability is added by this slice.
