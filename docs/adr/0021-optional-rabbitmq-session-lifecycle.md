# 0021: Optional native RabbitMQ session lifecycle

Date: 2026-10-22. Status: accepted and implemented in the optional native RabbitMQ session package.
The original owner-approved scope dates to 2026-10-22; current setup and limits are
[in the package contract](../../src/Gloaming.Messaging/Gloaming.Messaging.RabbitMQ/README.md).

## Context

Real broker/process proofs reproduced two consumer failures: ordinary dispatch retry against
a permanently closed channel, and connection disposal waiting after its native abort window.
The worker fixed these with native shutdown observation and separate continuation/abort
settings. Finite journeys and WorkflowBroker repeat connection/channel ownership and cleanup
with different channel counts and recovery policies. Consumer ownership of policy does not
require duplicating those mechanics.

## Decision

Provide optional Gloaming.Messaging.RabbitMQ with RabbitMqSessionOptions and RabbitMqSession.
It owns a fresh native factory/connection and explicitly created channels, returns original
IChannel interfaces, applies caller-selected native continuation/close settings, serializes
resource acquisition against disposal and attempts all acquired cleanup.

An optional intentional-stop token and synchronous shutdown callback observe a nonrecovering
session once, including late registration and later channels. Optional native logging records
terminal/blocked/unblocked states. The package does not reference Generic Host or set process
exit status. The native host consumer signals its own StopApplication/status in a short callback.

Native automatic recovery remains native configuration. Terminal observation rejects recovery-
enabled sessions; those can adopt ownership/cleanup alone. WorkflowBroker retains automatic
recovery and its separate command/reply/intake channels. Native publisher confirmations,
queue topology, wire mappings, admission and business behavior remain consumer/native APIs.

The package references only RabbitMQ.Client and logging abstractions, with no Messaging
core/EF/workflow dependency. Existing core/EF packages remain transport-independent. No
parallel publisher, operator retry, dead-letter or transport-manager interface is introduced.

## Consequences and limits

Consumers reuse shutdown race handling, resource ownership and cleanup rather than copying
the worker's policy-specific BrokerSession. Explicit factory configuration and two timeout
values retain deployment choice. An observer callback remains trusted short consumer code;
external supervision owns restart/backoff and eventual forced termination.

Timeout/close does not establish rejection or roll back external acceptance. Claim transactions,
lease fencing/expiry, identity-preserving retries and receipt lifetime remain unchanged. Native
phase timeouts do not promise arbitrary publisher/exporter or process shutdown deadlines.
Native automatic-recovery compatibility is tested for live broker application restart; every
network blackhole, permanent topology failure and whole-workflow outage is not certified.

The [implementation report](https://github.com/oxface/modulith-foundry/blob/bd2adb7dea58de304aa2eb3df3c587c6fe348320/docs/reports/rabbitmq-session-lifecycle.md) separates fresh public-
interface/adoption proofs from the earlier consumer-only evidence. No template preset,
database schema/grant, dependency version or generic recovery framework follows from this decision.
