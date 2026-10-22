# W1/M2 broker recovery and W1/OBS1 shutdown closure

Date: 2026-10-22. Branch `fix/worker-broker-recovery`, based on fetched inbox-retention
merge `2e1ffac`. The owner approved the [concrete consumer scope](../plans/w1-broker-recovery-shutdown.md)
before implementation. Changes remain unstaged and uncommitted for exact change-set review.

Subsequent owner-approved continuation: the [RabbitMQ lifecycle interface](../plans/rabbitmq-session-lifecycle.md)
is implemented in an optional package, with [separate fresh extraction/adoption evidence](rabbitmq-session-lifecycle.md).
The results below remain the earlier consumer-only proof; they do not substitute for those
new executions or rule out useful shared mechanics.

## Outcome and operating contract

The standalone dispatch/receive roles now fail-stop on unexpected native connection/channel
destruction, including idle dispatch and intake blocked in PostgreSQL. One diagnostic/stop
decision sets exit code 1; native callbacks never await teardown. An external supervisor
must restart the role with a fresh session after repair. Automatic recovery remains disabled.
Intentional stop suppresses teardown's terminal notifications; a previously observed broker
failure keeps its nonzero status. Temporary resource alarms leave ordinary retry intact.

Consumer-native cleanup first aborts the connection with a five-second close window, then
disposes resources with finally protection. The selected native protocol continuation timeout
is also five seconds, including channel-zero close during disposal and startup declarations.
The existing five-second publication timer is separate. None establishes an arbitrary-publisher
deadline, universal exporter flush bound or certainty about transport rejection.

Publication remains outside PostgreSQL transactions. No lease is renewed/cleared early, no
identity is regenerated, and failure/claim accounting meanings are unchanged. A cancellation
can leave an abandoned claim with zero observed failures. Replacement waits for database-clock
expiry, then claims under a fresh fence. Missing confirmations and post-acceptance completion
failure remain ambiguous; receiver deduplication is required while receipts are retained.
Receipt deletion still permits old delivery to execute again.

