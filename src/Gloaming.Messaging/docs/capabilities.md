# Messaging capabilities and remaining work

Worker registration and polling options are supplied by the explicitly selected
[hosting package](../Gloaming.Messaging.EntityFrameworkCore.Hosting/README.md), using the
`Gloaming.Messaging.EntityFrameworkCore.Hosting` namespace. Callable persistence/delivery
operations remain independently usable without this package.

The [native PostgreSQL wakeup recipe](../../../modular-monolith-patterns/postgres-wakeups.md)
optionally shortens idle waits around those callable roles, with dedicated sessions, retained
per-worker hints, reconnect registration and independent polling. It introduces no library runtime
or dependency in provider-free hosting; eligibility and typed completion/accounting remain unchanged.

The family separates [outbox enqueue/dispatch](../README.md), [inbox intake/processing](inbox.md),
[inbox recovery](inbox-recovery.md), [outbox recovery](outbox-recovery.md) and optional workers.
Completion/accounting uncertainty faults the selected worker; native host options and consumer
repair/restart policy govern recovery. [Operational visibility](outbox-observability.md) supplies
callable metadata reads; hosts choose sampling, readiness and alert thresholds.
[Outbox retention](outbox-retention.md) and [inbox retention](inbox-retention.md) have separate
authority and evidence-lifetime contracts. Deleting a processed receipt ends duplicate/content
evidence and can permit old work to execute again. Unprocessed/dead-lettered work remains.

[Queued-message schema compatibility](schema-compatibility.md) demonstrates retained v1 work
and explicitly selected v2 writes through independent consumer contexts and HTTP/RabbitMQ.
Rendering opts into the existing JSON codec; Messaging packages gain no serializer dependency.
Original-envelope deduplication, conditional retry and the outbox's acceptance uncertainty remain.

## Current composition

[Finite due-time intents](due-intents.md) are independently selected in the PostgreSQL package:
immutable occurrence/envelope storage, locked suppression and bounded atomic outbox handoff.
Caller-owned native ReadCommitted transactions, explicit UTC cutoffs and native saves/commits
remain visible. Scope is independent of optional envelope tenancy. Recurrence/calendar and
supplied scheduling workers remain outside that finite contract.

The optional [recurrence module](../Gloaming.Messaging.Recurrence.EntityFrameworkCore.Postgres/README.md)
adds retained generation/progress, explicit interval or zoned cron definitions, bounded
Skip/Coalesce/CatchUp and atomic finite-intent staging. It selects Cronos independently; consumers
pin calendar inputs, select current business relevance and invoke their own polling. No scheduling
worker or business calendar is installed.

[Technical contribution sets](contributions.md) add immutable named content, explicit expected
identities/required count and one same-transaction completion or eligible timeout notice. The
consumer selects participant/quorum policy and separately applies business acceptance/current
relevance. Existing continuation execution remains a single-pending-request model; this technical
protocol can be composed with its consumer messages without adding a parallel workflow graph.

Each module selects its own typed DbContext, schema/table, business/integration contracts,
publisher and optional worker. A state-stored adopter needs no event sourcing or context
libraries. An event-sourced adopter enqueues deliberately, alongside facts and the required
inline aggregate. Rebuild/replay does not publish old effects. Producer SaveChanges and Commit
remain explicit; the separately invoked inbox processor owns its local save/completion/commit. Atomicity does not replace concurrency predicates protecting business decisions.

