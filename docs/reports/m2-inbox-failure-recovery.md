# M2: failed inbox messages and operator redrive

Current naming, the metadata store and later proof results are recorded in the
[dead-letter report](m2-inbox-dead-letters.md); terminology below retains dated M2 history.

The [2026-10-20 supervision follow-up](m2-inbox-failure-supervision.md) adds a typed accounting
fault and worker containment, with fresh evidence separate from this original report.

Date: 2026-10-18. Base: fetched `origin/main` `54ebe8d`. Branch:
`feat/inbox-failure-recovery`, without upstream tracking. The owner reviewed
[consumer usage, interfaces and protocol](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/m2-inbox-failure-recovery.md), then authorized
implementation. The complete implementation/documentation change set remains unstaged and
uncommitted for line-by-line owner review.

## Outcome

One bounded inbox-only recovery capability is implemented and exercised through the public
interfaces. PostgreSQL retains confirmed failure counts, consumer-selected quarantine, and
revision-conditional operator retry of the original delivery. The independent Rendering
consumer supplies executable failure policy and authenticated operator endpoints. Existing
Inventory/Sales inbox callers receive forward schema migrations; restricted Sales processor
grants and effective-grant checks are updated together.

The new reusable mechanisms are serialized failure accounting and a conditional,
identity-preserving retry operation. No new package, runtime dependency, outbox recovery,
generic management framework or template preset was introduced.

## Mechanism and consumer policy

The processor acquires the delivery row lock before establishing a native savepoint. Handler,
save or completion failure rolls local effects back to that savepoint, including earlier
executed business/reply SQL. It commits only failure accounting in the surviving transaction,
while retaining the original lock. Failed tracked proposals are discarded with the scope.
No separate transaction writes retry metadata after releasing exclusion.

The previous pending-only update protected successor completion but could not distinguish
newer pending failures or operator redrive. Expanding that update was rejected. A revision read
inside a fully rolled-back attempt alone would not establish durable attempt identity. The new
protocol removes that gap without adding an inbox lease or public attempt token.

The separate recovery operation changes only a quarantined, unprocessed row at the observed
revision. It clears quarantine, sets database-clock eligibility, resets cycle failures and
increments revision/redrive count/time. Lifetime failures, last-failure diagnostics, delivery
key, payload, named schema, tenant/correlation/cause, receipt and tracing context remain.
Failure, completion and redrive advance revision, so repeated old requests cannot reopen a
later quarantine. Equivalent intake still deduplicates every state; incompatible reuse conflicts.

| Owner | Responsibility and evidence |
| --- | --- |
| Messaging library | Native locking/savepoint rollback, parameterized accounting, counters/revision, quarantine exclusion, conditional retry, fresh-context guards and schema mapping. PostgreSQL interface proofs exercise these together. |
| Rendering consumer | Exception classification, safe codes, one-second delay, three-recorded-failure budget for recognized serialization/deadlock failures, immediate stop for invalid/conflicting/unclassified failures. |
| Rendering operator host | Explicit optional bearer credential, native authentication/authorization, fixed producer/subscription, safe inspection DTO, expected-revision input and attributed request/result logging. No credential means no operator routes. |
| Sales consumer | Existing host-admitted immutable tenant/system actor, RLS, finite role provisioning and exact column-grant inventory. Default retry remains; the production-like sample host adds no operator registration. Test-only stop policy proves the available composition. |
| Templates | No files, presets, generated composition or mandatory messaging dependency changed. Existing architecture/independent-adoption checks pass. |

The new policy is scoped/keyed per context/subscription and supplies synchronous classification
without I/O or business mutation. Omission preserves indefinite positive-delay retry with code
`processing-failed`. There is no library-selected poison threshold. Admission, business
idempotency, operator identity/permissions, approval audit and production credential lifecycle
remain consumer policy. The sample operator credential grants one fixed permission; it is not
a general Access/identity-provider integration.

## Fresh proofs

These are new executions against active code, not inherited I1/W1/OBS1 or archived guarantees.
The added coverage comprises 14 inbox-library cases, four Rendering cases, one real-process
case and two restricted Sales cases. Existing tests also received focused stronger assertions
or changed their recovery call to the supported operator operation.