See [family recovery/cancellation obligations](../../src/Gloaming.Messaging/docs/worker-recovery.md)
and [executable adoption](https://github.com/oxface/modulith-foundry/blob/56a8b12857d1cffb842d839238784c607b66e474/src/Gloaming.Messaging/samples/MessagingWorkerDemo/README.md). Supervision must restart
unexpected exits including status 0 from existing native StopHost paths, and own external
grace/force-termination policy for noncooperative code. No deployment supervisor is supplied.

## Fresh proof and diagnosis

All results here are executions for this continuation, separate from historical W1/OBS1/M2
counts. Real PostgreSQL 18.6 and RabbitMQ 4.3.6 run through rootless Podman; child processes
execute the actual built producer and worker binaries. RabbitMQ.Client remains pinned at
7.2.2. Outage/alarm controls run only against this class's dedicated broker fixture.

| Proof | Observed result |
| --- | --- |
| Live outage/fresh-session recovery | First publication is confirmed and completion committed; dispatch is idle. Intake is held after its actual INSERT, before commit/ack, by a PostgreSQL advisory-lock trigger. Native `stop_app` terminates both roles with status 1 within the separate stop budget; intake rolls back. A producer commits new work during outage, and dispatch startup fails without claiming it. Native `start_app` preserves the same durable queue. Replacement intake receives the earlier message before new dispatch starts; fresh roles complete both original identities/jobs. |
| Native blocked publication/SIGTERM | A one-byte native memory watermark raises a real resource alarm. Publication-start and connection-blocked notifications plus committed claim state establish the stalled phase. SIGTERM while the alarm stays raised exits 0 within fifteen seconds, with no confirmation or ordinary-failure diagnostic. Attempts/Revision remain 1, failure count 0, and the claim remains. After alarm restoration and actual lease expiry, replacement confirms the same envelope; final Attempts 2/Revision 3, no failures, one retained receipt/job. Native queue totals include unacknowledged work before intake is stopped. |
| Ordinary confirmation timeout | Keeping the native alarm raised lets the existing attempt timer produce a durably accounted failure. The session stays alive. Removing the alarm allows the same process/session to confirm a later attempt. Any buffered/repeated acceptance is handled under the same identity with one retained receipt/job. No exact broker-delivery count or global attempt cap is asserted. |
| Unavailable collector reconciliation | The existing independent producer/dispatch/intake/processing scenario is reused, retaining unreachable endpoint and one-second exporter timeout. All three worker roles stop successfully under one separate fifteen-second budget after business completion. No duplicate unavailable-collector business journey was added. |
| Existing distinct failure protocols | The complete worker process suite reruns acceptance followed by completion failure, killed confirmed publication/actual lease expiry, processing rollback, accounting-failure supervision, original receipt-generation retry and schema-context recovery oracles. Native receiver commit-before-ack and stale lease/accounting fences are also covered by the relevant Messaging suites. |

The first outage proof failed on the unchanged host: idle dispatch remained alive after
terminal broker loss. Native session observation made that proof pass.

The stalled-publication proof then reproduced a separate teardown wait. Temporary tagged
boundary diagnostics showed host cancellation/return, connection abort return and channel
disposal complete, followed by a wait in connection disposal. The pinned local XML/native
API and official [v7.2.2 connection](https://github.com/rabbitmq/rabbitmq-dotnet-client/blob/v7.2.2/projects/RabbitMQ.Client/Impl/Connection.cs)
and [channel](https://github.com/rabbitmq/rabbitmq-dotnet-client/blob/v7.2.2/projects/RabbitMQ.Client/Impl/Channel.cs)
source identify the separate channel-zero close continuation. SourceLink PDB acquisition
was unavailable; no checksum-verified authored-source claim is made. Selecting the native
ContinuationTimeout resolved the reproduced process wait. All temporary diagnostics were
removed before the complete process-suite execution. No library or dependency patch was needed.

## Verification

The final complete worker suite passes **28 cases**, including three new broker cases and
the tightened existing collector case, in **77.6 seconds** including fixture lifecycle.
The initial targeted failure/success loops are diagnosis evidence, not additional final cases.

The complete Messaging CI selection passes **530 tests**, including that worker suite.
Architecture/adoption checks add **96**, for **626 passed, zero failed/skipped** in this
continuation's final verification selection. These are relevant checks, not a claim to have
run every unrelated repository test.

| Final suite/check | Result |
| --- | --- |
| MessagingTests / WorkflowTests | 8 / 8 passed. |
| WorkflowPostgresTests | 38 passed. |
| OutboxPostgresTests | 102 passed, including lease fencing, restricted dispatch authority, uncertain completion and accounting supervision. |
| InboxPostgresTests | 93 passed, including retained intake/processing/retry/expiry protocols. |
| OutboxDemo.Tests / InboxDemo.Tests | 25 / 43 passed, including native transport/intake and authorized recovery adoption. |
| MessagingWorkerDemo.Tests | 28 passed in 77.6 seconds. Three new broker cases; existing telemetry/exporter/business oracles retained. |
| Wholesale WorkflowDemo.Tests / LocalWorkflowDemo.Tests | 152 / 15 passed. |
| EventPersistenceDemo.Tests, CI `*DispatchTests` selection | 18 passed. The full unrelated event-persistence selection was not rerun. |
| ArchitectureTests | 96 passed. |
| Active solution build | Passed on SDK 10.0.401, zero warnings/errors. |
| Native style / analyzers | Both passed for the active solution with verify-no-changes. |
| CSharpier, including generated code | Passed, 921 files. |
| Frozen archive | Original 800-file snapshot verified; no archive changes. |
| Documentation / Git | At the consumer-only verification handoff, local links and whitespace passed; empty review index preserved, all 23 then-changed/new files unstaged. Subsequent extraction-review documents have their own scope. |

The ten other Messaging suites follow the current [.github/workflows/ci.yml](../../.github/workflows/ci.yml)
selection, using already-built binaries with `--no-build --no-restore`. Infrastructure/native
workspace checks ran outside the filesystem sandbox where its local sockets/pipes were
unavailable. The initial sandbox-only build/style failures are environment restrictions, not
successful checks; the listed final build/style/analyzer/test commands completed successfully.

Reproduce the native process suite on Linux with Docker, or a configured rootless Podman
socket (`DOCKER_HOST` and `TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE`; disable Ryuk if required):

```sh
dotnet build ModulithFoundry.slnx --no-restore -m:1
dotnet test --project src/Gloaming.Messaging/samples/MessagingWorkerDemo.Tests/MessagingWorkerDemo.Tests.csproj --no-build --no-restore --no-progress --no-ansi --output Normal --show-stdout Failed --show-stderr Failed
```

## Review surface and closure disposition

- Consumer runtime: [BrokerSession](https://github.com/oxface/modulith-foundry/blob/56a8b12857d1cffb842d839238784c607b66e474/src/Gloaming.Messaging/samples/MessagingWorkerDemo/BrokerSession.cs),
  [host attachment](https://github.com/oxface/modulith-foundry/blob/56a8b12857d1cffb842d839238784c607b66e474/src/Gloaming.Messaging/samples/MessagingWorkerDemo/Program.cs) and publication-start logging
  in [WorkerPublisher](https://github.com/oxface/modulith-foundry/blob/56a8b12857d1cffb842d839238784c607b66e474/src/Gloaming.Messaging/samples/MessagingWorkerDemo/WorkerPublisher.cs).
- Process proof: [BrokerRecoveryTests](https://github.com/oxface/modulith-foundry/blob/56a8b12857d1cffb842d839238784c607b66e474/src/Gloaming.Messaging/samples/MessagingWorkerDemo.Tests/BrokerRecoveryTests.cs),
  dedicated native broker controls, minimal intake barrier/connection-string topology overload,
  timeout diagnostics and the existing collector case's stop budget.
- Family/sample docs: recovery/shutdown contract, capabilities, observability and outbox recovery;
  dated W1/OBS1 follow-up links preserve historical results. Root design/extraction/roadmap
  reconcile the old stalled-publication/exporter gap with these selected process observations.

The selected **W1/M2 broker-recovery** and **W1/OBS1 shutdown** audit obligations are addressed
by implementation and native process evidence, subject to final change-set review. The
[preparation assessment](w1-broker-recovery-shutdown-assessment.md) retains the separately
maintained orchestration audit's provenance; its worktree/index was not modified.

No new reusable mechanism was proven. This is sample/consumer composition over existing
library protocols, with no public library-interface, schema, grant, package version, template
or archive change. Rejected extraction candidates are a generic transport manager, reconnect
state machine, scaling framework and arbitrary publisher/exporter deadline wrapper. Backoff,
restart admission, external process grace/termination and business idempotency remain consumer
policy. Network blackholes and cluster failover are not newly certified by a native application
restart/resource-alarm proof.

Inbox retention merged and the owner approved its five explicit post-v1 supplied-feature
deferrals. This continuation does not reopen dead-letter purge, permanent dedup tombstones,
automatic maintenance scheduling/cumulative accounting or operator dashboards. No further
important M2 protocol gap was identified in this reviewed scope. WF2 definition deployment
and T2/S1/readiness remain separate audit obligations; this report does not declare whole-v1
or unrelated capability closure. No checkpoint/push is authorized by implementation approval.
