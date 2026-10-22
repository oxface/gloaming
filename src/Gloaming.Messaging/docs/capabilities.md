# Messaging capabilities and remaining work

O1 supplies one bounded transactional-outbox capability. [Family setup](../README.md)
and package READMEs define the public surface; [the O1 proposal](../../../docs/plans/outbox1-transactional-dispatch.md)
records owner review and [the report](../../../docs/reports/outbox1-transactional-dispatch.md)
records dated executions. O1 is owner-approved at checkpoint `2d7a865` and merged as
`beafa4e`. I1 adds independently selected durable intake and separate transactional processing.
Its [interface/scope](../../../docs/plans/inbox1-durable-intake-processing.md) is owner-approved;
the implementation is checkpointed as `ecab884`, refined as `863456b` and merged as `0f4d8bf`.
[Inbox setup and guarantees](inbox.md) and
[the fresh execution report](../../../docs/reports/inbox1-durable-intake-processing.md) describe the current contract.
M2 adds [durable failure accounting and operator retry](inbox-recovery.md) after owner
interface review, with [separate current evidence](../../../docs/reports/m2-inbox-failure-recovery.md).
Its [outbox continuation](outbox-recovery.md) adds lease-fenced publisher-exception accounting,
consumer-selected dead letters and bounded inspection/conditional retry, with
[new PostgreSQL/HTTP/process evidence](../../../docs/reports/m2-outbox-dead-letters.md).
The [completion supervision follow-up](../../../docs/reports/m2-outbox-completion-supervision.md)
adds OutboxCompletionException and stops the optional worker on unconfirmed completion.
Native host options, consumer repair and external restart policy govern recovery.
The [operational visibility slice](outbox-observability.md) adds a separately callable
PostgreSQL route snapshot. Native sample gauges and health checks keep sampling, readiness
policy and alert thresholds with the consumer; no new recovery or health framework is supplied.
Separately authorized [completed-outbox retention](outbox-retention.md) now provides one
bounded PostgreSQL deletion batch. Evidence windows and unique identities after deletion
remain consumer obligations; inbox retention is independent.
M2's [processed-inbox retention](inbox-retention.md) supplies exact-lane bounded cleanup and
receipt-generation-aware operator retry. Deletion ends duplicate/content evidence and permits
old delivery to execute again. Unprocessed/dead-lettered work remains. Consumer windows and
scheduling are explicit. The owner approved retention's documented post-v1 supplied-feature
deferrals with checkpoint 6510f9e, merged as 2e1ffac. The selected
[broker recovery/shutdown continuation](../../../docs/reports/w1-broker-recovery-shutdown.md)
has its own implementation evidence and change-set review.

[Queued-message schema compatibility](schema-compatibility.md) demonstrates retained v1 work
and explicitly selected v2 writes through independent consumer contexts and HTTP/RabbitMQ.
Rendering opts into the existing JSON codec; Messaging packages gain no serializer dependency.
Original-envelope deduplication, conditional retry and the outbox's acceptance uncertainty remain.

## Current composition

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
connection/transaction setup to coordinate; O1 supports one owning producer context.

Dispatch commits a lease before transport work and fences completion/retry with its token
and expiry. Expiry may allow a successor to publish while the old publisher is still running;
fencing protects database completion, not external exactly-once effects. Best-effort ordering
does not imply FIFO or a global backlog-empty assertion. Recovery tests use real PostgreSQL;
they do not certify arbitrary providers. O1's original execution report supplies no abrupt
process-kill claim; the W1 consumer process proofs described below cover the selected
PostgreSQL/RabbitMQ host topology.

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
Receiving deduplication remains necessary. No database-handoff runtime is provided in O1.

## Durable inbox intake and processing

The owner approved a retained incoming queue with independently selected tables/registration:

1. Consumer receives a complete integration envelope, validates its trusted origin and
   persists a unique intake identity/payload in the receiving module's native transaction.
2. After that commit, acknowledge the broker. Commit-before-ack failure can redeliver; intake
   recognizes the retained identity and does not queue duplicate work.
3. Callable processing and an optional worker invoke explicitly registered handlers in fresh
   scopes. Local business effects, processing completion and newly produced outbox work must
   commit together. I1 holds a native row lock through bounded local
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

The current context-typed IInboxDeadLetters concentrates bounded metadata list/detail and
the existing conditional retry. Native timestamp/UUID keyset paging reserves no work and
observes live state. Rendering's authenticated operator endpoints and restricted Sales proofs
exercise the interface; [the dead-letter report](../../../docs/reports/m2-inbox-dead-letters.md)
records fresh paging, authority and checkpoint-migration evidence. No console or payload editor.

