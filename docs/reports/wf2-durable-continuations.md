# WF2: bounded durable continuations

Date: 2026-10-12. Owner-approved public interfaces and replacement scope implemented for
line-by-line review on `feat/compensating-workflow`. No checkpoint has been made for this
replacement; the owner's staged sequential-experiment snapshot remains byte-for-byte intact.

## Outcome

Optional `Rootbolt.Messaging.Workflows` and `Rootbolt.Messaging.Workflows.EntityFrameworkCore`
replace the uncommitted fixed-sequence saga experiment. The runner loads a provided process,
matches the requested operation, invokes only its saved typed continuation and tracks consumer
state plus one returned outgoing request in the owning native EF transaction. It does not replay
Start, interpret a graph, save, commit or publish. Existing inbox/outbox workers remain effective.

Sales uses ordinary methods for first reservation, second reservation, release and authorized
operator retry. Its public business Contracts, Inventory implementation and stable reservation
identities are retained. A materially different standalone adopter inspects an existing resource
instead of creating one; activation refusal compensates only a resource the workflow created.
It uses its own schema, aliases, JSON options and tenantless metadata, without sample Contracts,
Inventory, EventSourcing, actor/tenancy packages or RabbitMQ. Its external resources are fake
reply contracts, not a real provisioning service.

This proves a new reusable mechanism: selected-method execution and retained reply/request
bookkeeping with atomic local state/outbox composition. The library removes process lookup,
command matching, technical outcome recording and next-request enqueue details from Sales's
handler. Business compensation remains explicit C#, without a generic reverse-step interpreter.
The added provided storage/runner is justified by both consumers; scheduling, parallel requests
and replay engines are not introduced speculatively.

## Mechanisms and policy

| Concern | Owner and evidence |
| --- | --- |
| Start identity and equal-input detection | Runner retains original input and returns current state without reinvocation/new IDs. Changed input fails. |
| Pending operation and selected method | Definition binds named typed replies; runner matches tenant/correlation/causation and wire alias/schema before execution. |
| Duplicate/conflicting outcomes | Technical retained operations compare producer/alias/schema/semantic JSON, independent of delivery/trace IDs. Equal older outcomes do not replay; conflicting outcomes fail. |
| Branching and compensation | Ordinary Sales/workspace continuation methods; business rules and full resource reply validation remain consumer policy. |
| Stable business identity versus technical retry | Consumer state retains resource IDs; runner allocates new command IDs and retains old outcomes across explicit operator resumption. |
| Candidate isolation and serialization | Runner restores a detached typed state and serializes before tracked proposals; handler/serializer failure leaves loaded process unchanged. |
| Process/request atomicity | Provided save guard plus caller transaction or existing inbox processor. Real PG proves state/next outbox/inbox completion together and rollback after outgoing constraint failure. |
| Competing writers | Scalar Version concurrency token and original TenantKey. Native competing transactions commit one version/request; loser rolls back and discards scope. |
| Tenant/source/actor authority | Consumer admission, not metadata. Runner filters optional TenantKey but does not authenticate producer or actor; Sales keeps existing admission. |
| Delivery and acknowledgement | Existing supplied inbox/outbox primitives/workers; sample owns RabbitMQ routes, receiver and ack after durable intake. No workflow-specific worker. |
| External input and operator recovery | Saved typed signal binding; consumer owns admission, authorization and explicit save/commit. Direct signals have no durable signal receipt. |

## Review-worthy code and scope

- [WorkflowBuilder](../../src/Rootbolt.Messaging/Rootbolt.Messaging.Workflows/WorkflowBuilder.cs),
  [WorkflowTurn](../../src/Rootbolt.Messaging/Rootbolt.Messaging.Workflows/WorkflowTurn.cs),
  [IWorkflow](../../src/Rootbolt.Messaging/Rootbolt.Messaging.Workflows/IWorkflow.cs): reviewed
  typed API with XML documentation, copied/frozen options and immutable bindings.
- [EfWorkflow](../../src/Rootbolt.Messaging/Rootbolt.Messaging.Workflows.EntityFrameworkCore/EfWorkflow.cs):
  explicit start/continue/resume/read lanes, detached candidates and one accepted turn per instance/scope.
