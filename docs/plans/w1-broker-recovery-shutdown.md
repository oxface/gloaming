# W1/M2 broker recovery and W1/OBS1 shutdown closure

Status: **owner-approved consumer scope, implemented for change-set review**, 2026-10-22.
Branch fix/worker-broker-recovery starts from fetched origin/main 2e1ffac after the
inbox-retention merge. The owner approved implementation after concrete scope review.
No library-interface change was required. The [closure report](../reports/w1-broker-recovery-shutdown.md)
records the consumer-only behavior, refinements and verification; this plan retains that
reviewed scope. The subsequently owner-approved [RabbitMQ lifecycle interface](rabbitmq-session-lifecycle.md)
extracts shared mechanics into an optional package, with its own
[fresh implementation/adoption report](../reports/rabbitmq-session-lifecycle.md).

## Owning block and current findings

This closes selected operating obligations inside W1/M2 and W1/OBS1. It does not create a
new transport capability family or reopen the approved post-v1 retention/administration
deferrals. The [current-code assessment](../reports/w1-broker-recovery-shutdown-assessment.md)
reconciles the orchestration closure audit with actual merged code and prior evidence.

BrokerSession disables RabbitMQ automatic recovery and opens one process-owned connection
and confirm-enabled channel. WorkerPublisher reuses that channel. Generic OutboxWorker
retries ordinary publication exceptions in fresh database scopes; recreating those scopes
does not replace the broker session. RabbitMqIntakeWorker already stops its host and sets
exit code 1 on intake failure. A transport disconnect while intake is blocked in PostgreSQL
or dispatch is idle is not equivalent to that operation's exception path.

The existing RabbitMqExportPublisher links cancellation to a consumer-selected five-second
attempt timer and awaits native publisher confirmations. That timer is not a guarantee for
arbitrary IMessagePublisher implementations, a maximum external effect duration or proof
that a timed-out publication was rejected.

## Selected policy: fail-stop and supervised restart

Keep automatic recovery disabled and one session per process. Extend the existing internal
BrokerSession and Program wiring; add no Gloaming interface, transport manager or new worker.

1. Before host execution, attach native connection/channel shutdown observation to the
   current session using the built host's IHostApplicationLifetime and native logger. Late
   subscription must also notice an already-closed session; the pinned client documents
   immediate delivery for handlers attached after destruction. Include a current state check
   where useful; it is not a health or delivery-success assertion.
2. On the first **unexpected** terminal session shutdown, record a clear consumer diagnostic,
   set Environment.ExitCode to 1 and request StopApplication. Observe both dispatch and receive
   roles, including idle dispatch and intake waiting on local storage. Do not await disposal,
   database recovery or host termination inside the native shutdown callback.
3. ApplicationStopping marks intentional cancellation before normal broker disposal. SIGTERM,
   Ctrl+C and expected shutdown must not be reclassified as broker failure merely because
   channel/connection disposal subsequently emits shutdown events. Duplicate terminal events
   must not repeat the stop decision. An earlier unexpected transport failure keeps its failure
   exit status; a later cancellation does not turn it into success.
4. A resource alarm/ConnectionBlocked notification is diagnostic, not terminal session loss.
   Ordinary publication failures while the session remains open retain existing retry and
   durable-accounting behavior. An open session is not proof of broker health or acceptance.
   Add a publication-start diagnostic and native blocked/unblocked diagnostics if needed to
   observe the actual stalled transport phase; no production fault switch or deadline option.
5. Initial connection/passive queue-check failure remains startup failure. No in-process
   reconnect, queue recreation, migration, identity regeneration or hidden publication replay.
   After repair/broker availability, an external supervisor/operator launches the same role
   again, creating a fresh session. Supervisor backoff, desired replica count and restart
   admission belong to the consumer.

Existing executable usage stays:

```sh
dotnet run --project samples/MessagingWorkerDemo/MessagingWorkerDemo.csproj -- --role dispatch
dotnet run --project samples/MessagingWorkerDemo/MessagingWorkerDemo.csproj -- --role receive
dotnet run --project samples/MessagingWorkerDemo/MessagingWorkerDemo.csproj -- --role process
```

Deploy these as separately supervised processes with the already documented role settings.
The supervisor must restart unexpected exits, including the existing native StopHost path
whose exit status is not universally nonzero. Only this selected broker-failure policy adds
an explicit nonzero exit decision. Do not silently assume that native BackgroundService
failure or StopHost installs process restart or always reports a failure exit code.