MessageId identifies one delivery and must remain stable on retry. Define deduplication
scope explicitly: receiving module/handler identity matters when one delivery fans out.
CorrelationId groups a workflow/conversation; CausationId identifies its immediate cause.
They remain useful, but multiple legitimate messages can share them. They must not deduplicate
different deliveries. Semantic operation IDs/fingerprints are another consumer-owned policy.
Reused delivery identity with incompatible content requires an explicit conflict decision;
never silently treat it as an equivalent duplicate. The current Inventory adapter illustrates
correlation by stock position; it does not invent causation when no incoming command ID exists.

The archived inbox records a processed receipt with business effects; it is historical
evidence for the simpler model, not proof of queued intake/processing. Do not implement both
modes automatically. No generic acknowledgement facade is selected: ack is transport-specific
and belongs after the applicable database commit. Broker tags/channels and native failure
semantics remain visible in the sample. Additional helpers must earn their interface.

## Separate worker composition

[W1's native host](../../../samples/MessagingWorkerDemo/README.md) composes the existing library
workers outside an [API-only producer](../../../samples/MessagingProducerDemo/README.md).
Explicit setup applies the existing module migrations and declares the durable queue; worker
startup only checks its required tables/queue. Dispatch, broker intake and local processing
are separate roles. Processing needs only its receiving module's connection. The host owns
native connection/channel lifetime and each operation keeps the library's fresh DI scope.

Real child-process proofs cover API exit before delivery, acknowledged retained work before
independent processing, competing processor transactions, graceful SIGTERM/restart, processing
death/rollback and publication death/lease expiry/redelivery/deduplication. This establishes
the selected composition, not external exactly-once effects, a throughput benchmark or a
deployment supervisor. Native intake remains sample code. Original W1 introduced no new
library interface; existing typed composition, leases and row locks sufficed for that slice.
[The W1 report](../../../docs/reports/w1-separate-worker-hosts.md) distinguishes these executions
from historical O1/I1 results.

The [worker recovery/shutdown contract](worker-recovery.md) adds consumer-owned native
terminal-session observation and supervised fresh-process restart. Automatic RabbitMQ recovery
stays disabled; ordinary publisher exceptions against an open session still use existing retry.
Live broker outage/recovery, retained-message progress, actual blocked publication/SIGTERM and
the existing unavailable-collector stop scenario have their own
[closure evidence](../../../docs/reports/w1-broker-recovery-shutdown.md). Native attempt/RPC/close
timeouts and selected process verification budgets are distinct; no arbitrary publisher or
exporter deadline or persistence protocol is added.

Optional [Gloaming.Messaging.RabbitMQ](../Gloaming.Messaging.RabbitMQ/README.md) subsequently
extracts native session/channel ownership, acquired-resource cleanup, phase-timeout setup and
one-time terminal observation. It returns native IChannel and uses an intentional-stop token/
callback; no Generic Host/core/EF dependency or parallel publisher/recovery interface. The worker
chooses fail-stop; finite and three-channel workflow adopters share cleanup while retaining their
own lifetimes/native recovery. [Fresh extraction evidence](../../../docs/reports/rabbitmq-session-lifecycle.md)
is separate from the original consumer-only proof.

## Current observability and workflow boundaries

Host placement is consumer composition. Optional retained W3C context, native attempt
activities, correlated failure logs and metrics are supported; see [observability](observability.md).
Backlog observation, alert thresholds and dashboard UX remain deferred.

New-service snapshot/feed repopulation is a distinct planned capability. Current dispatch
ordering and retained delivery rows do not provide a safe replay cursor or snapshot boundary.
A producer-owned replay/export contract, retention rules and transactional receiver progress
need separate proof; no bootstrap runtime is supplied. The owner closed B1 as the
[retention/contract assessment](../../../docs/reports/b1-retention-and-contract-assessment.md)
and rejected retaining the experiment code, tests, migration and reconstruction patch.
Only research and [dated results](../../../docs/reports/b1-replayable-integration-feed.md)
remain. Current Sales writes and setup have no feed dependency; no supported Messaging API
or maintained bootstrap example was added. Neither generic state revisions nor historical
replay follow from the existing envelope/outbox. Revisit implementation only for a concrete
receiver need; B2 snapshot/bootstrap remains separate.

Durable workflows begin with module-owned state and the existing messaging contracts. The
[WF1 stock-issue consumer](../../../samples/Wholesale/WorkflowDemo/README.md) demonstrates
persisted request/reply progress, native EF request concurrency, semantic idempotency and
overdue-state recovery. These are consumer policies composed with inbox/outbox, not a supplied
saga engine or scheduler; see its separate dated report for the proven behavior.

[WF2's reservation saga](../../../samples/Wholesale/WorkflowDemo/compensating-reservations.md)
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
per-stream ordering; other DBMS providers; additional transports and local module handoff;
encryption/secrets; baggage/initiator propagation; generic workflow/timeout orchestration; cross-module
transactions; template messaging presets. Event-sourcing maintenance/async projections remain
in their owning family. These capabilities need specific failure/consumer evidence, not only
more interfaces. There is no required Gloaming root runtime.
