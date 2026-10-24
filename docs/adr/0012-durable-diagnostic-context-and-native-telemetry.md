# ADR 0012: durable diagnostic context and native telemetry

Status: accepted and implemented, 2026-10-10.

## Context

Separate producer/intake/processing lifetimes cannot share ambient process
context. Business delivery/conversation/cause IDs survive persistence, but cannot reconstruct
a trace relationship. Native RabbitMQ instrumentation can inject a different send context
on each publication, including repeated delivery of the same retained message.

## Decision

Retain explicitly supplied optional W3C TraceParent/TraceState on incoming/outgoing envelopes
and provided durable rows. These are diagnostic metadata, separate from business identities
and admission. Core remains BCL-only; no ambient capture or mandatory OTel runtime is added.
Malformed diagnostic parents cannot reject otherwise valid business work.

Exclude diagnostic context from inbox duplicate comparison and keep the first committed
intake's context. Include it in immutable outgoing-envelope validation. Consumers apply
additive nullable-column migrations; library registration does not migrate databases.

Instrument existing callable PostgreSQL dispatch/processing with native ActivitySource and
Meter. Link retained context at activity creation; use distinct attempts while preserving a
local ambient parent. Record observed completion outcomes and bounded operation/result
metrics. Optional native ILogger logging occurs while the attempt activity remains current.
SDKs, subscriptions, exporters, sampling, transport headers and monitoring policy belong to
editable hosts/adapters. No shared Gloaming host/telemetry framework is selected.

The internal native trace/metric mechanism resides in the package-free messaging core.
Explicit friend access lets the PostgreSQL assembly use it without exposing a new public
API. The operation implementations own generated failure logging and the placement of
attempt boundaries. ILogger dependencies stay with those operations/hosted workers rather
than the core. Operation failures and native Npgsql/EF database diagnostics are distinct.

Consumers subscribe with native AddSource("Gloaming.Messaging") and AddMeter("Gloaming.Messaging")
calls. SDKs, exporters and sampling remain host configuration.

## Consequences

Fresh processes can recover diagnostic relationships without changing transaction ownership,
at-least-once publication or delivery/business idempotency semantics. Trace context can differ
across retries without becoming an inbox conflict. Compiled consumers rebuild for the optional
constructor additions, and existing databases migrate before using the updated model.

[Outbox backlog observation](../../src/Gloaming.Messaging/docs/outbox-observability.md) is a
separate callable capability. Alert policy/dashboard UX, baggage, automatic tenant/actor
propagation and durable telemetry remain outside this diagnostic-context contract.
See [local setup and limits](../../src/Gloaming.Messaging/docs/observability.md).
