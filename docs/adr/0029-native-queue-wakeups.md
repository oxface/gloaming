# 0029: Optional native queue wakeups retain polling truth

Date: 2026-10-24. Status: implemented as application guidance.

## Decision

Provide an executable native PostgreSQL LISTEN/NOTIFY application recipe around the existing typed
inbox/outbox operations. There is no new reusable Gloaming interface/package or dependency added to
provider-free hosting. Npgsql owns native waiting/cancellation; bounded channels retain per-worker
hints. The consumer owns sessions, trigger migration, endpoints, credentials, backoff, supervision
and polling cadence. A wrapper around native waiting would leave these application choices exposed
while adding another interface. The two ordinary callers retain their existing transaction/fencing
and failure-accounting guarantees.

Use a dedicated native session, attach handlers before committed LISTEN registration, then force a
durable-state poll. Retain independent bounded hints and poll periodically regardless of listener
state. Reconnect registers and forces another poll. Worker failure remains terminal to the selected
consumer supervisor; a degraded listener cannot suppress completion/accounting uncertainty.

## Consequences

This establishes **no new reusable mechanism**. Notifications are an optional latency choice, never
durable work, authorization or acknowledgment. Queue eligibility can change with time alone. Session
and notification-queue overhead remain; queue saturation can fail producer commit. Fixed empty
payload triggers support unchanged native writers, additive rollout and removal back to polling.
Restricted grants and native cancellation/reset/reconnect behavior need actual execution.

The [public recipe](../../modular-monolith-patterns/postgres-wakeups.md),
[native fixture](../../src/Gloaming.Messaging/tests/Fixtures/Wakeups/README.md) and
[independent package consumer](../../tools/release/consumers/Wakeups.cs) own the complete composition
and supported limits. No automatic worker adoption, hosted scheduler, FIFO or external exactly-once
guarantee is selected. A future shared module needs repeated application obligations and its own
depth/adoption proof; this decision does not preempt that evidence.
