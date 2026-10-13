# Remaining capability roadmap

Status: proposed scope and order, 2026-10-10. This roadmap does not approve interfaces; OBS1 has separate owner-reviewed scope.
The owner considers transactional audit complete; checkpoint `18a76f8` is present in
`origin/main`. Existing implementations and dated slice reports remain the evidence for
supported capabilities. The original E0–E10 extraction sequence is historical context,
not the current work queue.

W1 is owner-approved at checkpoint `7b43201`, merged into `origin/main` as `94ca57a`:
[native host and setup](../../samples/MessagingWorkerDemo/README.md),
[bounded scope](w1-separate-worker-hosts.md) and
[fresh process evidence](../reports/w1-separate-worker-hosts.md). No library interface changes
were needed. OBS1 is owner-approved at checkpoint `b44b5d5`, merged into `origin/main`
at `4c0e0c5`.
[Fresh evidence](../reports/obs1-durable-message-observability.md) is separate from this roadmap;
remaining rows are candidates.

Read the actual consumers and owning family documentation before starting a slice. Resolve
stale descriptions against current code and owner decisions; do not carry an earlier
proposal into implementation merely because it appears in a plan. Each row below is a
candidate outcome, not authorization to build every associated abstraction. Split the
outcome further when its concrete interface/file proposal reveals independent work.

The [reference review](../reports/remaining-capability-reference-review.md) separates
primary-source findings from recommendations. Marten and Wolverine are behavioral
references, not selected runtime dependencies or a feature-parity target.

## Work queue

| Candidate | Bounded first outcome | Boundary and required evidence |
| --- | --- | --- |
| D1: DDD building blocks and template patterns | Compare a state-stored aggregate and an event-sourced aggregate; demonstrate creation, invariants, domain values and accepted/rejected decisions. | Domain rules stay in modules. Assess reusable identity/value/bookkeeping helpers against actual duplication; no mandatory aggregate hierarchy or generic repository. Useful editable patterns are a valid result even if no library is extracted. |
| C1: CQRS patterns and optional helpers | Explicit command/query contracts, handlers and native `{Aggregate}Queries` / `{Aggregate}Filters` in real consumers, including filtering a joined EF read view. | Separate read/write logic can share storage. Preserve direct calls, typed DI and caller-owned save/commit. A mediator, pipeline or handler registry needs demonstrated value rather than CQRS naming alone. |
| W1: separately hosted workers | Complete and merged: separate API, dispatch/intake/processing roles and finite setup. | Real PostgreSQL/RabbitMQ process proofs cover API independence, competing processors, shutdown, death and recovery using existing locks/leases. See the W1 report; no new library mechanism. |
| OBS1: durable message observability | Complete and merged: [observability scope](durable-message-observability.md) instruments the existing Exports → Rendering process journey. | Fresh process/OTLP/PostgreSQL proofs are recorded in the OBS1 report. Backlog alerts, dashboard inspection and new audit/reply workflows are deferred. |
| WF1: durable inter-module workflow / saga assessment | Owner-approved checkpoint `e05ae58`, merged into `origin/main` as `161a33e`: [stock-issue scope](wf1-durable-inter-module-workflow.md), [usage](../../samples/Wholesale/WorkflowDemo/README.md) and [fresh proofs](../reports/wf1-durable-inter-module-workflow.md). | Persisted module-owned request/reply progress composes existing inbox/outbox. No new reusable mechanism. |
| WF2: multi-step saga with compensation | Owner-approved [bounded continuation replacement](wf2-durable-continuations.md), [Sales usage](../../samples/Wholesale/WorkflowDemo/compensating-reservations.md) and [new protocol/adoption evidence](../reports/wf2-durable-continuations.md). | Provided process table, typed selected-method execution, semantic outcomes and atomic request enqueue. Consumer C# owns branching/compensation and native transactions; no workflow scheduler/replay engine. |
| B1: replayable source contract | One source module exposes retained integration changes with a provable progress cursor, ordering, retention floor and expired-cursor outcome. | Prove late commits, gaps, duplicates and restart. Outbox dispatch order and timestamps are not an established replay protocol. Public export contracts must preserve module ownership. |
| B2: new-service repopulation | Populate a new receiver from a consistent initial snapshot, then apply retained changes without losing or incorrectly repeating effects. | Builds on a proven boundary/feed such as B1. Prove concurrent writes during export, interrupted copy/pages, deletes and transactional receiver checkpoints. Start with one source; state-stored modules must also be supported by the chosen scenario. |
| ES3: aggregate regeneration orchestration | A simple explicit maintenance host/job around the existing full aggregate rebuilder. | Still needed eventually. Separate from normal fetch/write and from new-service bootstrap. Prove bounded retries, cancellation, restart and conflicts with writers; scheduling, admission and online/offline policy remain explicit. |
| P1: projection assessment and, if justified, implementation | First prove native EF queries/joins/filters over inline aggregates; add one persisted multi-stream or asynchronous view only for an unmet requirement. | Shared-row concurrency, ordering, checkpoint/restart, backfill and cutover need their own proofs. Consumer live reconstruction is already possible; no general projector engine is selected. |
| M2: messaging operations and compatibility | Select one concrete gap: poison handling/redrive, retention/deduplication windows, queued schema rollout, or bounded dispatch parallelism. | These are separate slices. Define identity, retry, retention and operator semantics before adding knobs. A successful basic queue does not prove these operational guarantees. |
| P2: late isolation and cross-family review | Revisit module persistence and composition after workers/workflows/feeds expose actual pressures; compare the useful Marten/Wolverine capability inventory. | Check typed contexts, scopes, migration/schema ownership, native SQL/filter bypass, tenant establishment, transactions and failures across families. Extract a repeated obligation only if a small mechanism removes meaningful complexity. |
| T2 / readiness: template and release follow-ups | Bring proven DDD/CQRS/worker setup into configurable templates one capability at a time; later review packaging, docs and v1 gaps. | Preserve T1's independent event-free composition. No default messaging/event runtime, giant preset expansion or automatic repository updater follows from this roadmap. |

