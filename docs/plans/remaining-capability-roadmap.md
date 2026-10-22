# Remaining capability roadmap

Status: proposed scope and order, reconciled for A1/A1.1/A2/C1 on 2026-10-16. This roadmap does not approve interfaces; implemented slices have separate owner-reviewed scope.
The owner considers transactional audit complete; checkpoint `18a76f8` is present in
`origin/main`. Existing implementations and dated slice reports remain the evidence for
supported capabilities. The original E0–E10 extraction sequence is historical context,
not the current work queue.

W1 is owner-approved at checkpoint `7b43201`, merged into `origin/main` as `94ca57a`:
[native host and setup](../../src/Gloaming.Messaging/samples/MessagingWorkerDemo/README.md),
[bounded scope](w1-separate-worker-hosts.md) and
[fresh process evidence](../reports/w1-separate-worker-hosts.md). No library interface changes
were needed. OBS1 is owner-approved at checkpoint `b44b5d5`, merged into `origin/main`
at `4c0e0c5`.
[Fresh evidence](../reports/obs1-durable-message-observability.md) is separate from this roadmap;
remaining rows are candidates.

WF2 is merged into fetched `origin/main` at `d674e39` on the B1 starting base.
The current implementation is the [bounded continuation runner](../../src/Gloaming.Messaging/docs/workflows.md),
not the earlier sequential coordinator experiment. The owner closed B1 on 2026-10-14 as the
[retention and contract assessment](../reports/b1-retention-and-contract-assessment.md),
then rejected keeping its experimental code. Only [dated results](../reports/b1-replayable-integration-feed.md)
and findings remain; source, tests, migration, snapshot and patch artifacts are removed.
Common Wholesale has no feed dependency. Revisit storage/transfer choices only when a later
slice supplies a concrete receiver requirement. B2 implementation remains separate.

Read the actual consumers and owning family documentation before starting a slice. Resolve
stale descriptions against current code and owner decisions; do not carry an earlier
proposal into implementation merely because it appears in a plan. Each row below is a
candidate outcome, not authorization to build every associated abstraction. Split the
outcome further when its concrete interface/file proposal reveals independent work.

The [reference review](../reports/remaining-capability-reference-review.md) separates
primary-source findings from recommendations. Marten and Wolverine are behavioral
references, not selected runtime dependencies or a feature-parity target.

The owner clarified on 2026-10-22 that **M2 remains open** until its inbox-retention and
deduplication-expiry gap is resolved before v1, with explicit disposition of other M2 gaps.
Completed sub-slices below do not close the entire block. The
[reviewed scope](m2-inbox-retention.md) received implementation approval; the
[slice report](../reports/m2-inbox-retention.md) records current evidence. The owner approved
checkpoint 6510f9e and its five explicit post-v1 supplied-feature deferrals; retention merged
as 2e1ffac. The selected operating obligations below have their own closure/change-set review.

## V1 integration and gap disposition

The owner has authorized completion of the remaining work and all local implementation,
interface and commit decisions through one integration branch, stopping before push. The
fixed starting base is `790a8b7`. See the [execution ledger](v1-integration-completion.md).
Existing staged/unrelated work remains preserved; original historical approval rules apply
outside this explicitly authorized goal.

Inbox retention is merged at `2e1ffac`. W1/M2 broker recovery, W1/OBS1 shutdown and the optional
native RabbitMQ session lifecycle are merged at `790a8b7`. The [earlier source audit](../reports/v1-slice-closure-audit.md)
records how those gaps were found, not new outstanding work after these checkpoints.
WF2 deployment continuity, B2 population and P1 projection work have their current integration
dispositions in the ledger. [S1 consolidation](s1-sample-experiment-consolidation.md), selected
T2 generation and [archive retirement](../reports/s1-archive-retirement.md) are implemented
for final review. Independent code/P2 architecture review and release verification remain
required. Keep important gaps in their owning block until resolved;
post-v1 deferral needs explicit owner disposition. Completion is established by executable
proofs and the final review/requirement audit, not old report status or test counts.

## Work queue

