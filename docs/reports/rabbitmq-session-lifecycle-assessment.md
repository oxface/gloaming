# RabbitMQ lifecycle extraction assessment

Date: 2026-10-22. Status: source comparison and
[public-interface proposal](../plans/rabbitmq-session-lifecycle.md), awaiting owner review.
No new runtime/library implementation or fresh infrastructure test result is claimed here.

Follow-up: the owner approved the interface/scope and the
[extraction report](rabbitmq-session-lifecycle.md) records implementation/adoption and fresh
native tests. This assessment retains its source-comparison preparation baseline.

## Current work and evidence

The default checkout remains on `fix/worker-broker-recovery`, based on fetched 2e1ffac.
The existing consumer-only implementation, tests and documentation were preserved; its
23 changed/new files and the index were snapshotted before preparing this proposal. No
branch switch, staging, commit, push, package creation or runtime modification occurred.

The [consumer-only closure report](w1-broker-recovery-shutdown.md) records actual live native
outage/shutdown executions and the 626-test relevant selection. Those prove the selected
consumer composition. They do not prove a not-yet-implemented public RabbitMQ integration.
The owner's follow-up correctly distinguishes **consumer policy ownership** from requiring
every consumer to reimplement error-prone mechanics.

## Actual adopter comparison

| Adopter | Current native mechanics | Consumer-specific semantics |
| --- | --- | --- |
| [Standalone BrokerSession](../../src/Gloaming.Messaging/samples/MessagingWorkerDemo/BrokerSession.cs) | One connection/confirm channel; explicit automatic recovery false; new terminal observation, five-second protocol continuation and connection-abort settings; exception-safe cleanup. | One role per process, queue declaration only for setup, passive check for workers, host stop/nonzero exit on unexpected loss, external supervised restart. |
| [MessagingJourney](../../src/Gloaming.Messaging/samples/MessagingDemo/MessagingJourney.cs) and [schema journey](../../src/Gloaming.Messaging/samples/MessagingDemo/SchemaCompatibilityJourney.cs) | Explicit automatic recovery false; one confirm-enabled channel; native nested await-using channel-before-connection disposal. | Finite operations, module migrations and explicitly owned private queue lifecycle; no host stop/restart loop. |
| [WorkflowBroker](../../samples/Wholesale/WorkflowDemo/WorkflowBroker.cs) | One connection, confirm-enabled commands/replies plus separate intake channel; recovery setting omitted; channel-first sequential cleanup. | Multiple concurrently hosted roles, consumer routes/headers/validation, setup/passive queue checks, native recovery currently enabled and retrying intake. |
| [Inventory journeys](../../samples/Wholesale/EventPersistenceDemo/OutboxJourney.cs) | Similar explicit nonrecovering connection/confirm channel ownership. | Finite historical adopter with its own module composition and topology; broad migration is not needed to prove this candidate. |

The exact pinned RabbitMQ.Client 7.2.2 local XML documents AutomaticRecoveryEnabled's
default as true. WorkflowBroker therefore differs from the worker; omission must not be
misreported as disabled recovery. Its [intake loop](../../samples/Wholesale/WorkflowDemo/WorkflowPollingWorkers.cs)
logs ordinary failures and retries. Recovery remains the native client's operation; current
source inspection is not a whole-workflow live-outage proof.

Shared mechanics are connection/channel acquisition ownership, acquired-resource cleanup,
protocol-continuation/connection-close settings and native shutdown observation. Topology,
channel concurrency, route/wire mapping, producer admission, business effects, fail-stop versus
native recovery and process authority are not interchangeable.

## Recommended seam and alternatives

Recommend one optional native-only session owner, with original IChannel returned for all
transport work. A small options record makes the two cleanup-related native time choices
explicit. The session creates its own native factory through caller configuration, avoiding
hidden mutation of a reused factory. No Gloaming/EF/Generic Host dependency is needed.

Terminal observation is an opt-in callback plus intentional-stop token, with optional native
logging. The host retains status/StopApplication in a few lines. Native recovery-enabled
sessions can reuse ownership/cleanup without observation; contradictory terminal observation
is rejected. This preserves WorkflowBroker's selected behavior instead of changing its policy
just to reuse a helper.

Considered and rejected for this scope:

- Moving BrokerSession unchanged: it includes queue/role/Generic Host/process-exit choices
  and one-channel assumptions which do not match the other adopters.
- Native extension methods for abort and event subscription alone: leave acquisition ownership,
  partial cleanup, late-created channels and disposal races repeated in callers.
- A generic host/transport manager: introduces reconnect/session replacement, routing or
  scheduling interfaces not required by these consumers.
- A fatal-publisher exception/classification hook in the generic outbox: cannot see idle
  transport loss or intake blocked in PostgreSQL, and conflates publication policy with native
  session lifetime. Existing accounting/completion exception semantics stay unchanged.
- A new hosting package or Environment.ExitCode policy inside the library: the callback/token
  seam already composes native host stop while preserving operator/deployment authority.

## Proof, gaps and disposition

The proposal specifies public signatures, invariants/error modes, executable consumer usage,
package/file scope and native/process proof seams. Pending proof includes multiple channels,
native automatic-recovery compatibility, observer registration/disposal/late close races,
partial setup cleanup and unchanged durable worker oracles after extraction.

No new reusable mechanism was proven during this assessment. A **credible extraction candidate
was identified**, correcting the earlier overly broad inference that consumer ownership rules
out useful shared lifecycle mechanics. The existing runtime proof remains valid; final
extraction closure requires owner interface approval, real adoption and new verification.

No database/schema/grant, dependency version, transport identity, outbox claim/accounting,
inbox receipt/retention, workflow deployment or template change is proposed. Classification,
topology, timeout values, recovery choice, external backoff/restart/termination and business
idempotency remain consumer policy. Family documentation and consumer setup will describe the
new mechanism only after implementation; current docs must not advertise the proposed API
as already available.

Preparation verification: whitespace and 340 local documentation links passed. Checksums
confirmed every previously changed runtime/test file and the review index were unchanged;
only the three existing root plan/report documents received continuation notes, alongside
the two new assessment/proposal documents. No fresh C# build or infrastructure suite ran
for this documentation-only review preparation.
