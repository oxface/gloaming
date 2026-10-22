# WF1: durable inter-module workflow

Date: 2026-10-10. Owner-approved checkpoint `e05ae58`, merged into `origin/main` as `161a33e`.
The original `feat/durable-inter-module-workflow` branch was based on `4c0e0c5`;
its index was preserved until the owner explicitly authorized the checkpoint.
The implementation agent created no push or pull request. This branch also corrected OBS1's status to record its
owner-approved checkpoint `b44b5d5` and merge at `4c0e0c5`.

## Outcome and ownership

Sales now owns persistent stock-issue request progress. Start commits one stable Inventory
command with its request. Inventory uses its existing stock decision, event/inline-state/audit
transaction and success/refusal replies. Sales processes the reply with its inbox completion
in another native transaction. The executable uses separate module databases, real RabbitMQ
and the existing Gloaming inbox/outbox workers. A separate Sales deadline lane marks overdue
requests NeedsAttention; valid late replies resolve them to Issued or Declined.

Request identity is scoped by Organization. Command MessageId remains stable across start
retries and publication redelivery. Request ID travels as CorrelationId, and Inventory replies
carry command identity as CausationId. Reply payload MessageId identifies the reply itself.
Sales matches admitted producer/tenant, schema, operation identities and decision fields.
Equivalent semantic replies under new delivery IDs do not repeat a transition; incompatible
inputs/outcomes fail without mutating progress or completing the offending inbox item.

Inbox locks alone cannot arbitrate updates to the same request from different deliveries or
deadline scans. A native EF Version token protects that business row. Losing transactions
roll back; provided inbox workers and the consumer deadline poller retry in fresh scopes.
Timestamps are normalized to UTC microseconds so a .NET value remains semantically equal after
PostgreSQL persistence. The first retry proof found this precision obligation and the final
suite exercises it.

**No new reusable mechanism was proven or extracted.** Existing Gloaming enqueue, lease,
retained intake, row-locked processing, telemetry and hosted-worker mechanisms compose the
workflow. Request transitions, semantic idempotency, timeout meaning and admission remain
consumer policy. There is no saga base class, handler registry, scheduler, common unit of work
or cross-module transaction. A materially different workflow is still needed before proposing
shared workflow mechanics.

## Review-worthy files

- [Business Contract](../../samples/Wholesale/modules/Sales/Sales.Contracts/IStockIssueRequests.cs):
  explicit start/read, immutable progress and assigned enum values; no EF or context types.
- [Request state](../../samples/Wholesale/modules/Sales/Sales/StockIssues/StockIssueRequestRow.cs),
  [operations](../../samples/Wholesale/modules/Sales/Sales/StockIssues/StockIssueRequests.cs) and
  [mapping](../../samples/Wholesale/modules/Sales/Sales/StockIssues/StockIssueRequestMapping.cs):
  ordinary consumer state, owned identity, stable command, semantic comparisons and versioning.
- [Reply admission](../../samples/Wholesale/modules/Sales/Sales/StockIssues/StockIssueReplyAdmission.cs)
  and [handler](../../samples/Wholesale/modules/Sales/Sales/StockIssues/StockIssueReplyHandler.cs):
  explicit codec and matching policy; leave final save/completion/commit to the inbox processor.
- [Deadline operation](../../samples/Wholesale/modules/Sales/Sales/StockIssues/StockIssueDeadlines.cs):
  bounded native query and separate local transaction; no remote cancellation or stock reversal.
- [Sales context](../../samples/Wholesale/modules/Sales/Sales/SalesDbContext.cs),
  [registration](../../samples/Wholesale/modules/Sales/Sales/SalesRegistration.cs), project references,
  additive `AddStockIssueWorkflow` migration and current model snapshot: separately owned
  storage and used dependencies. Previous migration files were preserved.
- [Inventory admission](../../samples/Wholesale/modules/Inventory/Inventory/Messaging/StockIssueMessageAdmission.cs):
  additionally accepts the host-bound Sales producer while retaining the earlier demo producer.
  Existing stock Contracts, decisions, persistence and reply mapping were not changed.
- [Executable host](../../samples/Wholesale/Worker/Broker/BrokerHost.cs),
  [composition](../../samples/Wholesale/Worker/Broker/WorkflowComposition.cs),
  [native broker adapter](../../samples/Wholesale/Worker/Broker/WorkflowBroker.cs) and
  [pollers](../../samples/Wholesale/Worker/Broker/WorkflowPollingWorkers.cs): existing library
  workers plus explicit transport/deadline policy. Finite setup/seed are separate from startup;
  missing tables/queues fail startup without provisioning them.