On 2026-10-22 the owner selected [W1/M2 broker recovery and W1/OBS1 shutdown closure](w1-broker-recovery-shutdown.md)
after inbox-retention merge 2e1ffac. These are required operating/evidence obligations within
the existing block, with a [current-code assessment](../reports/w1-broker-recovery-shutdown-assessment.md)
and owner-approved concrete implementation scope. They are not covered by the owner-approved post-v1
retention/administration deferrals. No new transport framework or arbitrary publisher deadline
is selected. The [closure report](../reports/w1-broker-recovery-shutdown.md) records consumer
terminal-session fail-stop/restart, fresh native outage/shutdown evidence and reconciled
collector shutdown observations for review. WF2 definition deployment and T2/S1/readiness
remain separate audit obligations.

The owner subsequently selected a [RabbitMQ lifecycle extraction assessment](../reports/rabbitmq-session-lifecycle-assessment.md).
Its [owner-approved optional interface](rabbitmq-session-lifecycle.md) separates shared native
session/channel ownership and shutdown mechanics from consumer recovery/process policy.
This remains in the existing worker/messaging block. The
[extraction report](../reports/rabbitmq-session-lifecycle.md) records the implemented optional
package, three actual adopters and fresh public-session/native/process evidence for change-set
review. The earlier consumer-only result remains separate historical proof.