The original [candidate inventory](library-extraction.md#candidate-inventory) also retains
Access/authentication, email, configurable architecture checks and delivery/update tooling.
These remain individual assessments, not an implicit requirement to implement them all
before v1. This roadmap does not discard them or promote them into library commitments.

## Recommended order

W1 demonstrates that the existing Hosting abstractions, fresh DI scopes and callable
processing/dispatch can run outside an API. Host placement is consumer composition. OBS1 adds diagnostic propagation to that path after owner interface/scope review; its
implementation and fresh evidence are owner-approved and merged. W1 itself added no retained
trace fields or OTel runtime. WF1 is merged; WF2 now exercises multiple steps and compensation
for owner code review. Its owner-approved sequential coordinator follow-up is implemented
and has its own fresh contract/adoption proofs, separate from earlier consumer-only evidence.
B1 remains the next recommended runtime slice after WF2 review.

D1 and C1 can be explored independently before adding template choices. They are explicit
roadmap items, with a library extraction decision at the end of each assessment. Reuse the
existing event aggregate bookkeeping where appropriate; avoid copying it into a general DDD
package or making event sourcing mandatory for state-stored domains.

WF1 follows the basic messaging path and benefits from W1/OBS1. B1 and B2 are a distinct
source/receiver correctness track, not prerequisites for every workflow. ES3 remains planned;
the simple current aggregate model lets consumers own reconciliation until that slice starts.
P1 proceeds when a real query or read-model requirement warrants it. Run P2 near readiness,
after several families have been composed, and carry each proven setup into T2 incrementally.

## Snapshot plus ongoing changes is a separate contract

The requested repopulation is broader than rebuilding an existing aggregate from its own
event stream. A new service needs producer-owned public state and integration changes, with
a defined boundary between them. It must not read another module's private DbContext or
assume internal domain events are a stable integration API.

A timestamp may describe the export, but is insufficient as the sole replay position:
a transaction can stamp a change and commit after a receiver advances past that timestamp.
Equal timestamps and sequence allocation introduce additional hazards. An allocated global
sequence can also commit out of order; `MAX(position)` alone does not establish a safe cut.
Per-stream committed versions solve a narrower problem than one multi-stream feed.

B1/B2 must choose and prove a source protocol: for example a consistent snapshot tied to a
committed feed cursor, or durable overlap capture started before the copy with explicit
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
its owner-approved follow-up implements a small fixed-sequence coordinator. Its scope and
report distinguish the new mechanism from existing messaging and consumer-owned policy.

## Current boundaries and completion criteria

Messaging already retains MessageId, CorrelationId and CausationId. Optional diagnostic context and native
spans are implemented by the separately reviewed OBS1 work; the current audit API deliberately excludes
these identifiers. Host OTel setup/exporters and native transport configuration remain editable
sample/template composition. No Rootbolt root runtime or universal transaction context is
planned simply to connect these families.

Each implementation handoff must name supported versus deferred behavior, provide executable
consumer usage and failure proofs, and keep the owning family's docs self-sufficient. Existing
historical tests are context, not fresh proof of a new topology, interface or guarantee. An
assessment can finish with template patterns and an explicit finding that no new reusable
mechanism was proven. This roadmap/research update proves no new runtime capability.
