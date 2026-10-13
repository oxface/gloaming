# WF2 follow-up: reusable sequential saga coordinator

Historical experiment, superseded before checkpoint by the owner-approved
[continuation replacement](../plans/wf2-durable-continuations.md). Old test counts are dated
evidence, not results for the replacement. The staged review snapshot is retained.


Date: 2026-10-10. The owner approved the concrete interface/replacement scope and requested
implementation on `feat/compensating-workflow`, based on merged WF1 at `161a33e`.
Changes are uncommitted. The owner's staged review snapshot is preserved byte-for-byte;
all follow-up edits are in the working tree. No push or pull request was created.

## Outcome and complexity removed

The optional package-free `Rootbolt.Messaging.Sagas` now owns fixed sequential progress,
requested command identities, retained business outcome codes, duplicate/conflict handling,
reverse compensation and explicit retry of a refused undo. It requests one action at a time
and waits for each acknowledged result. Exceptions and missing replies never imply refusal.

The optional `Rootbolt.Messaging.Sagas.EntityFrameworkCore` supplies a used native property
converter, independent tracker snapshots and optimistic concurrency on the original document.
PostgreSQL JSONB equality is proven. Neither package supplies a store, worker, transport,
SaveChanges override, domain base class or implicit transaction. Existing inbox/outbox workers
continue performing local processing and publication.

Sales replaces per-line command/decision fields, a child release-attempt entity/table,
technical transition branches and manual progress versioning with one Progress property.
The business entity now has 122 lines and its mapping 30 lines; line counts are descriptive,
not proof of utility. Some message mapping remains explicit and
slightly grows because it interprets the provided action. The meaningful removal is one shared
implementation of step ordering, retained attempt outcomes and compensation progression, also
exercised by an independent adopter requiring two reverse undos instead of the sample's one.

Inventory's reserve/release rules, original business receipt, event append and resource
idempotency stay unchanged from the initial WF2 consumer implementation. Sales still owns
requested lines, stable reservation IDs, full reply validation, admission, routes/payloads,
business status mapping and operator authorization. Correlation belongs to the owning order;
command attempts remain distinct from stable resources. No cross-module transaction is added.

The owner-approved draft-schema replacement revises only this slice's new uncommitted Sales
migration/designer and current snapshot. It uses one Progress JSONB column and removes the draft
release-attempt table. All earlier migrations and the Inventory migration are preserved. Native
EF generated the replacement from the previous committed Sales snapshot; no installed demo
was reset. Applying this to a database already using the earlier draft requires a separately
reviewed forward migration. Fresh test databases exercise the revised native migration.

## Ownership and guarantees

| Obligation | Owner / exercised mechanism |
| --- | --- |
| Ordered forward actions and reverse undo | New SequentialSaga core; one pending action and retained acknowledged outcomes. |
| Equal old results, conflicting results, unrelated commands | Core no-op/conflict checks; consumer validates complete business reply first. |
| Refused undo and later retry | Core retains old attempt result and returns a new command ID for the same step; consumer chooses when retry is authorized. |
| Mutable object change detection | New EF adapter uses an independent snapshot of serialized progress. |
| Competing process updates | Adapter's original-document concurrency token; native EF exception and fresh operation retry. |
| Progress, next command and inbox completion atomicity | Existing Rootbolt inbox processor, typed outbox and native owning transaction. |
| Reserve/release business effects and repeated external commands | Inventory-owned resource IDs, retained original inputs/results and current-state decision rules. |
| Transport delivery, producer trust, tenancy and routing | Existing messaging mechanisms plus explicit consumer native adapters/admission. |

No new transport or transaction mechanism was proven. The newly proven reusable mechanisms are
bounded saga bookkeeping and native progress mapping/snapshot/concurrency. The full composed
workflow uses earlier mechanisms as well; it does not turn them into new saga-engine guarantees.

## Review-worthy files

- SequentialSaga (historical source, replaced by WF2 continuations):
  candidate replacement, retained plan/attempt ledger, outcome conflict checks, reverse traversal,
  and one set of transition rules for operation and stored-state validation. Public records/enums
  have XML documentation and explicit numbering.
- EF mapping (historical source, replaced by WF2 continuations):
  converter, independent comparer snapshot and original-value concurrency predicate.
- Sales business entity (historical source, replaced by WF2 continuations),
  [reply handler](../../samples/Wholesale/modules/Sales/Sales/ReservationOrders/StockReservationReplyHandler.cs),
  [commands/envelopes](../../samples/Wholesale/modules/Sales/Sales/ReservationOrders/StockReservationOrders.cs)
  and mapping: business inputs/validation retained, core action mapped into existing Contracts.
