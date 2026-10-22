# Consumer worker recovery and shutdown

Worker registration and polling options are supplied by the explicitly selected
[hosting package](../Gloaming.Messaging.EntityFrameworkCore.Hosting/README.md), using the
`Gloaming.Messaging.EntityFrameworkCore.Hosting` namespace. Callable persistence/delivery
operations remain independently usable without this package.

Gloaming owns retained work, row locks, committed claims and fenced lifecycle writes. The
consumer owns its publisher/receiver, recovery choice, cancellation and process supervisor.
Retrying an operation in a fresh database scope cannot repair a permanently closed channel.

The [standalone worker](../samples/Delivery/README.md) selects fail-stop
using RabbitMQ.Client 7.2.2 and native Generic Host APIs. Optional
[Gloaming.Messaging.RabbitMQ](../Gloaming.Messaging.RabbitMQ/README.md) now supplies shared native
session/channel ownership, phase-timeout setup, terminal observation and cleanup. Queue/role,
host stop/status and recovery policy stay editable consumer composition. Core/EF remain
transport-independent; no transport manager or new publisher/recovery interface is introduced.

## Terminal sessions and restart

Dispatch and receive establish one connection/channel with automatic recovery disabled.
Setup alone declares the durable queue; long-lived roles passively require it. Initial
connection/declaration failure fails startup without claiming work. Unexpected connection or
channel destruction logs `Broker session lost`, sets exit code 1 and requests host stop,
including idle dispatch and intake awaiting PostgreSQL. Native late shutdown subscriptions
cover destruction during host construction. Duplicate notifications make one stop decision;
callbacks signal without awaiting teardown or database work. The package retains the native
reason and signals once; the consumer callback sets exit status and calls StopApplication.

The finite MessagingJourney adopts the same ownership/cleanup without a host observer.
WorkflowBroker owns three channels through it while retaining native automatic recovery.
ObserveTerminalShutdown rejects recovery-enabled sessions, so adopting cleanup does not
silently turn recoverable native disconnects into fail-stop. Native automatic recovery and
permanent channel/topology error limits remain RabbitMQ.Client/consumer obligations.

Intentional ApplicationStopping cancellation suppresses teardown's shutdown notifications.
A failure observed before intentional stop retains its nonzero exit decision. A resource
alarm is different: blocked/unblocked notifications are diagnostics, not terminal session
failure. Ordinary publisher failures against an open session retain existing fenced accounting
and worker retry. An open session is not a health or acceptance guarantee.

Supervise each role and restart it after repair/broker recovery. Configure backoff and restart
admission outside the library. Restart unexpected exits even when status is 0: native
BackgroundService StopHost begins shutdown but does not universally set failure exit status.
The broker-terminal path specifically sets 1. Accounting/completion/handler supervision retains
its separately documented native host behavior. Ignore does not restart a faulted worker.

After recovery, launch the same executable/role/settings to obtain a fresh session:

```sh
dotnet run --project src/Gloaming.Messaging/samples/Delivery/Worker/Messaging.Worker.csproj -- --role dispatch
dotnet run --project src/Gloaming.Messaging/samples/Delivery/Worker/Messaging.Worker.csproj -- --role receive
dotnet run --project src/Gloaming.Messaging/samples/Delivery/Worker/Messaging.Worker.csproj -- --role process
```

See the sample for connection settings. Do not hide setup/migration inside recovery, rewrite
messages or clear claims to accelerate restart. There is no in-process reconnect/channel
handoff, automatic recovery worker or deployment supervisor.

## Cancellation and uncertain outcomes

The host stopping token reaches native dispatch/intake. The sample publisher links it to its
existing five-second publication timer and awaits RabbitMQ confirms. Requested publication
cancellation preserves the committed claim for expiry. An ordinary timer/transport exception
can instead commit a fenced failure observation. Neither establishes rejection. Attempts
counts committed claims; failure counts describe committed observations, not every disconnect,
crash, cancellation or delivery.

Publication stays outside the transaction. Replacement respects database-clock expiry and
claims a fresh token, preserving MessageId and the original envelope. An obsolete publisher
can still accept externally, but cannot complete/account against a successor's claim. Missing
confirms or acceptance followed by completion failure can repeat delivery. Intake commits
before ack; channel closure makes unacknowledged work eligible for redelivery. Retained
receipts suppress duplicates; [receipt deletion](inbox-retention.md) ends that evidence and
allows old delivery to execute again. Consumer semantic idempotency remains necessary.

## Native cleanup and external supervision

Process-owned disposal first calls native connection AbortAsync with a five-second close
window, then disposes channel/connection resources in exception-safe cleanup. The pinned
client documents forced socket closure on close timeout. That window alone does not bound
later connection disposal: the internal channel-zero close can await a separate protocol RPC.
The sample selects RabbitMqSessionOptions with ProtocolTimeout and ConnectionCloseTimeout
both five seconds. The integration applies the protocol value to native ContinuationTimeout. This applies
to AMQP protocol RPCs such as channel/queue declaration and close, including startup/setup;
it does not replace publisher-confirm cancellation. Cleanup never deletes the durable queue
or reports publication/acknowledgement success.

These native settings and the five-second attempt timer are separate consumer policies.
They impose no universal IMessagePublisher deadline. Host cancellation cannot abort arbitrary
user code or roll back external effects. Noncooperative publishers/handlers/exporters can
outlive an in-process timeout. The deployment supervisor owns its external grace period and
eventual force termination; forced death can lose diagnostics and leave acceptance unknown.

## Evidence boundary

The [closure report](../../../docs/reports/w1-broker-recovery-shutdown.md) records real
PostgreSQL/RabbitMQ process evidence for live broker application outage, durable queue
preservation, producer commits during outage and fresh-session progress. Native resource
blocking establishes an actual stalled publication before SIGTERM, with a separate fifteen-
second process-stop verification budget. This is evidence for the pinned native composition,
not every network blackhole, cluster failover or arbitrary cancellation implementation.
The subsequent [extraction report](../../../docs/reports/rabbitmq-session-lifecycle.md) records
fresh public-session proofs and reruns of actual consumers through the optional package;
earlier consumer-only results are not substituted for those new library/adoption executions.

The existing unavailable-collector scenario is reused with its one-second OTLP timeout and
a separate fifteen-second budget for stopping the three worker roles. Its business-progress
oracle remains. This does not prove bounded flushing for arbitrary exporters or diagnostic
delivery after process death. Existing completion-after-acceptance, native commit-before-ack,
lease fencing and accounting-supervision tests retain their distinct obligations.
