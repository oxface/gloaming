# O1: Module-owned transactional outbox and recoverable dispatch

Date: 2026-10-08. Base `0740cd3`; branch `codex/transactional-outbox`.
Owner authorized the revised interface/scope before implementation. The initial handoff was
unstaged for line-by-line review; this agent has not committed, pushed or merged this slice.
[Scope/interface history](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/outbox1-transactional-dispatch.md),
[ADR 0009](../adr/0009-module-owned-transactional-outbox.md),
[current consumer contract](../../src/Gloaming.Messaging/docs/capabilities.md).

## Outcome and mechanism concentrated

Three used layers implement one bounded capability: immutable outgoing envelope/publication
contract; native EF record/mapping/enqueue/save validation and optional sequential worker;
PostgreSQL model specialization and private claim/completion/retry SQL. A producer enqueues
deliberately in its business transaction. A separately callable dispatcher commits a claim,
publishes without that database transaction, and uses token/expiry predicates for completion
or retry eligibility. Claims, prepared handles, transport discovery and a global DbContext
are not public protocols. The worker invokes this same operation in fresh scopes.

Deleting the extraction would restore repeated row lifecycle, database claim/token fencing,
retry/cancellation recovery and publisher/context binding in each adopter. This is a new
reusable mechanism, rather than just an enqueue convenience method. The original archive
contains comparable Inventory, Sales and Purchasing SQL relays; those were inspected as
historical evidence, not modified or rerun. The new tests exercise the public interfaces and
actual consumer decisions, rather than asserting code resembles the archive.

The EF layer contains used functionality and has no Npgsql dependency. PostgreSQL owns
JSONB, database-time defaults, partial pending index and native SKIP LOCKED SQL. The middle
package does not certify another provider or introduce a generic dialect/repository. Business
Contracts remain technical-library-free. No Gloaming root runtime or compulsory dependency
on Events, EventSourcing, Persistence, Tenancy or ActorIdentity is added.

## Executable adoption and consumer-owned policy

Inventory's existing state-dependent issue decision explicitly maps an accepted command
batch to one StockIssueRecordedV1. Final native save/commit covers events, stream/main state
and that notification. Rejection emits neither facts nor outgoing work; reconstruction of
old facts emits no new notification. Existing command signatures, reducers, domain events,
fixtures, old migrations and default demo output remain. A new forward migration adds only
inventory.outbox_messages. Normal default journeys need no broker/worker to enqueue work.

The explicit --outbox journey owns native RabbitMQ topology, channel/connection and the
publisher. Persistent mandatory publication waits for confirmation tracking; unroutable
returns propagate. The receiving test observer manually acknowledges transport delivery;
it does not represent durable receiver intake/business effects. No transport runtime was
promoted into Gloaming, and no archived broker/topology suite was reactivated.

The independent OutboxDemo adopts ordinary versioned EF state and direct JSON. A draft export
with pages may be submitted once, staging RenderExportV1 in the caller's transaction.
Expected version/native concurrency reject stale competing submissions; empty/already
submitted drafts enqueue nothing. Its HTTP publisher has a different native acceptance
condition and no event/context/module-layout dependency. Tests use actual Kestrel HTTP.

Routing, integration DTOs, eligibility, wire JSON/schema policy, semantic operation identity,
tenant admission, final save/commit, whole-operation retry, transport lifecycle and permissions
remain consumer policy. Optional library workers do not create schemas, discover modules,
publish from SaveChanges or establish trusted tenant contexts. Publisher/options/worker
bindings are per typed module context. Privileged dispatch drains that module table across
owners through technical SQL; ordinary EF owner-filtered reads remain independently protected.

## Newly executed proof results

The final supported assertions were exercised locally with .NET SDK 10.0.112/runtime 10.0.12,
EF 10.0.12, Npgsql EF 10.0.3, RabbitMQ.Client 7.2.1, PostgreSQL 18.6 and RabbitMQ 4.3.6 through
Podman's Docker-compatible socket. No hosted CI result for this uncommitted branch is claimed.

| Check | Passing cases |
| --- | ---: |
| Messaging envelope tests | 3 |
| PostgreSQL outbox producer/dispatch/registration/worker | 23 |
| Independent state-stored HTTP adopter | 5 |
| Wholesale non-broker event/adoption suite | 172 |
| Focused Wholesale RabbitMQ class | 3 |
| Architecture/dependency/module boundaries | 71 |
| Existing EventSourcing core / PostgreSQL | 11 / 67 |
| Existing Serialization/upcasting / History | 49 / 16 |
| Existing EF ownership model / PostgreSQL consumer | 23 / 6 |
| Existing Wholesale HTTP / persistence composition | 97 / 36 |
| Two external generated consumers, PostgreSQL | 5 each |