## Cancellation and broker cleanup

Keep the native Generic Host stopping token flowing through dispatch/intake and the native
publisher/receiver. Retain the existing sample's five-second publication timer, with no new
library option or universal publisher deadline. A stalled real native publication must be
observed and cancelled during SIGTERM in the process proof.

BrokerSession currently disposes the channel before the connection without a selected finite
broker-close policy. Proposed cleanup first invokes the pinned client's native connection
AbortAsync with a **five-second close window**, then disposes channel/connection resources
with exception-safe cleanup. The client's documented timeout path forces socket closure;
abort is teardown, not a successful publication/acknowledgement result. Do not await a potentially
stalled channel-close handshake before the connection teardown. Apply this process-owned
cleanup to the finite setup role as well; it does not delete its durable queue.

Implementation refinement: the native resource-alarm proof reproduced a subsequent wait in
connection disposal after abort and channel disposal returned. The pinned client disposes
its internal channel zero through a separate protocol continuation. The sample therefore
also selects native ConnectionFactory.ContinuationTimeout of five seconds, affecting native
protocol RPCs (including startup/queue declaration and close), not publisher-confirm acceptance.
This closes the selected native cleanup wait without wrapping arbitrary publishers in a deadline.

Native host shutdown configuration, publisher cancellation and broker socket teardown are
different obligations. The process proof uses an explicit **fifteen-second verification
budget** for the selected stalled-publish stop, not a library-wide wall-clock SLA. A publisher,
handler, callback or exporter that ignores cancellation can outlive an in-process stop timeout.
The deployment supervisor still owns its external grace/termination policy. Forced process
death can lose diagnostics and leave unknown transport acceptance.

No arbitrary publisher-abort guarantee, external effect rollback, shutdown scheduler or
replacement host lifetime abstraction is selected.

## Preserve the durable protocols

- Publication remains outside the PostgreSQL transaction. Claim token/lease commit precedes
  network work. New session/process creation does not renew, clear or rewrite a claim.
- On publication cancellation, the existing dispatcher leaves a committed claim for expiry.
  A transport exception can instead record a fenced failure observation before shutdown if
  that transaction commits. Tests must not require a failure count for every disconnect.
- A missing confirmation, timeout or disconnect does not establish rejection. Completion
  can also be unconfirmed after acceptance. Retrying may publish the same delivery again.
- Preserve MessageId, schema/payload and business metadata. Attempts counts committed claims;
  failure counters count committed observations. No global execution cap is introduced.
- Intake still commits before acknowledgement. Closing a failed channel makes unacknowledged
  work eligible for native redelivery; retained receipts suppress duplicates while retained.
  The merged retention contract still permits execution after receipt deletion.
- Preserve existing lease fencing, savepoint accounting, completion supervision and operator
  authority. No schema, grant, failure-policy or retention-policy change.

## Required fresh process and infrastructure evidence

Use real PostgreSQL and the pinned RabbitMQ 4.3.6 broker. Broker controls operate only on
test-owned infrastructure. Keep outage/alarm tests isolated so they do not disrupt other proofs.
Use native database barriers, broker notifications and storage/queue observations to establish
phases; a sleep or starting two processes is not evidence of an in-flight operation.