- Standalone adopter (historical source, replaced by WF2 continuations):
  ordinary consumer-owned entity/schema and existing typed inbox/outbox without tenancy,
  EventSourcing, sample modules or RabbitMQ. Resource replies are contract inputs; this is not
  evidence of a real external provisioning service.
- Core tests (historical source, replaced by WF2 continuations) and
  PostgreSQL proofs (historical source, replaced by WF2 continuations):
  refusal, undo ordering, persistence, retry, duplicates, transaction failures and concurrency.
- Revised uncommitted Sales migration, metadata and snapshot; workflow fault/concurrency probes
  now inspect the stored attempt ledger. Business assertions remain unchanged.
- Optional project references, central EF version pin, architecture rules and Messaging CI lane
  register only used dependencies/tests. [The local contract](../../src/Rootbolt.Messaging/docs/workflows.md)
  records supported setup and gaps without requiring root design documents.

## Fresh verification

SDK 10.0.401; PostgreSQL 18.6 and RabbitMQ 4.3.6 via Podman. Process/HTTP checks use
DOTNET_USE_POLLING_FILE_WATCHER=1 for this machine's established inotify limit.

| Suite | Passed |
| --- | ---: |
| New saga core contract | 21 |
| New standalone PostgreSQL saga adoption | 9 |
| WorkflowDemo, including all WF1 and WF2 broker/process cases | 62 |
| Architecture and independent dependencies | 83 |
| Existing message envelope contract | 8 |
| Existing PostgreSQL outbox contract | 36 |
| Existing PostgreSQL inbox contract | 34 |
| Existing event-persistence consumers | 190 |
| Existing module migrations/persistence consumers | 36 |
| Existing Wholesale HTTP/profile/audit consumers | 105 |
| Total distinct cases | 584 |

Core proofs cover ordered progression, reverse undo only for accepted steps, NotRequired skips,
Unsupported intervention, explicit undo retry, older duplicates/conflicts, rejected mutation
preservation, independent restoration, copied plans and malformed/inconsistent stored progress.

Standalone PostgreSQL proves two acknowledged reverse undos across fresh contexts; a separate
variant skips explicitly harmless work. It proves mutation tracking, duplicate reply completion
without another action, stale original-document arbitration with losing outbox rollback,
explicit rollback, next-action failure with fresh processing recovery, complete business reply
validation, missing business results rejected instead of deserialized as false/refusal, and
refused undo surviving reload/retry and an older refusal after success.

The complete 62-case workflow suite passes after replacement. Real native faults and PostgreSQL
barriers retain Sales/Inventory rollback and competing-writer proofs; actual child-host death
between committed release and Sales acknowledgement recovers without repeating compensation.
The operator release-refusal journey in this sample remains explicitly simulated, as recorded
in the initial report; the standalone PostgreSQL case independently proves library retry state.

Active solution build succeeds with zero warnings/errors. Native semantic style/analyzers pass;
CSharpier checks 614 active files including migration metadata. `git diff --check` passes and
archive verification retains all 800 original files at `15d1ec6`. The staged snapshot matches
its saved pre-implementation binary diff. New suites are wired into the existing Messaging CI
lane, but hosted CI has not run for this uncommitted work.

An initial local workflow invocation passed an extra `-m:2` argument to the native test runner
and selected zero tests; the corrected invocation ran all 62. The first core compile exposed
a target-typed constructor ambiguity in two argument tests; explicit SagaStep constructors fixed
it. These were local invocation/test syntax issues, not disabled protocol checks.

## Limits and historical evidence

The sequence is fixed and sequential. Plans/attempts are retained; their growing JSON document is
not compacted. Whole-document equality and replayed bookkeeping have no throughput guarantee.
Converted progress members are not native LINQ filters; status indexing/scanning needs a separate
relational read-side assessment. PostgreSQL is the only proved mapping provider.

The consumer must enqueue each returned action with progress and retains final save/commit control.
The utility cannot validate arbitrary bypass SQL or infer complete business reply equivalence.
A database rollback does not reverse in-memory mutations: failed operation scopes are discarded.
No timer treats an ambiguous outcome as failure or automatically undoes pending work.

Branching, parallel steps, cancellation, undo after completed success, workflow-plan migration,
scheduling, poison/redrive, retention, attempt limits, operator UI and generalized saga storage
remain deferred. WF1's overdue business process keeps its ordinary state machine. No template
preset was added; T1's source and library selection remain unchanged, and its creator rehearsal
was not rerun. Architecture/package checks verify the new packages remain optional.

The initial WF2 474-case report remains separately dated consumer-only evidence. O1/I1/W1/OBS1
reports retain historical delivery/telemetry guarantees; this run is not a new OTLP export,
exactly-once, production deployment or alternate-provider proof. B1 remains the recommended
next runtime slice after WF2 owner review. Archive code/tests remain frozen and were not run.