| Guarantee | Supporting evidence |
| --- | --- |
| Rollback with durable accounting | Earlier executed EF business/reply saves disappear while counts/retry/quarantine commit. Existing handler/save/completion faults now assert count/revision/code. |
| Exclusion after rollback | A PostgreSQL trigger pauses failure UPDATE at an observed advisory-lock wait. Independent reads see rolled-back effects, NOWAIT cannot acquire the inbox lock, a competitor skips, and unrelated work completes before accounting is released. |
| No obsolete retry overwrite | Terminate the actual processing backend after an earlier save; a successor fails and quarantines. Release the old invocation: it throws without changing successor revision, availability, count, code or quarantine. Existing successor-completion proof also passes. |
| Operator competition | Two retry operations wait on an actual locked quarantined row. On release exactly one transition applies and one returns NotApplied. Pending active processing cannot be interrupted. Stale requests after a later quarantine cannot reopen it. |
| Accounting failure/rollback | Native metadata constraint failure, deferred accounting-commit failure, injected savepoint-rollback failure, throwing policy and invalid decision preserve both errors and leave no business effects or partial metadata. |
| Commit uncertainty | Native EF transaction callbacks simulate response loss after an actual PostgreSQL completion/accounting/retry commit. Fresh reads observe the committed result; no compensating failure is added, and the same operator revision cannot apply twice. This is not a network failover test. |
| Repeated redrive and deduplication | Failure, quarantine, retry, another failure, another retry and success preserve envelope and original diagnostic context. Lifetime counts accumulate; only applied retry resets cycle counts; processed work rejects retry. Equivalent intake deduplicates and changed payload conflicts. |
| Process death/restart | A child worker records one transient failure and blocks during its next SQL attempt. Kill it, release the test barrier so PostgreSQL can observe the closed socket, and verify no extra count/effect. A fresh process reaches three recorded failures and stops. Another restarted worker processes unrelated work with quarantine unchanged. After retry a fourth process completes the original delivery. |
| Consumer authority/admission | Kestrel tests deny missing/wrong operator credentials before storage, omit routes without configuration, and permit the fixed lane with the configured credential. Inspection omits payload/exception text. Retried unsupported retained work repeats handler admission and quarantines again. |
| RLS/restricted grants | A restricted Sales processor records/quarantines only its admitted tenant; foreign work stays unchanged and workflow/outbox effects roll back. Revoke the failure-count grant: actual worker startup rejects it; direct processing preserves errors and commits neither business effects nor partial accounting. |
| Schema preservation | Rendering upgrades historical pending/completed rows, preserving content/status and initializing zero known failures. Rendering/Inventory/Sales native snapshots all report no pending model changes. Existing Inventory and Sales consumers run their migrations successfully. |
| Existing composition | Full workflows, broker-free local module delivery, Inventory inbox dispatch, workers and OTLP telemetry pass. No payload/schema rewriting or change to outbox publication protocol is involved. |

The process proof initially waited for backend exit while still holding its own database
barrier. PostgreSQL may observe a dead client only after the blocked statement resumes; the
proof now releases the barrier before checking backend exit, as existing W1 proofs do. This
does not establish prompt dead-client detection or bounded lock release during arbitrary stalls.

An existing telemetry test could retain an old PostgreSQL statistics snapshot while polling
inside the intake transaction. Its wait observation now uses fresh independent contexts.
The new HTTP fixture was corrected to send the sample's existing Pascal-case payload contract;
the production wire contract was not changed. Final passing runs below supersede those failures.

## Verification

All final suites passed, with **394 passed, zero failed, zero skipped** across ten projects/selections:

| Project / selection | Passed |
| --- | ---: |
| Messaging `InboxPostgresTests` | 48 |
| Messaging `WorkflowPostgresTests` | 38 |
| Messaging `MessagingTests` | 8 |
| Messaging `WorkflowTests` | 8 |
| `InboxDemo.Tests` | 17 |
| `MessagingWorkerDemo.Tests` | 9 |
| Wholesale `WorkflowDemo.Tests` (includes all 49 restricted Sales reply cases) | 141 |
| Wholesale `LocalWorkflowDemo.Tests` | 14 |
| Wholesale `EventPersistenceDemo.Tests`, `*InboxDispatchTests` selection | 15 |
| Repository `ArchitectureTests` | 96 |

Tests used .NET SDK 10.0.401 and existing disposable PostgreSQL 18.6/RabbitMQ fixtures through
rootless Podman. Process/telemetry tests also use their existing Collector fixture. Invocation:

```sh
DOCKER_HOST=unix:///run/user/1000/podman/podman.sock \
TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE=/run/user/1000/podman/podman.sock \
DOTNET_PROCESSOR_COUNT=4 \
  dotnet test --project <project.csproj> --no-build --no-restore \
  --no-progress --no-ansi --output Normal --show-stdout Failed --show-stderr Failed
```