Enqueue preserves the producer's native isolation. ReadCommitted is the sample baseline;
RepeatableRead/Serializable commit/rollback are exercised separately. Native serialization
errors (including Npgsql's transient-failure wrapper) propagate; consumers retry the whole
decision in a fresh context when appropriate. No package escalates isolation or introduces
a universal Gloaming transaction scope. Multiple DbContexts require explicit native shared
connection/transaction setup to coordinate; the outbox supports one owning producer context.

Dispatch commits a lease before transport work and fences completion/retry with its token
and expiry. Expiry may allow a successor to publish while the old publisher is still running;
fencing protects database completion, not external exactly-once effects. Best-effort ordering
does not imply FIFO or a global backlog-empty assertion. Recovery tests use real PostgreSQL;
they do not certify arbitrary providers. Consumer process proofs described below cover the
selected PostgreSQL/RabbitMQ host topology.

JSONB round trips preserve JSON values rather than original lexical form. The immutable
envelope owns a JsonElement clone. Consumers may use direct native JSON or independently
adopt Events.Serialization for names, schemas and upcasting. The dispatcher neither infers
CLR types nor upgrades queued payloads. Reader-before-writer rollout and payload meaning stay
consumer-owned; event history and delivery lifecycle remain different records.

Optional TenantKey lets consumers reuse Persistence's ownership/filter/save utilities.
Tenancy admission is not supplied or mandatory. Carrying an organization in JSON or a header
does not authorize access; receiver code must validate the trusted producer/tenant mapping.
Dispatch is a privileged module-table operation, with no per-message tenant rebinding.

The optional sequential worker only invokes dispatch in fresh scopes, logs operational
failures and delays/cancels cooperatively. Transport configuration, route declarations,
connection lifetime, confirms/acknowledgements, startup/setup, scaling and permissions remain
consumer-owned. The RabbitMQ sample uses persistent publication, confirmation tracking and
mandatory routing; HTTP demonstrates a different documented acceptance condition.
Neither sample implies successful downstream business completion.

## Contract terminology and integration obligations

The packages generate XML documentation alongside their assemblies, including envelope
fields, operation outcomes, timing options and registration responsibilities.

- RouteKey is a logical routing key chosen by the producer. IMessagePublisher maps it to
  native transport targets: the HTTP sample maps exports.render to its configured receiver;
  Inventory maps inventory.stock-issues to its RabbitMQ route. No router or topology manager
  is supplied. MessageName and SchemaVersion identify the wire payload contract independently
  of routing; a CLR class name is not automatically the wire name.
- TenantKey is optional opaque tenant metadata. Inventory uses its admitted Organization
  key as a tenant key and composes the existing persistence ownership guard. The independent
  HTTP adopter omits it. The messaging package itself does not interpret it, admit tenants,
  bind execution context or include it in the outbox primary key.
- NoWork means no row was currently claimable, not an empty backlog. Published means transport
  acceptance and a committed outbox completion. ClaimLost means publication returned normally
  but the claim expired or changed before completion could be recorded. Failures throw;
  successful dispatch does not prove downstream business processing.
- OutboxCompletionException means the publisher returned normally but completion was not
  confirmed. MessageId and the original InnerException identify the uncertain operation.
  The optional worker faults without another poll; requested shutdown ends normally.
  Completion may already have committed. Failure policy/accounting is never invoked for it;
  direct callers own supervision and fresh-state inspection. ClaimLost stays an ordinary result.
- ValidateOutboxChanges is an explicit tracked-save guard. Native transaction atomicity and
  dispatcher claim fencing do not depend on calling it. However, omitting it removes protection
  against tracked edits/deletes, envelope changes and saving new work in a different transaction.
  It is required wiring for the documented tracked-write contract, not an automatic interceptor.
- The provided record contains only durable data. An internal weak registry attaches the
  original envelope/transaction and context lease to each enqueued row instance for validation.
  Evidence survives failed saves and same-context detach/reattach; a different context or later
  pool lease cannot adopt it. The weak table does not itself keep row objects alive. There is
  no new registration, save callback or immediate cleanup guarantee.
- Producer/dispatcher constructors inspect native EF model metadata; this may build/cache the
  model but does not query the database, open a connection or require applied migrations.
  Register/configure the context first. Dispatch checks transaction/change-tracking conditions
  at operation time and discovers database/schema availability through its actual SQL.

The samples show application responsibilities: ExportRequestCommands handles an ordinary EF command;
StockPositionCommands handles Inventory commands through its event store; StockIssueMessages
maps an accepted decision to one integration envelope. None is a library-provided handler.

OutgoingMessage.FromPayload<TPayload> accepts explicit JsonSerializerOptions and returns a
non-generic immutable envelope. Serialization captures the value at construction; subsequent
payload changes do not change queued JSON. Already serialized payloads continue using the
constructor directly. No payload generic argument propagates into EF, dispatch or publishers.
The CLR members RouteKey and TenantKey map to the existing destination/owner_key SQL columns.

A publisher may implement a database handoff as its transport. It must commit receiving intake
before returning acceptance; the sender can still fail to record completion and repeat delivery.
Receiving deduplication remains necessary. No database-handoff runtime is provided by this capability.

## Durable inbox intake and processing

The owner approved a retained incoming queue with independently selected tables/registration:

1. Consumer receives a complete integration envelope, validates its trusted origin and
   persists a unique intake identity/payload in the receiving module's native transaction.
2. After that commit, acknowledge the broker. Commit-before-ack failure can redeliver; intake
   recognizes the retained identity and does not queue duplicate work.
3. Callable processing and an optional worker invoke explicitly registered handlers in fresh
   scopes. Local business effects, processing completion and newly produced outbox work must
   commit together. The processor holds a native row lock through bounded local
   processing rather than using an expiring claim; external effects are outgoing work.

The receiving module owns its context and commit independently of the sender. A native
RabbitMQ two-module sample proves rollback without ack and committed intake before ack
failure. PostgreSQL and consumer proofs additionally cover duplicate/racing intake, handler
failure/recovery, connection-loss rollback and fresh-context processing. This becomes a useful service-splitting pattern without a distributed transaction.
A local durable handoff adapter can be compared later.

Failure accounting faults now have a distinct InboxFailureAccountingException carrying delivery
identity and both errors. The optional inbox worker faults without repeatedly selecting the
same delivery; native host supervision and consumer repair/restart policy govern recovery.
This contains an accounting-fault loop, but does not guarantee progress while that worker is
stopped or count unrecorded crashes. See [failure accounting and supervision](inbox-recovery.md).

Context-typed IInboxDeadLetters supplies bounded metadata list/detail and conditional
retry. Native timestamp/UUID keyset paging reserves no work and observes live state.
Rendering operator endpoints and restricted Sales tests exercise admission and storage.
No operator console or payload editor is supplied.

MessageId identifies one delivery and must remain stable on retry. Define deduplication
scope explicitly: receiving module/handler identity matters when one delivery fans out.
CorrelationId groups a workflow/conversation; CausationId identifies its immediate cause.
They remain useful, but multiple legitimate messages can share them. They must not deduplicate
different deliveries. Semantic operation IDs/fingerprints are another consumer-owned policy.
Reused delivery identity with incompatible content requires an explicit conflict decision;
never silently treat it as an equivalent duplicate. The current Inventory adapter illustrates
correlation by stock position; it does not invent causation when no incoming command ID exists.

Acknowledgement is transport-specific
and belongs after the applicable database commit. Broker tags/channels and native failure
semantics remain visible in the sample. Additional helpers must earn their interface.

## Separate worker composition

[The native worker example](../samples/Delivery/README.md) composes the existing library
workers outside an [API-only producer](../samples/Delivery/README.md).
Explicit setup applies the existing module migrations and declares the durable queue; worker
startup only checks its required tables/queue. Dispatch, broker intake and local processing
are separate roles. Processing needs only its receiving module's connection. The host owns
native connection/channel lifetime and each operation keeps the library's fresh DI scope.

Child-process tests cover producer exit, retained intake before processing, competing
transactions, shutdown/restart, processing rollback and publication death/lease expiry/redelivery.
They exercise the selected composition, not external exactly-once effects, throughput or an
external deployment supervisor. Transport intake remains consumer code.

The [recovery/shutdown contract](worker-recovery.md) describes terminal-session observation
and fresh-process restart. The worker disables automatic RabbitMQ recovery; ordinary publisher
exceptions against an open session retain existing retry. Native RPC/close timeouts and test
process budgets do not bound arbitrary publisher or exporter code.

Optional [RabbitMQ integration](../Gloaming.Messaging.RabbitMQ/README.md) owns selected
native resources, cleanup and terminal observation. It returns IChannel without Generic Host,
Messaging core or EF dependencies. Consumers select fail-stop or native recovery and retain
their own routes, lifetimes and restart policy.

## Current observability and workflow boundaries

Host placement is consumer composition. Optional retained W3C context, native attempt
activities, correlated failure logs and metrics are supported; see [observability](observability.md).
[Outbox backlog observation](outbox-observability.md) is independently callable; alert thresholds
and dashboard UX remain consumer policy.

Dispatch ordering and retained delivery rows supply no safe replay cursor or snapshot boundary.
The separately selected [EventSourcing PostgreSQL module](../../Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore.Postgres/README.md)
provides a fixed-source committed feed and atomic secondary-projection progress. Whole-service
bootstrap, business import and receiver-specific integration remain consumer-owned.

Durable workflows begin with module-owned state and the existing messaging contracts. The
[Stock-issue consumer](../../../samples/Wholesale/Worker/Broker/README.md) demonstrates
persisted request/reply progress, native EF request concurrency, semantic idempotency and
overdue-state recovery. These are consumer policies composed with inbox/outbox, not a supplied
saga engine or scheduler; see its separate dated report for the proven behavior.

[Reservation saga](../../../samples/Wholesale/Worker/Broker/compensating-reservations.md)
adds sequential steps and an actual compensating release across separate module commits.
The optional [durable continuation runner](workflows.md) now owns provided process storage,
requested IDs, semantic reply matching, selected-method execution and atomic request enqueue.
Ordinary consumer methods own branching, compensation and business state. Native transactions
and existing inbox/outbox workers remain explicit. No workflow worker, scheduler or replay engine
is supplied. The independent tenantless workspace adopter takes a different existing-resource
branch and does not release resources it did not create.

## Deferred capabilities

The schema compatibility guide proves one consumer evolution. Different contract changes and
deployment/rollback topologies require their own evidence; no rollout orchestrator is supplied.

Receipt-only mode if an adopter needs it;
permanent inbox duplicate registries, dead-letter discard/purge and supplied cleanup scheduling;
caps on executions including crashes, lost leases and ambiguous completion; lease renewal; parallel/batch dispatch;
per-stream ordering; other DBMS providers; additional transports and a generic local-handoff runtime;
encryption/secrets; baggage/initiator propagation; generic workflow/timeout orchestration; cross-module
transactions. Event-sourcing maintenance/async projections remain
in their owning family. These capabilities need specific failure/consumer evidence, not only
more interfaces. There is no required Gloaming root runtime.
