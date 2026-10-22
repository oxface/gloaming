# V1 slice closure audit — 2026-10-22

## Outcome and evidence boundary

Source/documentation assessment against fetched main `eeaa25b`, while another session owns
M2 inbox retention. This audit changes no runtime, interface, schema or tests. No tests were
executed; linked test definitions and earlier reports are inspected evidence, not new passes.
No new reusable mechanism was proven. The owner's intent is to finish important work inside
its existing capability block, not repeatedly replace completed slices with new ones.

## Later integration status

Inbox retention merged at `2e1ffac`; broker recovery/shutdown and native session extraction
merged at `790a8b7`. The rows below preserve the original audit, not current outstanding
status. Remaining completion is tracked in the [execution ledger](../plans/v1-integration-completion.md).

## Important closure obligations

| Owning block | Finding and evidence | Required disposition before v1 |
| --- | --- | --- |
| M2: inbox retention | Current [inbox contract](../../src/Gloaming.Messaging/docs/inbox.md) retains processed/dead-lettered receipts indefinitely. Completed-outbox deletion does not define receiver deduplication expiry. | Open and already assigned to the M2 session. Define/prove receipt cleanup and redelivery after expiry before declaring M2 complete. Do not duplicate that work here. |
| W1/M2: real transport recovery | [BrokerSession](../../src/Gloaming.Messaging/samples/MessagingWorkerDemo/BrokerSession.cs) disables automatic recovery and retains one channel. [WorkerPublisher](../../src/Gloaming.Messaging/samples/MessagingWorkerDemo/WorkerPublisher.cs) reuses it; [OutboxWorker](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore/OutboxWorker.cs) retries ordinary publication exceptions. [RabbitMqIntakeWorker](../../src/Gloaming.Messaging/samples/MessagingWorkerDemo/RabbitMqIntakeWorker.cs) instead stops the host on intake failure. [Process tests](../../src/Gloaming.Messaging/samples/MessagingWorkerDemo.Tests/WorkerProcessTests.cs) prove host-death/restart, not live broker disconnect/recovery. | Keep consumer transport recovery open in W1/M2 until the final worker composition has a tested reconnect or fail-stop/supervised-restart policy. Do not require a Gloaming transport manager. Source inspection identifies a recovery risk; no live outage was reproduced by this audit. |
| WF2: definition deployment | [Workflow contract](../../src/Gloaming.Messaging/docs/workflows.md) supports one registered definition version and rejects incompatible persisted versions. [DefinitionMismatchRequiresExplicitMigration](../../src/Gloaming.Messaging/tests/WorkflowPostgresTests/ExecutionTests.cs) checks rejection, not a successful deployment transition. | Before release, document and rehearse a supported consumer deployment path that can continue retained instances/replies. Retaining compatible definitions or a controlled drain can suffice; do not infer multi-version routing or build a migration engine. This is an operational closure obligation, not evidence that WF2's mismatch guard is broken. |
| W1/OBS1: worker shutdown boundary | [ObservabilityTests](../../src/Gloaming.Messaging/samples/MessagingWorkerDemo.Tests/ObservabilityTests.cs) includes UnreachableCollectorDoesNotPreventIndependentBusinessCommitAndProcessing and orderly stops. [WorkerProcessTests](../../src/Gloaming.Messaging/samples/MessagingWorkerDemo.Tests/WorkerProcessTests.cs) covers in-flight processing cancellation. [The extraction plan](../plans/library-extraction.md#proof-transfer-and-unresolved-limits) still names stalled publication/exporter shutdown as unproven. | During final host consolidation, reconcile actual tests with this old gap, and exercise the selected host's stalled publication/shutdown path. State the supported cancellation/supervision behavior; do not promote cooperative cancellation into an arbitrary publisher deadline guarantee. No new unavailable-collector test is needed merely because the old report predates it. |
| T2/S1/readiness: runnable product and independent adoption | The sample inventory below shows repeated hosts and a standalone-to-Wholesale project reference. Current [template](../../templates/state-stored/README.md) intentionally supports a bounded source-snapshot composition, not every current sample feature or released NuGet adoption. | Complete the selected template rehearsal, sample consolidation and clean-consumer/package rehearsal before claiming coherent v1 delivery. These belong to existing template/readiness work rather than a new feature family. |

The first row is a confirmed missing selected capability. The next three are source-backed
operational/evidence obligations, with no new runtime bug claim or test result. They must be
closed by the owning work and final composition, or receive an explicit owner-approved change
to the v1 contract. Do not silently classify them as post-v1 merely because their original
slice was merged. The reviewed functional mechanisms and their existing proofs remain valid.

## Gaps that must not be reopened from stale reports

- Earlier W1/OBS1 reports list poison handling, redrive, queued compatibility and backlog
  visibility as future work. Current M2 supplies inbox/outbox dead letters and supervision,
  outbox observations, completed-outbox cleanup and one schema-evolution journey. Only inbox
  retention is the selected outstanding M2 retention work. Historical reports remain dated.
- A1/A2 assessment-era wording does not erase later membership administration, Sales
  resource-permission queries, optional RLS and HTTP/worker adoption. Their bounded runtime
  and role/pooling constraints remain explicit. No evidence here requires automatic RLS
  rollout to every module, provider failover support or a new authorization engine.
- ES2's early lack of full-operation execution is superseded by ES3. Full in-memory history,
  uninterrupted synchronous reducers and possible conflict exhaustion are documented limits,
  not a promise of bounded large-stream latency or a request for another scheduler.
- EventReactions deliberately covers explicitly collected sources and serialized local
  reactions. SQL bypass, external effects and complex orchestration are outside that protocol;
  no missing universal dispatcher is inferred.
- WF2's lack of parallel joins, timers, automatic workflow upgrades and durable operator-signal
  receipts is explicit. Existing SignalAsync requires fresh-state reconciliation after response
  loss. These facts do not by themselves select another workflow-engine feature for v1.
- Inbox backlog gauges/automatic alerts are absent; outbox observations are not inbox coverage.
  The final host must make stopped/faulted processing operationally visible, but a new inbox
  metrics/store package is not automatically required. Native host failure/readiness/restart
  can supply the selected consumer contract. Choose and verify it with the final composition.
- Generic scaling, extra transports/providers, AOT, denials/completeness audit policy and
  automatic actor/tenant propagation are not selected merely by appearing in a deferred list.
  Their original explicit boundaries remain; promotion needs an owner scope decision.

## Sample and experiment inventory

At this baseline there are **46 sample .csproj files: 33 under Wholesale and 13 outside it**.
Wholesale includes eleven distinct *Demo host directories, AppHost, ServiceDefaults, module
projects and test projects. Multiple executable roles can be legitimate; the count alone
is not a recommendation to delete projects or tests.

- Root standalone examples: EventReactionsDemo (model, consumer, tests), EventStorageDemo,
  OutboxDemo, InboxDemo, MessagingDemo, MessagingProducerDemo and MessagingWorkerDemo, plus
  associated test projects. These primarily demonstrate independent Gloaming adoption.
- Wholesale separates ContextDemo, PersistenceDemo, EventCodecDemo, EventPersistenceDemo,
  HttpIdentityDemo, DddDemo, RichAggregateDemo, DomainEventsDemo, CqrsDemo, WorkflowDemo and
  LocalWorkflowDemo. Most already share the module projects; product composition and entry
  points still remain fragmented into historical slice hosts.
- [MessagingProducerDemo](../../src/Gloaming.Messaging/samples/MessagingProducerDemo/MessagingProducerDemo.csproj) and
  [MessagingWorkerDemo](../../src/Gloaming.Messaging/samples/MessagingWorkerDemo/MessagingWorkerDemo.csproj) reference
  Wholesale.ServiceDefaults. This is concrete sample-composition coupling to remove when
  independent examples move into their owning family. Preserve the actual ServiceDefaults/OTel
  integration proof without making the library family depend on the Wholesale product.
- [CI](../../.github/workflows/ci.yml), [path filters](../../.github/ci-paths.yml), the solution,
  architecture checks, template scripts and many docs encode current paths. Moving a folder
  alone is insufficient. Tests at family, independent-adoption and product levels can prove
  different obligations even when they share a scenario name; preserve those distinctions.

The [S1 consolidation scope](../plans/s1-sample-experiment-consolidation.md) records the
owner-requested target, proof-preserving relocation/deletion map and archive retirement.
It is selected pre-v1 repository/product work, not a generic cleanup framework.

## Verification and limits

Inspected current family contracts, closure sections in the W1/OBS1/WF2/ES3/D2/M2 reports,
workflow version handling/tests, worker lifetime code, sample project references, template
limits and archive tooling references. Whitespace and new local links are checked with this
change. No C# build, database/broker outage, runtime tests or independent code review ran.
The planned post-cleanup code and architecture reviews remain separate future verification.
No implementation, sample movement, archive deletion, index mutation or commit is performed.