- [Provided record](../../src/Rootbolt.Messaging/Rootbolt.Messaging.Workflows.EntityFrameworkCore/WorkflowInstanceRecord.cs)
  and [save validation](../../src/Rootbolt.Messaging/Rootbolt.Messaging.Workflows.EntityFrameworkCore/WorkflowSaveChangesExtensions.cs):
  scalar version, owned JSON, native transaction/request binding and pooled-scope registry lifetime.
- [Sales methods](../../samples/Wholesale/modules/Sales/Sales/ReservationOrders/ReservationWorkflow.cs),
  [application](../../samples/Wholesale/modules/Sales/Sales/ReservationOrders/StockReservationOrders.cs),
  [reply handler](../../samples/Wholesale/modules/Sales/Sales/ReservationOrders/StockReservationReplyHandler.cs)
  and [message helpers](../../samples/Wholesale/modules/Sales/Sales/ReservationOrders/ReservationMessages.cs):
  consumer rules and transaction/ingress/routing ownership are visible.
- [Standalone consumer](../../src/Rootbolt.Messaging/tests/WorkflowPostgresTests/WorkspaceConsumer.cs)
  and [PG proofs](../../src/Rootbolt.Messaging/tests/WorkflowPostgresTests): ordinary branching
  adoption and executable technical protocol.
- Only the uncommitted Sales WF2 migration draft/designer/current snapshot were regenerated from
  the last committed snapshot. Same migration identity; new `sales.workflow_instances` table.
  Earlier Sales and all Inventory migrations/fixtures are preserved. No database was reset.
  Test databases are independently created and migrated using native EF.
- The existing sample startup SQL probe was mechanically updated to the replacement table as
  implementation follow-through. No host/worker protocol changed. Tests' table/ledger barriers
  now target the provided row and scalar Version, preserving public failure/recovery assertions.
- Solution/CI and architecture declarations select the two replacement packages/test projects.
  Existing family path filtering is retained. Existing Messaging packages acquire no workflow
  dependency. Core depends only on Messaging/BCL; adapter uses existing EF messaging roles,
  EF Relational and DI abstractions. No vendor/transport module dependency is added.
- Self-sufficient [library contract](../../src/Rootbolt.Messaging/docs/workflows.md), package
  READMEs, Sales usage, design/roadmap and [ADR 0014](../adr/0014-bounded-durable-continuations.md)
  record the supported model and supersede the sequential experiment. No template preset added.

## Fresh verification

Executed against .NET SDK 10.0.401, PostgreSQL 18.6 and RabbitMQ 4.3.6 through local Podman.
Commands use the repository's MTP options. Ordinary builds use `-m:2`; tests run built projects.
Reproduce the core protocol with:

```sh
dotnet build ModulithFoundry.slnx -m:2
dotnet test --project src/Rootbolt.Messaging/tests/WorkflowPostgresTests/WorkflowPostgresTests.csproj --no-build --no-restore --no-progress --no-ansi --output Normal --show-stdout Failed --show-stderr Failed
dotnet test --project samples/Wholesale/WorkflowDemo.Tests/WorkflowDemo.Tests.csproj --no-build --no-restore --no-progress --no-ansi --output Normal --show-stdout Failed --show-stderr Failed
```

Local child-host runs retain `DOTNET_USE_POLLING_FILE_WATCHER=1` for this machine's watcher limits.

| Check | Fresh result |
| --- | --- |
| Workflow definition/return contract | 8 passed |
| Standalone continuation protocol on real PostgreSQL | 38 passed |
| Sales/Inventory/RabbitMQ full workflow journey | 62 passed |
| Architecture, optional dependency/adoption and module isolation | 82 passed |
| Existing outbox PostgreSQL suite | 36 passed |
| Existing inbox PostgreSQL suite | 34 passed |
| Existing module persistence/migration suite | 36 passed |
| Existing event-persistence consumer suite | 190 passed |
| Active solution build, style/analyzers and root formatter | Passed; build has zero warnings/errors; 625 formatter files checked |
| Frozen archive integrity and unchanged index | 800 original files verified; staged binary diff unchanged |

