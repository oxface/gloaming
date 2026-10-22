# RabbitMQ lifecycle extraction and adoption

Date: 2026-10-22. Branch `fix/worker-broker-recovery`, based on inbox-retention merge 2e1ffac.
The owner approved the [concrete public interface/adoption scope](../plans/rabbitmq-session-lifecycle.md)
before implementation. All work remains unstaged and uncommitted for exact change-set review.

## Outcome: one reusable lifecycle mechanism

Optional [Gloaming.Messaging.RabbitMQ](../../src/Gloaming.Messaging/Gloaming.Messaging.RabbitMQ/README.md)
now supplies native connection/channel ownership, explicit phase-timeout setup, safe acquired-
resource cleanup and optional one-time terminal shutdown observation. It exposes two public
types and returns native IChannel; it has no bus/publisher, host lifetime, reconnect loop,
dead-letter/recovery store or business contract interface.

The package depends only on the already pinned RabbitMQ.Client 7.2.2 and logging abstractions
10.0.12. Core/EF/workflow packages and module contracts do not depend on it. Its independent
native test project has no EF, Generic Host or Messaging-core reference. [ADR 0021](../adr/0021-optional-rabbitmq-session-lifecycle.md)
records the owner-reviewed package/authority decision.

Consumer ownership now means choosing topology, native recovery, phase/attempt time values,
short host stop/status callback and external supervision. Consumers share the delicate mechanics
rather than copying event races, multiple-resource cleanup and disposal admission guards.

## Actual adoption and policy differences

| Adopter | Shared mechanics / retained consumer behavior |
| --- | --- |
| [Worker BrokerSession](../../src/Gloaming.Messaging/samples/MessagingWorkerDemo/BrokerSession.cs) | Delegates connection/channel ownership, diagnostics and observation to the optional package. Retains queue setup/passive check, automatic recovery false and the callback setting status 1/StopApplication. WorkerPublisher/receiver continue using native IChannel and unchanged wire protocols. |
| [Finite MessagingJourney](../../src/Gloaming.Messaging/samples/MessagingDemo/MessagingJourney.cs) | Uses shared ownership/cleanup with automatic recovery false and no observer/host dependency. Keeps explicit migration, private queue creation/deletion, confirmations, admission and independent commit/ack/processing. |
| [WorkflowBroker](../../samples/Wholesale/Worker/Broker/WorkflowBroker.cs) | Uses shared ownership for command/reply confirm channels and an intake channel. Keeps native default automatic recovery enabled and installs no terminal observer. Routing, publication attempt timer, retrying intake and business workflows remain consumer code. |

All three choose five-second native protocol/connection-close values. The protocol value
maps to ContinuationTimeout after fresh factory configuration, including the internal
channel-zero close wait reproduced by the earlier consumer-only proof. It also affects native
startup/queue RPCs; handshake/socket settings and publisher-confirm attempt timers are separate.
The integration never changes AutomaticRecoveryEnabled or silently installs process policy.

No existing public inbox/outbox interface, stored identity, schema, grant, lease, failure
accounting or operator authority changes. No dependency version, template, archive or workflow
definition deployment change. The alternate finite schema journey/Inventory journeys were
not broadly migrated merely to maximize reuse.

## Fresh public-interface proofs

The final **16 RabbitMqTests cases pass**, including fixture lifecycle, in **14.3 seconds**.
They exercise the public session and original native channels against RabbitMQ 4.3.6. The
native outage case owns a separate broker; other proofs are not interrupted by its controls.

| Contract | Fresh evidence |
| --- | --- |
| Actual native transport and cleanup | Native confirm-enabled publication and manual unacknowledged intake across owned channels. Disposal closes them; a fresh session receives the same retained body/identity with Redelivered true, then acknowledges/deletes the consumer-owned queue. |
| Partial consumer setup | A real missing-queue passive check closes a later channel with native 404. Await-using cleanup closes the earlier acquired channel while preserving the original native setup exception. |
| First reason / late registration | A channel-only native 404 occurs before observer registration while another channel remains usable. Registration receives the original ShutdownEventArgs instance. A second channel failure and session close do not duplicate notification. |
| Later channels / intentional stop | Registration before channel creation observes a subsequently created channel's failure. Cancellation requested by the callback does not revoke the already admitted reason. Pre-cancelled lifetime or disposed registration suppresses new callbacks. Active-observer session disposal is intentional, and registration cannot be rearmed. |
| Acquisition versus disposal | A native shutdown callback barrier holds actual cleanup in flight. New acquisition fails with ObjectDisposedException rather than entering a closed native connection. Sixteen competing acquisitions and eight disposal callers settle with acquired channels closed or acquisition rejected. Repeated disposal observes one outcome. |
| Native automatic recovery | With default recovery enabled, terminal observation rejects the contradictory policy. Three original native channels observe live broker `stop_app`; after `start_app`, native recovery restores them and their selected continuation values. Both original publisher channels confirm fresh messages; original intake receives the pre-outage persistent message plus both fresh identities/bodies. Shared disposal closes all three. Gloaming creates no replacement session/channel. |
| Invalid phase options | Six zero/negative/overflow cases fail with the appropriate option name before the native configure callback or connection is attempted. The native channel proof also checks session options override a conflicting configure-callback continuation value. |

The initial cleanup proof failed with channels still usable; the observer proof failed without
registration support; the in-flight disposal proof failed with native AlreadyClosedException
instead of rejecting new acquisition at the session seam; invalid options initially reached
configuration. Each loop was made green at the same public seam before broad verification.
An initial temporary queue default was corrected to an explicitly durable/nonexclusive queue
for the redelivery oracle; it was a test setup issue, not an extracted library guarantee.

