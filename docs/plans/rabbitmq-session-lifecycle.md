# RabbitMQ session lifecycle: interface review

Status: **owner-approved public interface/scope; implemented for change-set review**,
2026-10-22. The owner approved this proposal after the consumer-only proof and adopter
comparison. The [extraction report](../reports/rabbitmq-session-lifecycle.md) records current
implementation, adoption and new verification on `fix/worker-broker-recovery`, based on 2e1ffac.
The index remains preserved; no checkpoint follows from implementation approval.

The [comparison assessment](../reports/rabbitmq-session-lifecycle-assessment.md) separates
actual common mechanics from different consumer recovery policies. This is a continuation
inside W1/M2 and W1/OBS1, not a new queue framework or release-readiness block.

## Recommendation and package scope

Add optional **Gloaming.Messaging.RabbitMQ** under the Messaging family. It owns one native
connection and its explicitly created channels, captures terminal shutdown and tears resources
down safely. Callers keep native IChannel for publishing, queue operations and acknowledgements.
It does not implement IMessagePublisher, interpret envelopes or know a DbContext.

Direct dependencies: pinned RabbitMQ.Client 7.2.2 and Microsoft.Extensions.Logging.Abstractions
10.0.12, both already centrally declared. No dependency on the Gloaming core/EF/workflow
packages, Generic Host, DI, ASP.NET, OTel SDK, sample contracts or ServiceDefaults. Existing
Messaging packages gain no reference to this opt-in integration.

An explicit callback/token seam avoids a second hosting package: native Generic Host callers
need only select their exit status and call StopApplication. Finite consumers use resource
ownership/cleanup without installing a host or an observer.

## Approved public surface

```csharp
namespace Gloaming.Messaging.RabbitMQ;

public sealed record RabbitMqSessionOptions(
    TimeSpan ProtocolTimeout,
    TimeSpan ConnectionCloseTimeout);

public sealed class RabbitMqSession : IAsyncDisposable
{
    public static Task<RabbitMqSession> OpenAsync(
        Action<ConnectionFactory> configure,
        RabbitMqSessionOptions options,
        CancellationToken cancellationToken = default);

    public Task<IChannel> CreateChannelAsync(
        CreateChannelOptions? options = null,
        CancellationToken cancellationToken = default);

    public IDisposable ObserveTerminalShutdown(
        CancellationToken intentionalStop,
        Action<ShutdownEventArgs> onUnexpectedShutdown,
        ILogger? logger = null);

    public ValueTask DisposeAsync();
}
```

ConnectionFactory, CreateChannelOptions and IChannel are native RabbitMQ.Client types;
ShutdownEventArgs is RabbitMQ.Client.Events.ShutdownEventArgs. ILogger is native
Microsoft.Extensions.Logging. This class has no public constructor, connection property,
virtual methods or matching IRabbitMqSession interface. No new shutdown DTO, failure-policy
interface, transport enum, dispatch option or operator recovery method is proposed.

### Creation, options and ownership

- Validate configure/options and native-supported positive finite durations before connecting.
  No implicit five-second policy or infinite-timeout default. The pinned client's native
  minimum abort window still applies; a smaller argument is not a smaller process deadline.
- OpenAsync creates a fresh native ConnectionFactory, invokes configure once, then sets
  its ContinuationTimeout from ProtocolTimeout before opening the connection. That one
  property is explicitly controlled by session options. Configure retains all other native
  endpoint/TLS/credentials/client-name, handshake and recovery settings. No shared external
  factory is mutated and no URI/credential is stored in a new DTO or diagnostic.
- AutomaticRecoveryEnabled is **not** changed by the library. It remains native configuration.
  OpenAsync is resource ownership in either native recovery mode, not a reconnect guarantee.
- CreateChannelAsync delegates native creation options, tracks each successfully created
  channel before returning it, and applies the selected protocol continuation timeout.
  Publisher confirms/tracking, channel count and one-channel-per-concurrent-role decisions
  remain explicit native options. All channels remain owned by this session; callers use
  them directly but do not independently dispose them or replace their lifetime settings
  while relying on the documented session cleanup behavior.
- Channel creation and disposal share an internal lifecycle guard. A create/dispose race
  cannot leave a successfully created channel outside cleanup. Creation after disposal starts
  fails with ObjectDisposedException. Concurrent/repeated DisposeAsync calls observe one
  cleanup operation. This guard does not serialize publication/intake or create a channel pool.
