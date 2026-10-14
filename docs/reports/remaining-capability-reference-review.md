# Remaining capability reference review

Date: 2026-10-09. Research only; no implementation or compatibility claim.
Primary documentation was read as served on this date. Marten and Wolverine documentation
contains version-specific and evolving behavior; it is reference evidence, not validation
against this repository's dependency versions. No new reusable mechanism was proven.

## Decision context

The [design](../design.md) and [extraction plan](../plans/library-extraction.md) keep ordinary
.NET adoption, native transactions, explicit setup and consumer-owned policy. The existing
[durable-message observability proposal](../plans/durable-message-observability.md) is still a
proposal. This review provides external evidence for bounded follow-ups; it neither approves
that interface nor changes the currently supported library contracts. Archived sample proofs
remain historical evidence and were not rerun.

The useful distinction is between a small mechanism that removes repeated correctness work
and a sample/template recipe that makes application choices visible. An external framework's
capability catalog does not itself demonstrate a Gloaming extraction candidate.

## DDD and CQRS: start with executable consumer code

**Source facts.** Microsoft's CQRS guidance includes separate read/write models using the
same database. Commands own validation/domain decisions and queries return purpose-built
data; splitting storage adds synchronization and consistency obligations. Its DDD guidance
explicitly allows simpler CRUD implementations for less complex domains.
[Microsoft CQRS](https://learn.microsoft.com/en-us/azure/architecture/patterns/cqrs),
[Microsoft DDD guidance](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/ddd-oriented-microservice).

**Inference for Gloaming.** Command handlers, direct EF projections, domain values and result
types can start as sample/template code. CQRS alone supplies no reason for a mediator, handler
registry, generic repository, mandatory aggregate inheritance or another unit-of-work layer.
A pending-event or value helper earns extraction when two concrete consumers expose repeated
bookkeeping or a repeated invariant that it can remove. Existing aggregate bookkeeping is
current local evidence; general DDD utilities remain separate candidates.

**Bounded next proof.** Show one state-stored command/query pair and one event-sourced pair
using explicit Contracts and native queries. Review actual duplicated code before proposing
another public interface. Domain eligibility, cross-module orchestration, transaction ownership
and error presentation remain consumer policy.

## Durable messaging: retain context without conflating identities

**Source facts.** .NET tracing uses `Activity`; W3C identifiers distinguish the whole trace
from each span. `Activity.Current` flows through in-process asynchronous calls, while crossing
processes requires propagation. Sampling can omit recording. Native HTTP instrumentation
does not establish durable storage or arbitrary broker propagation.
[.NET distributed tracing](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/distributed-tracing-concepts).

W3C `traceparent` contains version, trace ID, parent/span ID and flags; `tracestate` carries
additional tracing state. A trace ID alone therefore cannot reconstruct the caller's span
context. These fields are diagnostic context.
[W3C Trace Context](https://www.w3.org/TR/trace-context/).

OpenTelemetry messaging conventions recommend attaching producer creation context and linking
consumer processing to it. Links accommodate batches and another ambient execution context;
using creation context as the parent is an allowed single-message alternative. The conventions
distinguish creation, sending, processing and settlement. Messaging spans are currently marked
Development, and attachment/extraction details remain transport-specific.
[OpenTelemetry messaging spans](https://opentelemetry.io/docs/specs/semconv/messaging/messaging-spans/).

Wolverine's envelope distinguishes `Id` for a message, `CorrelationId` for related workflow
actions, and `ConversationId` for the immediate cause. Its documented correlation default can
come from the current Activity root and can be overridden. Wolverine also supplies native
logging and an ActivitySource. That default is a framework policy, not a requirement to make
business correlation equal to tracing identity.
[Wolverine instrumentation and correlation](https://wolverinefx.net/guide/logging.html#message-correlation).

**Inference for Gloaming.** Retained optional W3C context and native callable-path
instrumentation are small candidates because an exited producer and a fresh worker lose
ambient process context. Keep these roles distinct:

| Identifier | Role to review |
| --- | --- |
| MessageId | Stable identity of a delivery/envelope under retries; used within the explicitly selected deduplication scope. |
| CorrelationId | Consumer-selected conversation or business workflow grouping. |
| CausationId | Immediate message/operation that caused a derived message. |
| TraceId and span context | Diagnostic trace and execution relationships, potentially sampled or restarted. |

That table is a proposed design interpretation consistent with current messaging terminology,
not a universal transport contract. Correlation/cause do not replace delivery deduplication.
Tracing context does not grant tenant admission, actor authority or business idempotency.

**Bounded next proof.** Exercise the existing inbox/outbox paths in separate processes:
producer exits before dispatch, intake exits before processing, one retry and one restart.
Assert actual span relationships and committed effects; retain stable business identifiers
while attempts get distinct spans. Review absent, malformed and unrecorded diagnostic context,
duplicate handling and additive migrations. Host source subscriptions/exporters and broker
mapping stay sample/template policy. Do not introduce a tracing context runtime or exporter
dependency into the envelope merely to wrap native APIs.

## New-service bootstrap: snapshot plus a provable change boundary

**Source facts.** PostgreSQL logical decoding can export a snapshot when creating a logical
replication slot. That snapshot represents the state after which changes appear in the stream;
using it for the initial copy and then applying slot contents avoids losing intervening changes.
Logical slots can resend recent changes after a crash, so clients must handle duplicates.
[PostgreSQL logical decoding concepts](https://www.postgresql.org/docs/current/logicaldecoding-explanation.html).

Marten's daemon distinguishes each projection's progress from a safe high-water mark. The
current documentation's version-labelled 9.23 section describes holding below outstanding
sequence holes and proving abandoned gaps before advancing. The same page describes older
stale-gap behavior; those versions must not be assigned the newer guarantee. `Solo` assumes
one process; `HotCold` coordinates projection ownership.
[Marten async daemon](https://martendb.io/events/projections/async-daemon.html).

PostgreSQL sequences advance outside transaction rollback, while an ordinary Read Committed
query sees rows committed before that query began. `CURRENT_TIMESTAMP`/`now()` represent
transaction start time; `clock_timestamp()` represents invocation time rather than commit.
[PostgreSQL isolation](https://www.postgresql.org/docs/current/transaction-iso.html),
[PostgreSQL time functions](https://www.postgresql.org/docs/current/functions-datetime.html).

**Inference for Gloaming.** A timestamp-only feed is unsafe: an earlier-stamped transaction
can commit after a reader has advanced its timestamp, and equal timestamps need deterministic
tie handling. Adding an ID tie-breaker fixes ties without proving that earlier invisible rows
cannot later commit. A global allocated sequence or `MAX(position)` has the same late-commit
problem. A single-stream committed version does not establish a multi-stream cursor.

A useful small outcome starts with one source-owned public bootstrap/feed contract and one
new receiving service. It must specify:

1. A consistent initial snapshot associated with an opaque source cursor, or another explicitly
   proven snapshot/change overlap protocol.
2. Retained changes from that boundary, stable identities and deterministic ordering, including
   deletes/tombstones and schema versions when the consumer needs them.
3. Receiver application of a page and checkpoint in one local transaction; replay after crash
   must be safe. Retrying a partially completed initial copy must also have defined semantics.
4. A retention floor and explicit expired-cursor/resnapshot result. A bounded overlap window
   is valid only with a proven bound on late commits and retention; time alone supplies none.
5. Failure proofs for concurrent writes during the copy, late commits below an observed maximum,
   aborted gaps, duplicates, interrupted pages and restart.

The PostgreSQL slot protocol is a concrete correctness reference, not a decision to ship CDC.
A serialized source publication cursor, replayable committed feed or CDC adapter are alternatives
requiring separate design. There is currently no evidence for a generic snapshot engine,
global event-feed service or universal cursor abstraction. Producer-owned export DTOs and
admission policy must not expose internal module rows or DbContexts to the receiving service.

## Separate worker hosts: deployment is distinct from ownership

**Source facts.** .NET offers a Worker Service template and native host lifecycle. Hosted
services are singletons; no scope is created automatically, and Microsoft's example creates
an async scope for each unit of work.
[.NET scoped background work](https://learn.microsoft.com/en-us/dotnet/core/extensions/scoped-service).

PostgreSQL documents `SKIP LOCKED` as useful for multiple consumers of a queue-like table,
while warning that it gives an inconsistent general-purpose view. Wolverine separately uses
leader election to assign stateful agents, including durable recovery agents and exclusive
listeners, across nodes.
[PostgreSQL locking reads](https://www.postgresql.org/docs/current/sql-select.html#SQL-FOR-UPDATE-SHARE),
[Wolverine leader election and agents](https://wolverinefx.net/tutorials/leader-election).

**Inference for Gloaming.** A dedicated native worker host that calls an existing processor
or dispatcher is initially sample/template composition. It needs explicit registrations,
fresh scopes, service identity, trusted tenant establishment and shutdown behavior. Moving it
out of the HTTP host creates no distributed coordination guarantee.

Existing inbox transactional row ownership and outbox external-publication lease fencing
already solve different ownership problems. Competing workers consuming independent queue
rows do not inherently need a cluster leader. A singleton schedule/projection coordinator
may need another protocol, but a leader does not automatically fence stale external work.
Start with the existing callable contracts and a declared deployment mode. Add generic leases,
renewal, partition assignment or leader election only for a concrete unmet requirement and
their own takeover/stale-owner/restart proofs.

## Durable process managers: prove one workflow before an engine

**Source facts.** Wolverine sagas persist identity-bearing state between messages, bind messages
to that state and support completion and scheduled timeout messages. Its Marten/EF support
includes optimistic concurrency; its documentation distinguishes local partitioned processing
from concurrency protection needed across nodes. Wolverine owns the handler/persistence/runtime
integration for those features.
[Wolverine sagas](https://wolverinefx.net/guide/durability/sagas.html).

**Inference for Gloaming.** A first durable Sales/Purchasing process manager can be ordinary
module-owned state with explicit transition handlers, inbox processing and outgoing work in
one owning transaction. Test duplicate/reordered replies, concurrent transitions, restart,
timeout-versus-success races and ambiguous remote outcomes. Compensation, terminal states,
deadlines and operator decisions are business rules. Persisting state alone is insufficient
to claim recovery of scheduled work. Extract repeated persistence or deadline-delivery
mechanics only after comparison; defer a saga DSL, registry, scheduler and workflow engine.

## Module-specific DbContext isolation

**Source facts.** EF recommends typed `DbContextOptions<TContext>` so DI resolves the correct
configuration when multiple context types are registered. DbContext is a short-lived unit of
work and is not thread-safe. Shared relational transactions require sharing both connection
and transaction explicitly.
[EF context configuration](https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/),
[EF cross-context transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions#cross-context-transaction).

Wolverine's current EF middleware documentation requires explicit transactional-context
selection when more than one context-shaped dependency is present; aliases for the same
context must resolve to the same scoped instance. This demonstrates that transaction owner
selection remains a real obligation even in a larger framework.
[Wolverine EF transactional middleware](https://wolverinefx.net/guide/durability/efcore/transactional-middleware.html).

**Inference for Gloaming.** Preserve typed module registrations, mappings, migration histories
and callable workers. Cross-module consistency is an explicit separate transaction/protocol
decision. Schemas and separate CLR contexts express ownership; they do not establish database
authorization by themselves. Additional isolation utilities need a demonstrated repeated
failure rather than a global DbContext locator, common context base or implicit shared commit.

## Outcome, verification and remaining gaps

The review supports small observability metadata/instrumentation candidates, a separate native
worker composition and a consumer-owned durable workflow. Bootstrap/feed work remains a useful
capability but requires a concrete source ordering/boundary design before a library interface.
DDD/CQRS examples and module setup primarily belong in editable sample/template code. A generic
projection, snapshot, leader-election or saga engine is not justified by these references.

Only this report was added. Sources were checked through primary documentation; no new runtime
tests, compatibility checks, benchmark or deployed recovery proof ran. No new reusable mechanism
was proven. Library interfaces, transport mappings, mixed-version rollout, retention and actual
consumer guarantees still require their own executable slice and owner review. No index,
branch, commit or configuration changes were made by this research task.