Cleanup attempts native abort, reverse channel disposal and connection disposal despite errors.
Single-error stack preservation and BCL aggregation of multiple cleanup errors are explicit,
reviewable implementation/error semantics. Simultaneous native cleanup failures were not
artificially injected; tests prove actual close/redelivery, partial setup and disposal races.
No fake transport or new test-only port was introduced to advertise stronger native guarantees.

## Fresh consumer and repository verification

These results are executions **after adoption**, separate from the earlier consumer-only
[626-test closure report](w1-broker-recovery-shutdown.md) and the
[source-comparison assessment](rabbitmq-session-lifecycle-assessment.md). The final relevant
selection is **645 passed, zero failed/skipped**: 546 Messaging cases and 99 architecture cases.
This is not every unrelated repository test.

| Final suite/check | Result |
| --- | --- |
| New RabbitMqTests | 16 passed. |
| MessagingTests / WorkflowTests | 8 / 8 passed. |
| WorkflowPostgresTests | 38 passed. |
| OutboxPostgresTests / InboxPostgresTests | 102 / 93 passed. Existing leases, restricted authority, accounting/completion uncertainty and receipt protocols remain intact. |
| OutboxDemo.Tests / InboxDemo.Tests | 25 / 43 passed. Includes the actual adopted finite journey across independent module databases with no receiving outbox. |
| MessagingWorkerDemo.Tests | 28 passed in 81.9 seconds. Original native live outage, broker/PG-retained progress, confirmation timeout, alarm-held SIGTERM, actual lease expiry and accounting/completion supervision oracles rerun through the extracted session. |
| WorkflowDemo.Tests / LocalWorkflowDemo.Tests | 152 / 15 passed. Actual multi-channel workflow adoption preserves request/reply, compensation, restricted-role and selected process behavior. |
| EventPersistenceDemo.Tests, CI `*DispatchTests` selection | 18 passed. Broader unrelated event-persistence tests were not rerun. |
| ArchitectureTests | 99 passed, including three new declaration/assembly checks for optional-package isolation and independent native adoption. |
| Full active solution / final new-test build | Passed, zero warnings/errors. SDK 10.0.401. |
| Style / analyzers | Active-solution verify-no-changes passed. The final strengthened test source also passed its project style verification. |
| CSharpier | Passed, 929 files including generated code. |
| Frozen archive | Original 800-file snapshot verified unchanged. |
| Documentation / Git | 635 local links and whitespace passed; all 48 changed/new files unstaged, original index preserved, no commit/push. Pre-existing work outside approved adoption/documentation edits verified unchanged by checksum. |

The Messaging CI command list now includes the new suite. The broad CI run first included
15 public-session cases; the final public-session rerun includes explicit partial-setup and
strengthened active-observer assertions, totaling 16. Overlapping executions are not counted
as additional cases. Local native/IPC checks used the configured rootless Podman socket and
execution outside the filesystem sandbox where required; the initial sandbox-only restore/
test-runner pipe failures are not successful verification results.

Reproduce with a configured Docker/Podman runtime on Linux:

```sh
dotnet build ModulithFoundry.slnx -m:1
dotnet test --project src/Gloaming.Messaging/tests/RabbitMqTests/RabbitMqTests.csproj --no-build --no-restore
dotnet test --project src/Gloaming.Messaging/samples/MessagingWorkerDemo.Tests/MessagingWorkerDemo.Tests.csproj --no-build --no-restore
dotnet test --project src/Gloaming.Messaging/samples/InboxDemo.Tests/InboxDemo.Tests.csproj --no-build --no-restore
dotnet test --project samples/Wholesale/IntegrationTests/Wholesale.IntegrationTests.csproj --no-build --no-restore
```

## Closure, limits and review surface

A new reusable mechanism **was proven in this continuation**: native RabbitMQ lifecycle
ownership/observation/cleanup with actual consumers of different lifetimes/channel counts.
The earlier statement that no mechanism was proven describes the consumer-only stage; it
does not apply to this completed extraction. Rejected additions remain a generic transport
manager, new publisher/receiver facade, hosted runtime, reconnect state machine, channel pool
and arbitrary publisher deadline wrapper.

Real process evidence still preserves publication outside the transaction, original identity,
lease fencing and unknown acceptance. SIGTERM under a native alarm and the reused unreachable-
collector scenario meet their separate fifteen-second verification budgets; this is not a
universal process/publisher/exporter wall-clock guarantee. External supervision owns repair,
restart/backoff, grace and forced death. Faulty/blocking callbacks can defeat consumer stop
policy; the library callback contract is explicitly short/nonthrowing/nonblocking.

Native automatic-recovery compatibility is a public-session broker-application restart proof,
not whole-workflow broker-failover certification or recovery from every permanent channel/
topology error. Core classification and durable accounting remain consumer/library protocols
already proven separately. No important new M2 storage gap was found; approved post-v1
retention/administration deferrals remain intact. WF2 definition deployment and T2/S1/readiness
remain separate audit work, not new obligations extracted into this package.

Review [RabbitMqSession](../../src/Gloaming.Messaging/Gloaming.Messaging.RabbitMQ/RabbitMqSession.cs),
[options](../../src/Gloaming.Messaging/Gloaming.Messaging.RabbitMQ/RabbitMqSessionOptions.cs),
the [public proof project](../../src/Gloaming.Messaging/tests/RabbitMqTests/RabbitMqTests.csproj),
three consumer integrations, architecture/dependency declarations, family/consumer docs and
ADR 0021 together. No changes are staged and implementation approval authorizes no checkpoint.