- If native startup/creation/setup fails, the consumer's await-using session still disposes
  already acquired resources. Queue declaration/checks remain outside OpenAsync; it never
  declares, recreates, purges or deletes a queue, nor applies a database migration.

### Optional terminal observer

- ObserveTerminalShutdown explicitly selects terminal observation for a **nonrecovering**
  native session. Reject it with InvalidOperationException when automatic recovery is enabled;
  a temporary recoverable disconnect must not be silently reclassified as terminal loss.
  Callers wanting fail-stop first explicitly disable native recovery in configure.
- Permit one observer registration per session lifetime. Observe both the connection and all
  owned channels, including channels created later. Preserve the first unexpected reason
  observed before registration, so closure during host/setup construction is not lost.
  Native late shutdown notification/CloseReason handling must cover that race as well.
- Invoke onUnexpectedShutdown at most once with the original native reason. Requested
  intentionalStop cancellation or session disposal suppresses subsequent intentional close
  notifications. A reason already classified as unexpected is not rewritten into success
  by a later intentional stop. Concurrent stop/failure is classified at the observation gate;
  no cross-thread wall-clock ordering guarantee is claimed.
- The synchronous callback must be short, nonblocking and nonthrowing: set consumer status
  and signal stop. It must not await teardown, PostgreSQL or process termination, or synchronously
  dispose the session. The library does not set Environment.ExitCode, terminate a process or
  promise recovery when consumer callbacks fail/ignore the signal.
- Disposing the returned registration detaches observation/diagnostics and prevents new
  callback admission; it does not revoke a callback already admitted. Session disposal also
  detaches it. A second registration or registration after session disposal is rejected.
- If a logger is supplied, this registration emits terminal-loss and blocked/unblocked
  diagnostics. Resource blocking is diagnostic, not terminal loss. No URI, credentials,
  payload, failure-counter write or publisher-rejection assertion is introduced. Finite and
  automatic-recovery callers need no registration; native client diagnostics remain available.

### Cleanup and messaging semantics

DisposeAsync first uses native connection AbortAsync(ConnectionCloseTimeout), then disposes
owned channels in reverse acquisition order and finally the connection. Finally protection
attempts every acquired resource even if an earlier cleanup step fails. Preserve cleanup
failures: rethrow a single failure, or throw the BCL AggregateException with original failures
in cleanup-attempt order when multiple steps fail. Repeated disposal observes that same outcome.
Native acquisition/setup errors retain their original types; no transport-rejection exception
is invented. Do not swallow an uncertain close as successful publication. ProtocolTimeout also
applies to channel-zero close during connection disposal, addressing the reproduced native
wait. It is distinct from a publisher-confirm attempt timer and native handshake/socket settings.

Stop intended operations cooperatively before disposal. The session never waits for or wraps
arbitrary IMessagePublisher code. Native close cancellation/timeout does not establish transport
rejection or roll back external effects. No reconnect loop, session replacement, channel renewal,
retry budget, lease renewal, hard process deadline or automatic forced termination is added.
Consumers retain external supervision and grace/termination policy.

No inbox/outbox storage behavior changes. Publication remains outside the claim transaction;
MessageId/envelope, token fencing, expiry and Attempts/failure-observation meanings stay intact.
This transport lifecycle must not become a parallel dead-letter/retry interface.

## Concrete adoption

Standalone worker (illustrative proposal, not existing API):

```csharp
await using var session = await RabbitMqSession.OpenAsync(
    factory =>
    {
        factory.Uri = new Uri(configuration["RabbitMQ:Uri"]!);
        factory.AutomaticRecoveryEnabled = false; // Consumer selects fail-stop.
        factory.ClientProvidedName = "messaging-worker-" + role;
    },
    new(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5)),
    startup.Token);
IChannel channel = await session.CreateChannelAsync(
    new CreateChannelOptions(
        publisherConfirmationsEnabled: true,
        publisherConfirmationTrackingEnabled: true),
    startup.Token);
await channel.QueueDeclarePassiveAsync(queue, startup.Token);

// After building the native host, before executing it:
using var observation = session.ObserveTerminalShutdown(
    lifetime.ApplicationStopping,
    _ =>
    {
        Environment.ExitCode = 1;
        lifetime.StopApplication();
    },
    logger);
await host.RunAsync();
```

BrokerSession may remain a small **sample topology holder** for queue + native channel and
setup/passive declaration. Its lifecycle mechanics/diagnostics move into the optional package;
its host callback remains consumer code. WorkerPublisher and RabbitMqRenderReceiver still
receive native IChannel. No wire/header/admission/business-policy extraction follows.