582 unique active test cases across 13 projects passed, plus ten generated-consumer cases.
Repeated development runs are not counted twice. The two complementary xUnit class filters
used by CI were executed: 172 non-broker tests stay in EventSourcing, three broker tests run
in the new Messaging lane. The full Wholesale suite had 175 discovered cases. Initial
assertion failures were corrected to expect semantic JSONB preservation and native Npgsql,
RabbitMQ and event-position conflict outcomes; no supported assertion was removed.

Important new proofs:

- Uncommitted work is not visible/dispatchable. Commit/rollback at ReadCommitted,
  RepeatableRead and Serializable preserve native producer isolation and all local writes.
  Cancellation or later outbox failure rolls back earlier business SQL; fresh contexts recover.
- Competing state-stored/event-sourced decisions commit one winner and its outgoing work.
  Native predicates/constraints arbitrate writes; serialization conflicts propagate unchanged.
  Every included Inventory participant has an injected SQL-failure/rollback proof.
- JSON payloads outlive source documents and survive fresh-context JSONB reads; optional
  tenantless adoption and existing trusted owner-filter/save policy both execute.
- PostgreSQL locked rows are skipped, active claims are exclusive, expired leases recover,
  and old successful/failed publishers cannot complete or defer a successor's claim. Expiry
  without takeover also prevents completion. Cancellation leaves recoverable abandoned claims.
- Publication errors retain retry eligibility on the database clock; publication plus retry
  SQL failure preserves both exceptions. Native transaction/pending-write overlap is rejected.
- Actual HTTP and RabbitMQ acceptance followed by completion failure produces repeat delivery
  with the same ID/content. Mandatory unroutable RabbitMQ publication remains incomplete and
  recovers after native topology repair. Acceptance is not claimed as completed business work.
- Two typed contexts with distinct quoted schema/table names, publishers/options and hosted
  workers do not cross-wire. Worker errors recover in new scopes; idle/in-flight shutdown is
  cooperative; missing model/dependency setup fails rather than spinning configuration retries.

Active solution restore/build, style/analyzer verification, CSharpier, actionlint and
git diff --check passed. The frozen archive verifier still matches all 800 original files.
Template TypeScript/type/format checks and complete creation/adoption proof passed: two names/
namespaces, deterministic generation, omitted events/messaging, native failure/refusal/races,
parent SDK isolation, local source references and real PostgreSQL journeys. No template source
or snapshot changed. Full Aspire/Keycloak/browser deployment was not rerun for this slice.

## Findings and limits

JSONB normalizes lexical representation. Tests compare its semantic JSON values to the
original input, and separately retain stable stored content/identity across retries. The
library does not promise original whitespace, key order or numeric spelling. Native JSON
payloads need no second registry; Inventory reuses the optional Events.Serialization codec.
Queued-schema rollout/upcasting remains a separate proof rather than automatic dispatcher behavior.

ReadCommitted is the command sample baseline, not a replacement for optimistic concurrency.
PostgreSQL serialization failure can arrive wrapped in Npgsql's InvalidOperationException
with DbUpdateException/PostgresException underneath. Native event-position uniqueness can
also report a losing append before its stream predicate. Consumer classification/retry policy
remains existing native code; the outbox does not swallow or retry these decisions.

At-least-once delivery needs continued dispatch, retained records and an accepting transport.
Expiry may permit overlapping publishers; fencing protects the completion/retry writes, not
external exactly-once effects. No global FIFO/concurrency cap, lease renewal, retention,
poison/attempt-cap policy or redrive is implemented. The pending index is configured, but no
throughput/large-backlog benchmark is claimed. Raw/bulk SQL, omitted save guards and external
writers remain outside tracked validation. Multiple module DbContexts do not acquire a shared
transaction implicitly. PostgreSQL is the only supported dispatch provider.

Crash recovery is proved through abandoned claims and actual acceptance-before-completion
failure with fresh-context recovery. This is not a new abrupt process-kill, broker-cluster,
deployment upgrade or universal transport-shutdown proof; archived crash tests remain historical.
The native sample adapters have explicit time bounds, while IMessagePublisher alone cannot
make non-cooperative code cancellable.

## Review-worthy files and next slice