| Proof | Concrete observation |
| --- | --- |
| Live broker disconnect and fresh recovery | Start actual dispatch and receive processes against a healthy broker. Establish idle dispatch after confirmed persistent publication and receive blocked in an uncommitted PostgreSQL intake before ack. Stop the broker application in its running test container; observe nonzero termination of both roles. No old process silently retries a permanently closed session. |
| Broker-down startup and retained progress | Keep producer commits independent while the broker application is down. A role started against the unavailable broker fails startup without consuming retained work. Restart the same broker application, preserving its durable queue, then launch replacement roles. Previously broker-retained/unacknowledged delivery and newly PostgreSQL-retained outgoing work reach local completion under their original identities. No business duplicate is produced while receipts remain. |
| Stalled publication and SIGTERM | Raise a native broker resource alarm to block publication while keeping the connection alive. Observe publication start/native blocked state and committed pending claim. Signal the real dispatch process while the alarm persists. Verify selected native cancellation/cleanup exits successfully within the proof budget, without interpreting cancellation as transport rejection. |
| Ordinary timeout against an open session | Keep the native alarm raised until the existing confirmation timer produces a committed failure observation. Remove the alarm and prove the same process/session confirms a subsequent attempt and retained intake deduplicates any buffered/repeated acceptance. Resource blocking alone must not trigger terminal-session fail-stop. |
| Recovery after stalled stop | Restore the broker in a finally path; inspect fresh sender state and await actual lease expiry where a claim remains. Replacement dispatch/intake/processing progresses the original identity. Accommodate ambiguous prior acceptance; assert deduplicated retained processing, unchanged envelope and no obsolete completion/accounting write, not a fabricated exact broker-delivery count. |
| Intentional versus terminal shutdown | Existing normal worker stop tests and the new stalled-stop case remain exit 0. Unexpected session loss is exit 1. Closing resources during intentional shutdown must not produce the terminal-failure diagnostic/status. |
| Unavailable collector reconciliation | Reuse UnreachableCollectorDoesNotPreventIndependentBusinessCommitAndProcessing. Preserve its actual independent producer/dispatch/intake/processing assertions and one-second OTLP timeout. Add an explicit bounded shutdown budget to that same scenario; do not duplicate its business journey or claim evidence for arbitrary exporters. |
| Existing uncertainty/fencing evidence | Rerun current completion-after-acceptance, killed-dispatch/real-lease-expiry, failure-accounting supervision, native receiver commit-before-ack and telemetry-context recovery tests. Their distinct oracles remain; do not replace them with a weaker broker-restart scenario. |

The broker application restart is a live outage of already-running clients, not merely a
replacement worker started against a continuously healthy broker. Selected native alarm and
stop behavior is evidence for this pinned consumer composition; it does not certify every
network blackhole, cluster failover, exporter or consumer publisher.

## File and review scope

| Area | Intended change |
| --- | --- |
| samples/MessagingWorkerDemo/BrokerSession.cs | Internal native terminal-session observation, intentional-stop suppression, finite native connection teardown and diagnostic hooks. |
| samples/MessagingWorkerDemo/Program.cs | Attach policy to built host before execution; keep existing role/DI/channel ownership and native host supervision explicit. |
| samples/MessagingWorkerDemo/WorkerPublisher.cs | Publication-start diagnostic if required for observable stall phase; native publish/identity behavior stays. |
| RabbitMqIntakeWorker.cs | Preserve commit-before-ack and current failure behavior; edit only if integration reveals a stop/cancellation race within this policy. |
| MessagingWorkerDemo.Tests | New broker-recovery/shutdown process cases and test-owned broker control fixture; minimal native intake-barrier/child-process helpers where required. No production fault injection or generic transport harness. |
| ObservabilityTests.cs | Tighten the existing unavailable-collector shutdown observation; retain the original business/OTLP-independence oracle. |
| Family/sample docs | Worker README, Messaging family capabilities/observability/recovery contracts, W1/OBS1 historical report follow-up links, current design/extraction/roadmap and final closure report. |

No changed library interface is proposed. If process evidence exposes a needed library
change, stop that dependent implementation and present the concrete interface/semantic change
for owner approval. Consumer implementation choices may be refined within this reviewed scope.
No migration, role/grant, dependency version, template, sample relocation or archive change.

## Verification and closure criteria

After scope review, run the actual worker process suite and relevant inbox/outbox/broker and
OBS1 checks, followed by the Messaging CI matrix and architecture/build/style/analyzer/format
checks affected by the final changes. Repeat only when a changed path or failure warrants it.
Report new tests separately from historical W1/OBS1/M2 evidence and identify exact native
control/stop phases. Confirm the review index is preserved and leave all changes unstaged.

The final closure report must reconcile these two audit rows against the tested final host:
terminal broker sessions have a visible recovery path, retained work progresses after live
broker recovery, and the selected native stalled-publication shutdown is exercised. Record
what the collector test already proves and its tightened stop observation, without promoting
those results into arbitrary publisher/exporter deadlines. Explicitly state whether any new
reusable mechanism was proven; the proposed outcome is consumer composition using existing
library protocols. WF2 definition deployment and T2/S1/readiness obligations remain with their
own selected work. No whole-block closure claim precedes implementation, proof and owner review.
