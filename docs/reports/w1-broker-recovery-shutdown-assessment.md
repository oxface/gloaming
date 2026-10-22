# W1/M2 and W1/OBS1 operating-boundary assessment

Date: 2026-10-22. Status: preparation for
[concrete consumer-scope review](../plans/w1-broker-recovery-shutdown.md).
No runtime/test implementation or fresh PostgreSQL/RabbitMQ result is claimed here.

Follow-up: the owner approved this scope and the
[implementation/closure report](w1-broker-recovery-shutdown.md) records subsequent native
executions. Findings below remain the preparation baseline, not the final test result.

## Baseline and audit reconciliation

Fetched origin/main is 2e1ffac, the inbox-retention merge. Its tree matches the clean default
checkout's approved checkpoint 6510f9e. The existing index was empty. The new
fix/worker-broker-recovery branch starts from that fetched base without tracking origin/main;
other worktrees and their review indexes were not changed.

Read the orchestration worktree's docs/reports/v1-slice-closure-audit.md, dated 2026-10-22,
baseline eeaa25b, from `/home/oxface/.codex/worktrees/orchestration/modulith-foundry`.
That audit is independently maintained and not yet in this branch; this assessment does not
copy or modify its pending work. Its selected findings were checked against current merged
source rather than adopted solely from the audit or chat history.

| Audit obligation | Current disposition |
| --- | --- |
| Inbox retention/deduplication expiry | Implemented and merged as 2e1ffac. Owner approved the five post-v1 supplied-feature deferrals recorded by checkpoint 6510f9e. Those deferrals do not close real broker recovery or the selected shutdown proof. |
| W1/M2 live transport recovery | Still open: current channel ownership/retry behavior confirms the source-backed risk below. No live broker outage was reproduced during preparation. |
| W1/OBS1 stalled publication/shutdown | Still open for the selected native publication phase. Existing unavailable-collector/business-progress and normal stop tests must be reused and accurately bounded. |
| WF2 definition deployment and T2/S1/readiness | Separate audit obligations; this request does not authorize implementing or relocating them. |

## Current code and existing evidence

- [BrokerSession](../../samples/MessagingWorkerDemo/BrokerSession.cs) explicitly sets
  AutomaticRecoveryEnabled=false and opens one persistent confirm-tracked channel. Program
  shares that session across sequential scoped dispatch/intake operations. Startup passively
  checks the queue; only the separate setup role declares it.
- [WorkerPublisher](../../samples/MessagingWorkerDemo/WorkerPublisher.cs) constructs the
  existing native adapter over the same channel for every operation. A new database scope
  does not recover that channel. The [generic worker](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore/OutboxWorker.cs)
  catches ordinary publication exceptions, waits and retries; unconfirmed accounting/completion
  exceptions follow its distinct fail-stop path. Idle NoWork polls do not access the broker.
- [RabbitMqIntakeWorker](../../samples/MessagingWorkerDemo/RabbitMqIntakeWorker.cs) already
  logs failures, sets exit code 1 and requests host stop. Requested cancellation ends normally.
  A receive awaiting PostgreSQL does not poll a broken broker session at that instant.
- [Native publication](../../samples/MessagingDemo/RabbitMqExportPublisher.cs) sets persistent
  delivery and stable MessageId/schema/business headers, publishes mandatory with confirmations,
  and uses a linked five-second cancellation timer. No arbitrary-publisher deadline follows.
- [Dispatcher](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/PostgresOutboxDispatcher.cs)
  commits the lease before publication, preserves a claim on requested publication cancellation,
  accounts publication exceptions under fencing and distinguishes unconfirmed completion.
  It must not be changed to equate broker disconnect with rejection or to force early replay.
- [Process tests](../../samples/MessagingWorkerDemo.Tests/WorkerProcessTests.cs) prove native
  competition, SIGTERM during a blocked job write, killed processors and dispatcher death after
  confirmation with actual lease-expiry recovery. Those are not a live broker restart proof.
- [ObservabilityTests](../../samples/MessagingWorkerDemo.Tests/ObservabilityTests.cs) already
  launches independent producer/dispatch/intake/process roles with unreachable OTLP endpoint,
  a one-second exporter timeout and real PostgreSQL/RabbitMQ progress, then stops them. Its
  existing ninety-second whole-scenario deadline is not a separately demonstrated stop budget.
  The [OBS1 report](obs1-durable-message-observability.md) explicitly avoids a universal shutdown bound.
- BrokerSession currently disposes channel before connection, with no selected explicit
  broker-close window. Resource lifetime and native shutdown callbacks need to be tested with
  the stalled transport; cancelling a host is not a process-wide arbitrary-code abort facility.

## Pinned native inspection and extraction finding

Directory.Packages.props pins RabbitMQ.Client **7.2.2**. Inspected its exact local net8.0
assembly/XML using dotnet-inspect 0.26.0 and the restored package, not a latest-version API
assumption. Native IConnection.ConnectionShutdownAsync and IChannel.ChannelShutdownAsync
document immediate notification when a handler subscribes after destruction. Native
ConnectionBlockedAsync/ConnectionUnblockedAsync expose broker resource blocking. The
timeout-taking IConnectionExtensions.AbortAsync documents socket closure when the close
window expires. These are native contract/source-inspection findings, not process timing proofs.

Recommend small consumer-owned terminal-session observation and supervised process restart,
with intentional cancellation distinguished before finite native broker teardown. An in-process
reconnect state machine would add session replacement, channel handoff and in-flight ambiguity
obligations not required by the existing separately hosted roles. No new transport manager,
health framework, scaling mechanism or Gloaming public interface is justified by this assessment.

No new reusable mechanism was proven during preparation. No tests were run. Existing dated
W1/OBS1/M2 reports remain historical executions; current test definitions establish only the
proof obligations described above until rerun. The scope lists executable adoption, native
broker/process proofs, exact cancellation/supervision limits and final closure checks after review.