- [Behavior proofs](../../samples/Wholesale/IntegrationTests/Reservations/Broker/WorkflowTests.cs),
  [concurrency/process recovery](../../samples/Wholesale/IntegrationTests/Reservations/Broker/WorkflowRecoveryTests.cs),
  fixture and database barrier: independent expected outcomes, failure constraints, observed
  database waits and actual killed/restarted executables. Reuses the existing child-process
  helper through a compile link; no new repository script.

The solution, architecture project/rules and CI path map include the new consumer. The
Messaging lane runs its tests. Module/library/Contracts types cannot depend on the workflow
host. Sample/module and Messaging-family documentation link the executable and declare limits;
the [scope map](../plans/wf1-durable-inter-module-workflow.md) records the complete change areas.

## Fresh verification

Local .NET SDK 10.0.401; native PostgreSQL 18.6 and RabbitMQ 4.3.6 Testcontainers through Podman.
Tests used the installed MTP runner with progress/ANSI disabled and failed-output capture.

| Suite | Passed |
| --- | ---: |
| New WF1 workflow/transport/concurrency/process proofs | 29 |
| Architecture and independent boundaries | 81 |
| Existing Wholesale HTTP/profile/audit consumers | 105 |
| Existing Inventory/Purchasing event-persistence consumers | 190 |
| Existing PostgreSQL inbox contract | 34 |
| Existing PostgreSQL outbox contract | 36 |
| Existing module persistence consumers | 36 |
| Total distinct tests across these suites | 511 |

The workflow suite proves the real success and all three Inventory refusal paths; stable
start identity; start/command and reply/completion rollback with fresh-scope recovery;
native command redelivery; transport/semantic reply duplicates; conflicting and unrelated
replies; malformed transport rejection; Organization separation; bounded deadlines and
reordered independent replies. Simulated replies in focused policy/race tests are explicitly
separate from the real broker/Inventory decision journey.

Observed database barriers prove competing starts commit one command; concurrent semantic
replies on separate inbox rows resolve progress once; and deadline/reply races in both orders
roll back the stale update and resolve the actual outcome on fresh retry. Separate processes
prove death after Inventory commits but before Sales saves, and death during an uncommitted
deadline update. Restart recovers retained work and overdue state without another stock issue.
The executable-command proof also runs finite setup, seed, start, read and the hosted journey.

The first broad parallel local run exhausted the machine's shared inotify instance limit in
one existing HTTP test (104/105 passed). The full HTTP rerun passed 105/105 with
`DOTNET_USE_POLLING_FILE_WATCHER=1`. This was an environment-only rerun; no HTTP/CI behavior was
changed to conceal a failure. Local MSBuild parallelism was bounded to two after an initial
build-node failure. These are local verification conditions, not new library guarantees.

The active solution builds with zero warnings/errors. Native semantic style and analyzer
verification pass; CSharpier passes including generated files. `git diff --check` passes.
Archive verification retains all 800 original files from `15d1ec6`; no archived source,
fixture or test was changed. Changed-document local links and CI YAML are checked separately.
Hosted CI has not run for this uncommitted change set.

## Historical evidence and remaining gaps

O1/I1/W1/OBS1 reports remain the evidence for their original library contracts and topologies.
Fresh reruns above verify compatibility; WF1's new proofs do not establish new exactly-once,
transport, telemetry-export or provider guarantees. The archive's fulfilment saga remains
historical comparison, not an adopted runtime or tested active workflow.

NeedsAttention reports uncertainty, not failure. No cancellation, reservation/release,
compensation, remote outcome query or operator reconciliation is supplied. Start retries after
native uniqueness/ambiguous commit failures require the original inputs and a fresh scope.
The deadline uses host TimeProvider and a finite consumer Organization list; global scheduling,
clock-drift policy, partitioning and scaling are not claimed.

Shared demo broker credentials do not authenticate a module. Deployment owns queue bindings,
permissions and trusted producer identity. Malformed intake is rejected without requeue;
configure a DLQ before deploying that policy. Invalid processing outcomes remain pending and
retry under existing inbox behavior. Poison quarantine, attempt caps, redrive and retention
need the separately planned operations slice. No business permission API or automatic human
initiator propagation was added.

No template preset was added; T1 remains event/messaging-free. Worker placement stays editable
composition. WF1 intentionally uses one small process rather than recreating the archive's
fulfilment domain. After review, B1's safe retained-feed cursor is the next recommended runtime
slice; snapshot/feed repopulation and aggregate maintenance remain distinct later outcomes.