| Candidate | Bounded first outcome | Boundary and required evidence |
| --- | --- | --- |
| D1: DDD building blocks and template patterns | Committed as `041c64a`: [Sales/Purchasing executable comparison](../../samples/Wholesale/IntegrationTests/Scenarios/Ddd/README.md) demonstrates creation, domain values and accepted/rejected decisions; [scope](d1-ddd-sample-patterns.md) and [fresh proof/extraction report](../reports/d1-ddd-sample-patterns.md). | Domain rules stay in modules. Immutable Sales candidates need no event bookkeeping; Purchasing uses its existing reviewed aggregate. No new reusable mechanism, general aggregate base or template preset. |
| D1.1: rich state-stored aggregate | Owner-authorized [relational draft/line scope](d1-1-rich-state-stored-aggregate.md), with [executable usage](../../samples/Wholesale/IntegrationTests/Scenarios/RichAggregate/README.md) and a separate [proof/extraction report](../reports/d1-1-rich-state-stored-aggregate.md), checkpointed as `f55d803`. | Complete candidate acceptance, child add/change/remove, root-version concurrency and native rollback; no generic mapper, aggregate base or new reusable mechanism. |
| D2: local event reactions | Owner-approved [EventReactions consolidation](../reports/event-reactions-consolidation.md), checkpointed as `c4a96da`. | Composable buffers, marker-free facts, explicit EF sources and bounded ordered reactions preserve atomic local work. Explicit application orchestration remains preferred for complex dependencies; no DDD foundation or decider extraction. D2.2 is deferred until a concrete post-commit need exists. |
| C1: CQRS patterns and optional helpers | Owner-approved on 2026-10-15; present in fetched main `7ea6652`: [bounded native scope](c1-cqrs-consumer-patterns.md), [executable Sales/Inventory journey](../../samples/Wholesale/IntegrationTests/Scenarios/Cqrs/README.md) and [dated proof/extraction report](../reports/c1-cqrs-consumer-patterns.md). Earlier review-snapshot statements are historical. | Joined Sales business filters precede deterministic total/ID pagination and matching counts; resource-permission predicates are not implemented. Native repeatable read is caller-selected for a common snapshot. Typed DI, domain rules, module Contracts and save/commit ownership remain explicit. No new reusable mechanism or CQRS/template runtime. |
| D2.3: module consistency and coordination research | Owner-selected [comparison and recommendations](../reports/d2-3-module-consistency-and-coordination.md) prepared for review. | Module-owned durable processes can use broker-free delivery, provided receipt/effects/replies remain recoverable. D2.3a now supplies the bounded consumer proof using existing inbox/outbox and public intake Contracts. Shared native transactions remain a conditional atomicity alternative requiring separate PostgreSQL/consumer proofs; no new mechanism is extracted. |
| D2.3a: local durable module delivery | Owner-selected [broker-free consumer and public module intake](../../samples/Wholesale/Worker/Local/README.md), prepared with [fresh proofs](../reports/d2-3a-local-durable-module-delivery.md). | Existing inbox/outbox workers and module-owned saga retain independent transactions. Process-death/duplicate/commit-failure proofs accompany typed Contracts; no generic delivery engine or shared transaction is added. |
| D2.1e: bounded local policy and ordering | Owner-selected [capture order and workflow durability review](../reports/d2-1e-local-order-and-workflow-durability.md). | Keep required intra-module reactions atomic and use explicit orchestration for complex dependencies. D2.2 is deferred pending a concrete post-commit requirement. Module-owned saga state is durable; D2.3a separately proves consumer-owned durable intake through Contracts. |
| A1: Access/resource authorization research | [A1 + A1.1 assessment prepared for review](../reports/access-authorization-assessment.md), with [pinned primary sources](../reports/access-authorization-primary-sources.md), 2026-10-16. | Recommend native resource checks/module-owned relational predicates initially, retain conditional OpenFGA evaluation and avoid default business mirroring. A1.1's separately authorized consumer journey is recorded below; it adds no separate gate. The assessment selects no runtime/library interface, engine adoption or new reusable mechanism. |
| A1.1: membership suspension/reactivation | Merged at fetched main `d35b3ee`: owner-authorized [bounded implementation](a1-1-membership-suspension.md), [usage](../../samples/Wholesale/modules/Access/README.md) and [dated evidence](../reports/a1-1-membership-suspension.md). Earlier unstaged statements describe its handoff. | Current scoped administrative authority, revision/Organization concurrency, serialized self/last-admin safeguards and atomic audit. Reactivation keeps retained grants subject to current checks. Existing active admission and immutable operations remain; no new reusable mechanism or library interface. General business permission queries remain separate. |
| A2: tenant-aware authorization and optional RLS research | [Assessment prepared for review](../reports/tenant-authorization-assessment.md) and [primary sources](../reports/tenant-authorization-primary-sources.md), baseline `d35b3ee`. | Retain late consumer resource checks and complete module-owned permission queries. Actor is established before native handlers; tenancy follows native authorization. Optional RLS requires restricted roles, transaction-local context and explicit transport/global exceptions; default local runtime bypasses it. No runtime change/new reusable mechanism. Executable permission/RLS/pooling/retry/transport proofs remain proposed. |
| A2.1: Sales resource permissions | Owner selected [Sales draft permissions and complete queries](a2-1-sales-resource-permissions.md) for implementation, 2026-10-16; [usage](../../samples/Wholesale/modules/Sales/README.md) and [proof report](../reports/a2-1-sales-resource-permissions.md). | Native consumer Read/Edit grants, SQL permission selection before count/page and late HTTP/module entry checks. Existing immutable operations/admission stay intact. Optional RLS/lifecycle/transport proofs remain separate. No new reusable mechanism or library interface. |
| W1: separately hosted workers | Complete and merged: separate API, dispatch/intake/processing roles and finite setup. | Real PostgreSQL/RabbitMQ process proofs cover API independence, competing processors, shutdown, death and recovery using existing locks/leases. See the W1 report; no new library mechanism. |
| OBS1: durable message observability | Complete and merged: [observability scope](durable-message-observability.md) instruments the existing Exports → Rendering process journey. | Fresh process/OTLP/PostgreSQL proofs are recorded in the OBS1 report. Backlog alerts, dashboard inspection and new audit/reply workflows are deferred. |
| WF1: durable inter-module workflow / saga assessment | Owner-approved checkpoint `e05ae58`, merged into `origin/main` as `161a33e`: [stock-issue scope](wf1-durable-inter-module-workflow.md), [usage](../../samples/Wholesale/Worker/Broker/README.md) and [fresh proofs](../reports/wf1-durable-inter-module-workflow.md). | Persisted module-owned request/reply progress composes existing inbox/outbox. No new reusable mechanism. |
| WF2: multi-step saga with compensation | Complete and merged at `d674e39`: owner-approved [bounded continuation replacement](wf2-durable-continuations.md), [Sales usage](../../samples/Wholesale/Worker/Broker/compensating-reservations.md) and [new protocol/adoption evidence](../reports/wf2-durable-continuations.md). | Provided process table, typed selected-method execution, semantic outcomes and atomic request enqueue. Consumer C# owns branching/compensation and native transactions; no workflow scheduler/replay engine. |
| B1: replayable source assessment | Closed by owner direction, 2026-10-14: [retention/contract findings](../reports/b1-retention-and-contract-assessment.md) and dated results retained; experiment code and patch artifacts removed. | Revisit only for a later concrete receiver need. No retained-message, state-transfer or custom-feed design selected as a general solution; no new reusable mechanism. |
| B2: new-service repopulation | If a chosen receiver needs initial replicated state, select and prove its population and ongoing-update contract. | B1's custom feed is optional. Compare targeted hydration, export with overlap and retained broker state as appropriate. Prove source/receiver boundaries, restart, deletions and readiness for the selected contract; no generic aggregate-revision assumption. Implementation remains outside the current assessment. |
| ES3: aggregate regeneration operations | Owner-reviewed [optional executor](library-extraction.md#es3-optional-regeneration-executor-interface-review), [stock-position adoption](../../samples/Wholesale/Worker/Maintenance/stock-regeneration.md), independent ledger and [slice report](../reports/es3-aggregate-regeneration-operations.md) are implemented and prepared for review. | Gloaming owns fresh scopes/native completion, finite recognized-conflict retries and typed progress/outcomes. PostgreSQL proofs distinguish a verified writer read race from persistent ahead-state corruption, and exercise cancellation/process restart/commit uncertainty. Admission, retry values, presentation, durable scheduling and online/pause policy remain consumer-owned. |
| P1: projection assessment and, if justified, implementation | First prove native EF queries/joins/filters over inline aggregates; add one persisted multi-stream or asynchronous view only for an unmet requirement. | Shared-row concurrency, ordering, checkpoint/restart, backfill and cutover need their own proofs. Consumer live reconstruction is already possible; no general projector engine is selected. |
| M2: failed inbox messages and operator retry | Merged owner-reviewed [bounded scope](m2-inbox-failure-recovery.md), [current family contract](../../src/Gloaming.Messaging/docs/inbox-recovery.md) and [historical implementation report](../reports/m2-inbox-failure-recovery.md); later continuations are listed below. | Durable committed failure observations, consumer-selected dead letters and receipt-generation/revision-conditional retry preserve retained identity/payload/deduplication. Classification/admission/operator authority stay consumer-owned. Outbox recovery, retention and queued schema rollout have their own reviewed continuations. |
| M2 follow-up: inbox dead-letter operations | Owner approved the [concrete interface](m2-inbox-dead-letters.md), 2026-10-21, before merging checkpoint `f7d38d4`; [implementation report](../reports/m2-inbox-dead-letters.md). | Public dead-letter terminology, bounded metadata list/detail and the existing conditional retry. Consumer authority stays explicit. No console, payload access/editing, discard, leases, outbox recovery or SQL-reader refactor. |
| M2 outbox dead letters | Owner-approved [scope and interfaces](m2-outbox-dead-letters.md), [current family contract](../../src/Gloaming.Messaging/docs/outbox-recovery.md) and [implementation report](../reports/m2-outbox-dead-letters.md). | Lease-fenced publisher-exception observations, safe metadata List/Get and identity-preserving conditional Retry. Attempts remains committed claims; acceptance/completion uncertainty, consumer classification/authority and restricted-grant rollout stay explicit. No retention, replay, payload editing, lease renewal or generic queue framework. |
| M2 outbox completion supervision | Owner-selected [follow-up](m2-outbox-completion-supervision.md) after approved checkpoint `6bcdf9c`; [fresh evidence](../reports/m2-outbox-completion-supervision.md) prepared for review. | Typed unconfirmed completion faults the optional worker without another poll. Native host options and consumer repair/restart govern containment. Completion can already have committed; failure counters, leases and operator retry remain intact. No schema/grant change or global execution cap. |
| Outbox operational visibility | Owner-selected [scope](outbox-operational-visibility.md) after approved checkpoint `020a292`; [family contract](../../src/Gloaming.Messaging/docs/outbox-observability.md) and [fresh evidence](../reports/outbox-operational-visibility.md) prepared for review. | One-clock route snapshots, metadata-only RLS credentials, native consumer gauges/readiness and restricted executable inspection. Observations reserve no work; readiness topology, exporters and alert policy stay consumer-owned. No schema/grant changes, recovery framework or template preset. |
| Completed-outbox retention | Owner-selected [scope](outbox-completed-retention.md) after approved checkpoint `2125860`; [family contract](../../src/Gloaming.Messaging/docs/outbox-retention.md) and [new proof report](../reports/outbox-completed-retention.md) prepared for review. | Finite route/cutoff/batch deletion with separate maintenance authority and consumer index/migration. Pending, leased and dead-letter work is preserved. Sender identity history is removed; inbox lifetime, scheduling and reconciliation remain consumer-owned. Dispatch permissions stay unchanged. |
| Queued-message schema compatibility | Owner-approved continuation after PR #41; [reviewed scope](queued-message-schema-compatibility.md), [family contract](../../src/Gloaming.Messaging/docs/schema-compatibility.md) and [fresh consumer report](../reports/queued-message-schema-compatibility.md) prepared for review. | Explicit v2 writes alongside retained v1, original-envelope deduplication, existing optional codec, conditional operator retry after reader replacement and native HTTP/RabbitMQ/process proofs. Deployment coordination and rollback remain consumer policy; no new library mechanism, schema migration or template preset. |
| M2 inbox retention / deduplication expiry | Owner approved checkpoint 6510f9e and five explicit post-v1 supplied-feature deferrals; merged as 2e1ffac. [Reviewed scope](m2-inbox-retention.md), [current contract](../../src/Gloaming.Messaging/docs/inbox-retention.md) and [implementation/proof report](../reports/m2-inbox-retention.md). | Processed-only finite cleanup, intake/delete race correction and receipt-generation-aware existing operator retry. Old delivery can execute again after deletion. Preserve pending/processing/dead-lettered work; consumer duration/scheduling stay explicit. Native operating closure has its own row below. |
| W1/M2 broker recovery and W1/OBS1 shutdown | Owner-approved [consumer scope](w1-broker-recovery-shutdown.md), [family contract](../../src/Gloaming.Messaging/docs/worker-recovery.md) and [closure report](../reports/w1-broker-recovery-shutdown.md), implemented for change-set review. | Terminal session fail-stop and supervised fresh-session restart, live native broker outage/retained progress, resource-alarm confirmation retry and SIGTERM, reconciled existing unavailable-collector stops. Preserve identity/fencing/acceptance uncertainty. No library-interface/schema change or arbitrary publisher/exporter deadline. |
| S1: sample/experiment consolidation | [Selected scope](s1-sample-experiment-consolidation.md) implemented: family-local independent examples, canonical Wholesale product and selected T2 rehearsals. [Retirement record](../reports/s1-archive-retirement.md) maps active proofs and immutable historical source. | Functional/template prerequisite gate passed; exact tracked archive and obsolete checksum tooling removed. Final independent code/P2 reviews and release audit inspect the cleaned tree before closure. |
| P2: late isolation and cross-family review | Revisit module persistence and composition after workers/workflows/feeds expose actual pressures; compare the useful Marten/Wolverine capability inventory. | Check typed contexts, scopes, migration/schema ownership, native SQL/filter bypass, tenant establishment, transactions and failures across families. Extract a repeated obligation only if a small mechanism removes meaningful complexity. |
| T2 / readiness: template and release follow-ups | [Selected T2](../reports/t2-template-patterns.md) delivered configurable DDD/CQRS and worker selection/omission; [package readiness](../reports/release-package-readiness.md) records ordinary archives and package-only consumers. | Preserve T1's independent event-free composition. Final integration/package rerun, independent reviews and release audit remain required. No default messaging/event runtime or automatic repository updater follows. |

The original [candidate inventory](library-extraction.md#candidate-inventory) also retains
Access/authentication, email, configurable architecture checks and delivery/update tooling.
These remain individual assessments, not an implicit requirement to implement them all
before v1. This roadmap does not discard them or promote them into library commitments.

## Recommended order

W1 demonstrates that the existing Hosting abstractions, fresh DI scopes and callable
processing/dispatch can run outside an API. Host placement is consumer composition. OBS1 adds diagnostic propagation to that path after owner interface/scope review; its
implementation and fresh evidence are owner-approved and merged. W1 itself added no retained
trace fields or OTel runtime. WF1 and WF2 are merged. WF2's owner-approved bounded
continuation replacement has its own contract/adoption proofs, separate from the earlier
consumer-only and superseded sequential-coordinator evidence. B1's initially approved protocol
is documented as a discarded experiment. The owner closed B1 with its assessment and removed
its code. A later concrete receiver need may warrant revisiting the findings. B2 remains
a separate candidate for population; it does not require the custom B1 feed.

D1 has a committed bounded executable comparison and a decision to keep the new profile model
consumer-owned. D1.1 adds relational child collections and is checkpointed as `f55d803` with
separate proofs. D2.1 adds local submission/allowance handling and is prepared for review with
its own event-lifecycle evidence. D2.2 durable deferred handling remains unimplemented. C1
remains independently useful for query/command organization before template choices. Reuse existing event aggregate
bookkeeping where appropriate; avoid copying it into a general DDD package or making event
sourcing mandatory for state-stored domains.
DDD does not imply domain-event persistence. D1 neither supplies retained integration history
nor authorizes automatic mapping of private facts to public messages.

D1.1 was initially prepared independently while D1 remained under working-tree review. After
the owner committed D1 as `041c64a`, its pending changes were moved onto `feat/ddd-sample-patterns`.
The local-event assessment is now consolidated into Gloaming.EventReactions. It supplies composable
capture and reactions rather than an aggregate model or DDD framework. Consumers use ordinary records,
own their inheritance and select buffers explicitly. Required intra-module effects remain atomic;
complex dependencies use explicit orchestration. The [current review report](../reports/event-reactions-consolidation.md)
supersedes intermediate D2.1/EF1 interfaces while retaining their dated evidence.

Shared Gloaming.EntityFrameworkCore composition supplies deterministic preparation/detection/validation
for existing capabilities. Domain models do not acquire an event marker or aggregate base. D1/D1.1
remain useful consumer patterns without another generic mapping/decider/history abstraction.

D2.3 now supplies a [source-backed comparison](../reports/d2-3-module-consistency-and-coordination.md)
of module-owned durable orchestration, broker-free delivery through Contracts and shared native
transactions. Existing workflow state does not make arbitrary direct calls recoverable. The owner-selected
[D2.3a proof](../reports/d2-3a-local-durable-module-delivery.md) now composes existing inbox/outbox
with public module intake and fresh-scope recovery. Shared transactions need their own concrete atomic-acceptance
requirement and PostgreSQL proof. D2.2 local post-commit reactions remain deferred until justified.

A1 + A1.1 now has a [comparative recommendation](../reports/access-authorization-assessment.md)
after C1 reached fetched main `7ea6652`. Review native module-owned authorization first,
consumer authority/scope/revocation decisions and the small Access suspension/reactivation
proof before Access-specific T2/readiness choices. A1.1 adds no separate pre-development gate.
The owner has now authorized and selected that bounded A1.1 implementation; its report
records fresh consumer proofs separately from A1's source assessment. Review/commit remain
separate, and larger role/query/Personnel/licensing behavior is not implied by this journey.
A1.1 is now merged at `d35b3ee`. A2's [completed assessment for review](../reports/tenant-authorization-assessment.md)
supplies native composition and optional RLS role/lifecycle findings. Permission-query and RLS
runtime proofs remain separately proposed. Neither assessment authorizes runtime changes, a custom engine or a new Gloaming
interface. The preliminary intake and C1's original review-snapshot statements retain dated
evidence rather than current queue/Git status.

WF1 follows the basic messaging path and benefits from W1/OBS1. B1 and B2 are a distinct
source/receiver correctness track, not prerequisites for every workflow. ES3 now supplies one
bounded explicit stock-position maintenance command; reconciliation policy stays consumer-owned.
P1 proceeds when a real query or read-model requirement warrants it. Run P2 near readiness,
after several families have been composed, and carry each proven setup into T2 incrementally.

## Snapshot plus ongoing changes is a separate contract

When a receiver needs replicated current state, repopulation is broader than rebuilding
an existing aggregate from its own event stream. The producer must define disclosed state
and the required ongoing updates, with a boundary between them. A new service does not
inherently need a full replica or historical replay. It must not read another module's
private DbContext or assume internal domain events are a stable integration API.

A timestamp may describe the export, but is insufficient as the sole replay position:
a transaction can stamp a change and commit after a receiver advances past that timestamp.
Equal timestamps and sequence allocation introduce additional hazards. An allocated global
sequence can also commit out of order; `MAX(position)` alone does not establish a safe cut.
Per-stream committed versions solve a narrower problem than one multi-stream feed.

A later population implementation must choose and prove a source protocol: for example a
consistent snapshot tied to a committed feed cursor, or durable overlap capture before the copy with explicit
idempotency and cutover. PostgreSQL snapshot/logical-decoding behavior and Marten's safe
high-water handling provide references; neither CDC nor a daemon implementation is selected.
Online export and an explicitly paused source are different deployment contracts and must
not receive the same guarantee without evidence.

The receiver needs restartable initial population, atomic change application/checkpointing,
and an explicit rule for when its new view becomes available. Retained deletions, schema
versions, tenant admission and expired cursors belong in the concrete scope when required.
The outbox is delivery lifecycle storage; retaining its rows does not automatically make
it a complete replayable change feed. Ordinary inline aggregate reads remain exact-head
reads without automatic repair or snapshot catch-up.
Per-key revision merging is available only where the producer explicitly defines a coherent
public-state revision and compatible updates. Integration messages spanning multiple aggregates,
partial patches and business effects cannot inherit a generic aggregate-version merge rule.

## Worker scaling and durable workflows

Separate hosts allow independent deployment, resource limits and replica counts. Existing
inbox processing holds a row lock through its local transaction; outbox publication uses a
committed lease and fences completion. Those protocols already address different competing
worker obligations. W1 demonstrated them in separate processes without introducing
leader election solely because multiple workers exist.

Bounded concurrency, batching, backpressure, lease renewal, partition assignment and exclusive
scheduling remain candidates for later evidence. A leader does not make external delivery
exactly once or prevent every stale publisher effect. Module-specific typed contexts and
native transactions remain the isolation boundary in worker hosts as well as APIs.

A saga first means persistent business progress across independently committed module
operations. It is not a distributed rollback transaction. Start with visible transitions
and existing messaging; durable timeout delivery requires a real persisted/recoverable path,
not an in-memory timer. A saga base class, registry, generic scheduler or workflow DSL is
not selected by the consumer implementations. WF2 adds a real compensating business action;
its owner-approved follow-up implements bounded explicit continuations. Its scope and
report distinguish the new mechanism from existing messaging and consumer-owned policy.

## Current boundaries and completion criteria

Messaging already retains MessageId, CorrelationId and CausationId. Optional diagnostic context and native
spans are implemented by the separately reviewed OBS1 work; the current audit API deliberately excludes
these identifiers. Host OTel setup/exporters and native transport configuration remain editable
sample/template composition. No Gloaming root runtime or universal transaction context is
planned simply to connect these families.

Each implementation handoff must name supported versus deferred behavior, provide executable
consumer usage and failure proofs, and keep the owning family's docs self-sufficient. Existing
historical tests are context, not fresh proof of a new topology, interface or guarantee. An
assessment can finish with template patterns and an explicit finding that no new reusable
mechanism was proven. This roadmap/research update proves no new runtime capability.