The eight test suites above total **486 passed, zero failed/skipped** in their final runs.
Earlier failed setup/probe runs are recorded below rather than counted as successful evidence.

New protocol proofs cover existing/create branches, consumer-specific compensation, fresh-context
selected-method execution, equal starts, changed starts, semantic duplicates with reordered JSON,
conflicting/unknown/mismatched replies, required fields, tenant isolation, definition mismatch,
operator retry/older refusal, wrong operator signal, missing binding/model/native transaction,
ambient rejection, cancellation, rollback after native save, competing writers, changed/detached
proposals, original concurrency-token tampering, direct edits, transaction switching, scope reuse/pooling, candidate mutation, immutable
definition options/bindings and serializer failure. Existing inbox processing proves atomic next
request/state/completion and fresh-scope recovery after an outgoing database constraint fails.

The transferred Sales assertions retain real stock-event/receipt behavior, original refusal and
compensation failure, admission, two reply writers, database/broker outages, finite executable
commands and killed-host recovery after Inventory committed a release before Sales completion.
These are new execution results for the replacement, not inherited claims from the old run.

Intermediate checks caught a zero-delay inbox-test setup error and a missed old-table startup
probe. The latter caused five full-sample child-host timeouts; polling alone did not resolve them.
After the probe repair the complete sample suite passed. No arbitrary retry/delay increase or
weakened assertion was used to make it pass.

## Historical evidence and deferred gaps

The [initial consumer-only report](wf2-compensating-workflow.md) and discarded
[sequential-coordinator report](wf2-sequential-saga-coordinator.md) retain their dated results.
Their earlier 584-check run is not counted as proof for the replacement. Fixed-order reverse
undo, NotRequired/Unsupported compensation flags, plan interpretation and the progress JSON
converter are retired implementation/policy tests: the new library owns neither a fixed sequence
nor generic compensation choice. Business branching/compensation is newly exercised by both
adopters; mutable tracker safety and native competing-writer/rollback obligations are re-proven
against provided storage and scalar concurrency rather than inferred from those retired tests.

One pending request, retained JSON operations and one bound definition version are supported.
No ledger pruning/throughput claim, parallel joins, deadlines, cancellation protocol, scheduling,
workflow state migration, durable operator signal receipts, workflow telemetry source, graph or
replay engine, transport or generic compensation algorithm is supplied. PostgreSQL is verified;
other providers need their own JSON/transaction/concurrency proof. Save guards constrain native
tracked proposals only, not raw SQL or deliberately bypassed integration. Methods must not do
external effects or save/commit themselves. Ambiguous commits require fresh reads using retained
identities. An installed discarded migration draft would need an explicit forward migration.

No unrelated staged edits, frozen archive, fixtures or checkpointed migrations were removed.
All replacement changes remain unstaged for owner review; no staging, commit or push occurred.

## Naming refinement: 2026-10-13

The owner approved clearer reply/input names after code review:

| Previous name | Current name |
| --- | --- |
| ContinueAsync | HandleReplyAsync |
| ResumeAsync | SignalAsync |
| OnResume | OnSignal |
| WorkflowTurn.NeedsIntervention | WorkflowTurn.WaitForInput |
| WorkflowStatus.NeedsIntervention | WorkflowStatus.AwaitingInput |

Core, native EF implementation, Sales, standalone consumers, tests, XML documentation,
current usage and interface proposal now use the reviewed names. The input pause can carry
typed external inputs; operator retry remains the demonstrated business adoption. No HITL
UI, durable signal receipt or automatic input worker was added. Persisted status numbers,
continuation names, record shape and migration drafts are unchanged.

Redundant qualifications were also removed from four workflow source files. This follow-up
proves no new reusable mechanism; it refines the existing contract and retains the dated
486-check implementation evidence above. Fresh renamed-API verification passed: 8 core
checks, 38 PostgreSQL protocol checks and 62 Sales/Inventory/RabbitMQ checks, totaling 108
passed with zero failures/skips. Active solution build, semantic style and the 625-file root
formatter check passed. No new tests were added for the naming-only refinement.

The owner's staged snapshot is preserved; all refinement changes are working-tree edits.