Adopt the same ownership/cleanup in at least one finite MessagingDemo journey and the
three-channel WorkflowBroker. The finite journey keeps explicit queue setup/deletion and
uses no observer. WorkflowBroker retains its current native automatic recovery and separate
confirm-enabled command/reply channels plus intake channel; it **does not register the terminal
observer**. Its module routing, retrying intake and business transactions stay unchanged.
Do not change WorkflowDemo to fail-stop merely to force reuse of that optional operation.

This proves two uses of resource ownership with different lifetimes/channel counts, while
the existing worker proves the opt-in terminal observer. It does not claim the policies match
where they differ. Broader migration of all finite examples is unnecessary for this slice.

## Proof seams and adoption gates

These are proposed seams for owner approval together with the interface:

| Seam | Required evidence / limit |
| --- | --- |
| New public session, real RabbitMQ | Actual native single/multiple channel use and confirms, partial setup failure, reverse cleanup, repeated/concurrent disposal and channel-create/dispose race. Observe native channel/queue/connection outcomes, not private fields or mocked client behavior. |
| Observer, real native close | One callback across connection/channel shutdown, late registration, a channel-only protocol failure while connection survives, intentionally cancelled stop, disposed registration and a subsequently created channel. Automatic recovery + terminal observation rejects clearly. |
| Native automatic-recovery compatibility | A three-channel session with recovery enabled survives a real broker application stop/start via the native client's recovery and can publish/receive again without this helper replacing sessions/channels. No broker-cluster/blackhole or business exactly-once claim. |
| Standalone actual processes + PostgreSQL/RabbitMQ | Replace the sample mechanics, then rerun the existing live outage, retained-message identity, ordinary timeout retry and alarm-held SIGTERM proofs unchanged in their durable oracles. No claiming previous consumer-only results as new library evidence. |
| Finite/multi-channel consumers | Execute the finite journey and WorkflowDemo native tests/selected process roles through the new package, preserving confirmations, commit-before-ack, original envelopes and independent business behavior. Do not infer whole-workflow live recovery solely from the session-level compatibility proof. |
| Existing collector and uncertainty protocols | Reuse the tightened unavailable-collector case and acceptance-followed-by-completion-failure/accounting supervision/stale-lease proofs. No new duplicate collector journey or arbitrary-deadline wrapper. |
| Package independence | No native broker/optional-package leak into core, EF, producer or module contracts. New package uses only its declared native dependencies and works without Generic Host, EF or a Gloaming publisher implementation. |

If acquisition/cleanup can be shared but the observer cannot earn a stable interface, narrow
the extracted surface before adding abstractions. A reusable integration is not proven merely
because the proposed class compiles or its signature resembles the sample.

## File and schema scope after approval

| Area | Planned files/change |
| --- | --- |
| Optional library | `src/Gloaming.Messaging/Gloaming.Messaging.RabbitMQ/`: csproj, RabbitMqSession.cs, RabbitMqSessionOptions.cs and README. No service-registration facade. |
| Public-seam proofs | `src/Gloaming.Messaging/tests/RabbitMqTests/`: test project, dedicated native broker controls and lifecycle/observer/recovery cases. |
| Worker adoption | BrokerSession.cs/Program.cs and project reference; keep current process proof storage oracles and diagnostics, adjusting only their owning category where necessary. |
| Other adoption | MessagingDemo journey/project and WorkflowDemo WorkflowBroker/project; topology and wire adapters remain consumer-owned. Relevant consumer tests may gain focused native lifecycle assertions. |
| Wiring/verification | ModulithFoundry.slnx, Messaging CI command for the new test project, ArchitectureTests assembly/declaration/reference checks. Existing CI path map already covers the Messaging root/consumers; change only if adoption reveals missing coverage. |
| Documentation | Family README/capabilities/worker recovery contract, consumer READMEs, this plan/extraction/roadmap/design, final slice report and the next ADR after owner review. Preserve existing reports as dated evidence. |

No migration, database schema/index/grant, payload/contract, retention, dead-letter DTO,
operator retry, dependency version, template preset, archive or workflow definition deployment
change. Existing consumer runtime work remains intact until interface approval.

After implementation, run the new real native suite, affected consumer process suites,
Messaging CI selection, architecture/build/style/analyzer/format/archive and local-link checks.
Leave changes unstaged and obtain exact complete change-set approval before any commit.