The Inventory selection adds `--filter-class '*InboxDispatchTests'`. Earlier opening baselines
were 34 inbox and 13 Rendering tests on unmodified main; they did not prove this capability.

Additional successful checks:

- Active solution build with zero warnings/errors.
- Native `dotnet ef migrations has-pending-model-changes --no-build` for RenderDbContext,
  InventoryDbContext and SalesDbContext: no pending changes in all three.
- Active solution `dotnet format style` and `dotnet format analyzers`, both with
  `--verify-no-changes --no-restore`.
- `dotnet csharpier check . --include-generated`: 837 files checked.
- Archive integrity: all 800 original files verified against the frozen checkpoint.
- `git diff --check` and `git diff --cached --exit-code`: clean whitespace and preserved empty index.

No new test lane or durable repository script was needed; the affected projects already belong
to the Messaging/consumer/architecture lanes. The full repository test matrix, other providers,
production identity-provider integration, deployment, real network failover and external poolers
were not certified by these focused checks.

## Files for owner review

Paths below are repository-relative; review public interfaces and implementation line by line.

1. `src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore/`: InboxFailure,
   InboxFailureDecision, IInboxFailurePolicy, IInboxRecovery/InboxRetryResult, new record fields,
   fixed mapping/defaults and keyed policy registration. Existing processing/handler/result
   declarations remain unchanged.
2. `src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/`:
   PostgresInboxProcessor savepoint placement and commit-error paths, PostgresInboxStorage
   accounting SQL/model guards, PostgresInboxRecovery revision predicate, mapping constraints
   and separate recovery registration.
3. `src/Gloaming.Messaging/samples/InboxDemo/`: RenderInboxFailurePolicy, RenderOperatorAuthenticationHandler,
   RenderInboxOperations and explicit host/Program wiring. Review auth-before-invocation,
   fixed lane, safe DTO and consumer classification.
4. Forward `AddInboxFailureRecovery` migrations/designers and current snapshots in Rendering
   (`20261018183419`), Inventory (`20261018183520`) and Sales (`20261018183600`). DDL touches
   only each inbox's new columns/checks/index. Native scaffolding also refreshes existing model
   annotations; Inventory snapshot entity ordering is retained to reduce unrelated diff noise.
   Old migrations were not changed.
5. `samples/Wholesale/Worker/Broker/SalesReplyDatabase.cs`: processor-only new column grants
   and the shared exact effective-grant inventory. No redrive count/time rights are added.
6. Proofs: library FailureRecoveryTests, Rendering FailureRecoveryTests/RecoveryMigrationTests,
   InboxRecoveryProcessTests and SalesReplyRecoveryTests. Existing broker/OTLP proofs now call
   recovery after consumer-selected quarantine; the trusted fixture is explicitly the operator.
7. [Family recovery contract](../../src/Gloaming.Messaging/docs/inbox-recovery.md), package and
   sample READMEs, current capability/deferred records, [ADR 0018](../adr/0018-serialized-inbox-failure-recovery.md)
   and design/roadmap/extraction links.

## Limits and remaining work

Failure accounting survives committed state and application restart. It does not count every
cancelled, killed or infrastructure-failed attempt. There is no global cap on all executions.
Policy or unrecoverable accounting failures may leave no new count. Noncooperative work and
database statements blocked elsewhere can delay lock release/shutdown. Response-loss tests
exercise commit ambiguity through native callbacks, not actual network partition/failover.

Rollout requires consumer migrations before updated binaries, reviewed grant updates for
already-provisioned restricted roles, and stopping old processors before relying on quarantine.
Old binaries ignore quarantine. Mixed versions and downgrade/online schema rollout are not
supported. Legacy rows start with zero known failures; historical counts are not reconstructed.

Application invocation authorization remains essential. Native column grants cannot distinguish
trusted processor/recovery statements from arbitrary SQL by the credential holder. The finite
sample bearer policy is independently replaceable; production admission, permission checks,
credential management, durable operator approval/audit and presentation remain consumer work.
Technical metadata/diagnostic fields grant no authority.

Outbox recovery, retention/cleanup, payload editing, queued schema evolution, generalized
management, additional providers, throughput guarantees and template rollout remain excluded.
No archived source/fixtures, module business Contracts or existing staged snapshot were changed.
No commit or push was made. The implemented reusable mechanisms have new PostgreSQL/consumer
proofs; further extraction is not justified by the sample's classification/authentication policy.
