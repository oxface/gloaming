# Optional RabbitMQ session lifecycle

Own a native RabbitMQ connection and its channels, with safe acquired-resource cleanup and
optional terminal-shutdown observation. Depend only on RabbitMQ.Client and native logging
abstractions. No Messaging core, EF, Generic Host, DI, OTel SDK or business-contract dependency.

Reference Gloaming.Messaging.RabbitMQ and continue publishing/receiving through native IChannel:

```csharp
await using var session = await RabbitMqSession.OpenAsync(
    factory =>
    {
        factory.Uri = brokerUri;
        factory.AutomaticRecoveryEnabled = false; // Choose fail-stop explicitly.
        factory.ClientProvidedName = "exports-dispatch";
        // Configure native TLS/credentials/handshake settings here when needed.
    },
    new RabbitMqSessionOptions(
        ProtocolTimeout: TimeSpan.FromSeconds(5),
        ConnectionCloseTimeout: TimeSpan.FromSeconds(5)),
    startupToken);
IChannel channel = await session.CreateChannelAsync(
    new CreateChannelOptions(
        publisherConfirmationsEnabled: true,
        publisherConfirmationTrackingEnabled: true),
    startupToken);
await channel.QueueDeclarePassiveAsync(queue, startupToken);

// Native host composition, after building the host and before executing it:
using var observation = session.ObserveTerminalShutdown(
    lifetime.ApplicationStopping,
    _ =>
    {
        Environment.ExitCode = 1; // Consumer process authority.
        lifetime.StopApplication();
    },
    logger);
await host.RunAsync();
```

Use `Gloaming.Messaging.RabbitMQ`, `RabbitMQ.Client` and the host's usual namespaces.
The complete executable examples are [standalone workers](../samples/Delivery/README.md),
the [finite journey](../tests/Fixtures/BrokerJourney/README.md) and
[multi-channel workflow](../../../samples/Wholesale/Worker/Broker/README.md).

## Ownership and native configuration

OpenAsync creates a fresh native factory, invokes configure once, then applies ProtocolTimeout
to its ContinuationTimeout. That property is deliberately controlled by session options; other
native configuration stays with the caller. The caller's external factory is never mutated.
Both timeouts must be positive finite values supported by .NET timers (at most 4,294,967,294 ms).
Invalid values fail before configuration or connection. Native minimum close windows still
apply: smaller positive values do not promise a smaller shutdown duration.

CreateChannelAsync returns an owned native channel. Select confirmations/tracking and separate
channels for concurrent roles explicitly. The session tracks channels before exposing them;
all acquired channels are included in cleanup after partial consumer setup failure. Use them
directly but let the session dispose them. All owned channels share its lifetime; while terminal
observation is active, closing any of them is session loss. Do not change native lifetime settings
while relying on the selected cleanup policy. No queue creation/check/deletion, routing or acknowledgement
is hidden here. Native automatic topology recovery, if enabled by native configuration,
still performs its own recovery of previously declared topology.

Acquisition and disposal share a lifecycle guard. New acquisition after disposal starts fails
with ObjectDisposedException; an already admitted native creation is included in cleanup.
Repeated/concurrent disposal observes one cleanup outcome. Publication/intake is not serialized
or pooled by this guard; native per-channel concurrency rules remain the caller's obligation.

DisposeAsync aborts the connection first, disposes channels in reverse acquisition order, then
the connection, attempting all acquired resources despite errors. A single cleanup failure
retains its exception/stack; multiple failures use the BCL AggregateException in attempt order.
Native startup/protocol errors keep their original types. No transport-rejection result is
invented. A selected ProtocolTimeout also covers the internal channel-zero close continuation,
which can otherwise wait after the connection-abort window finishes.

## Optional observation and recovery choice

Native automatic recovery is not changed. Native defaults enable it. Finite consumers may
use ownership/cleanup alone. WorkflowBroker keeps recovery enabled and owns three channels;
native connection recovery, topology recovery and their limits remain RabbitMQ.Client behavior.
The integration does not supply reconnect/session replacement or certify recovery from every
permanent channel/topology error.

ObserveTerminalShutdown requires automatic recovery to be explicitly disabled; contradictory
configuration throws InvalidOperationException. Only one observer registration is allowed per
session lifetime. Disposing it does not permit another registration.
Connection and all owned channels are observed, including later channels. An original close
reason retained before registration is considered when registering; a lifetime already
cancelled at registration suppresses admission. Once a failure is admitted, later cancellation
does not revoke it. Concurrent stop/close is classified at that admission gate.

The callback receives the original native ShutdownEventArgs at most once. It must be short,
nonblocking and nonthrowing: signal stop, without waiting for cleanup, storage or termination.
Never synchronously dispose the session from that callback. A faulty/noncooperative callback
is consumer code; the integration cannot guarantee the selected host response in that case.
The library never sets process exit status or invokes Generic Host itself.

Cancelling the intentional-stop token or disposing the session suppresses subsequent intentional
close notifications. Disposing the registration detaches observation/diagnostics and prevents
new callback admission; an already admitted callback may finish. Optional ILogger diagnostics
cover terminal loss and native blocked/unblocked notifications. Resource blocking is diagnostic,
not terminal loss or confirmed publication rejection. No payload, URI or credential is logged.

## Delivery and shutdown limits

Stop intended operations cooperatively before disposal. Neither phase timeout wraps arbitrary
publisher/handler code or promises a process wall-clock bound. Publisher-confirm attempt timers,
native handshake/socket settings, protocol continuations and connection close are different
policies. External supervision still owns restart/backoff, grace and forced termination.

Cancellation, timeout or close does not establish transport rejection. Publication stays
outside the outbox transaction; identity, lease fencing and claim/failure accounting stay with
the store. This package does not release claims or replay messages. Intake commits before ack;
receipt retention controls duplicate suppression. See [recovery/shutdown](../docs/worker-recovery.md).