Read the [family contract](../../src/Gloaming.Messaging/README.md), then core OutgoingMessage/
IMessagePublisher; EF IOutbox/IOutboxDispatcher/options, provided record, model/save guard,
enqueue and worker; PostgreSQL mapping, DI and dispatcher. Inspect
[consumer mapping](../../samples/Wholesale/modules/Inventory/Inventory/Messaging/StockIssueMessages.cs),
[accepted issue](../../samples/Wholesale/modules/Inventory/Inventory/StockPositions/StockPositionCommands.cs),
[native RabbitMQ adapter](../../samples/Wholesale/IntegrationTests/Scenarios/EventPersistence/InventoryRabbitMqPublisher.cs),
[independent commands](https://github.com/oxface/modulith-foundry/blob/56a8b12857d1cffb842d839238784c607b66e474/src/Gloaming.Messaging/samples/OutboxDemo/ExportRequestCommands.cs), new migration/snapshot
artifacts, public-interface tests and the CI/dependency rules. The exact path map remains in
the reviewed O1 brief. At the initial handoff there were no staged entries or unrelated edits.

Next is durable inbox intake and transactional processing, with independently selected
model/DI and callable operation before its optional worker. Establish a real receiving module
and native RabbitMQ commit-before-ack example, duplicate/racing intake, failure/redelivery,
handler rollback/recovery, stale completion and any outgoing response in its local transaction.
MessageId identifies delivery; correlation groups related work; causation identifies its
immediate cause. Neither correlation nor causation alone deduplicates a delivery. Current
Inventory correlation-by-stock-position is sample policy; it invents no missing command ID.
Deduplication scope/content conflicts, retention and receiving tenant admission must be
reviewed explicitly. No inbox runtime, generic ack facade, handler registry or durable
workflow was introduced by O1. The owner may revise this design after inspecting the code.

## Initial documentation review follow-up

Public Messaging types/members now explain routing, ownership metadata, dispatch outcomes,
timing, registration and native transaction obligations through XML comments. All three
packages emit compiler-checked XML documentation. Sample handler/envelope-factory roles and
the two internal unmapped enqueue-evidence references are documented. No public signatures
or runtime behavior changed, and no additional reusable mechanism was proven by this follow-up.

The full solution builds with zero warnings/errors; style, analyzers, CSharpier and
git diff --check pass. The existing runtime proofs above were not rerun for comments and
documentation output settings. Typed serialization and naming changes remain explicitly
unimplemented proposals at that point. Constructor metadata validation remains eager and
does not open a database connection.

The original slice was already staged when this review turn began. This agent preserved that
index; the documentation additions remain unstaged and no commit was made.

## Approved typed-construction and registry follow-up

The owner then authorized implementing these suggestions. OutgoingMessage.FromPayload<TPayload>
serializes with caller-supplied JsonSerializerOptions and returns the existing non-generic
envelope. The ordinary EF adopter executes it with its existing wire JSON naming policy;
Inventory retains its explicitly registered Events.Serialization codec. No new serializer
registry, generic publisher/worker or mandatory dependency was added.

RouteKey and optional TenantKey replace the less specific CLR names. ExportRequestCommands
communicates the ordinary EF application's command-handler role. Published/NoWork/ClaimLost
retain their documented meanings and numeric values. The existing destination/owner_key SQL
columns and migration operations remain; only the new migration target models/current snapshots
needed matching CLR-property changes. No old migration or archived file changed.

OutboxMessageRecord now contains only persistent data. The internal OutboxEnqueueRegistry
uses weak row-object keys to retain original envelope/transaction/context-lease evidence.
There is no extra DI registration, save-success callback or pool-reset subscription.
The implementation is 34 lines including comments; it replaces the record's transient
properties and changes enqueue/guard lookups. This supports the existing O1 save contract;
it does not establish another messaging capability or a performance improvement.

New real-PostgreSQL proofs confirm that an actual SQL save failure can retry the exact message
in the same transaction, and that a referenced detached row can return to its original context.
Another context cannot save it; an actually reused pooled context under a later lease rejects
abandoned work and can still enqueue fresh work. Earlier same-ID/different-row, mutation,
changed-transaction, rollback and cancellation proofs continue passing. Two new core cases
verify explicit typed JSON policy, capture before later payload mutation, and invalid inputs.
No forced-GC or memory benchmark is claimed.

The affected five projects passed 282 cases: Messaging 5, PostgreSQL outbox 26, independent
HTTP adoption 5, Wholesale event/RabbitMQ adoption 175 and architecture 71. This rerun is
distinct from the initial broader 582-case/ten-generated-consumer evidence above; unrelated
suites and template runtime creation were not repeated. Full solution build has zero warnings/
errors; style, analyzers, CSharpier (455 files), archive hashes (800 files) and git diff --check
pass. Both XML comments and family-local setup/limitations match the implemented interface.

These review changes remain unstaged. The pre-existing staged set was preserved; no commit,
push or merge was performed.
