# Native queue wakeup composition

This application-owned fixture adds no reusable Gloaming mechanism. `NativeWakeups` owns a dedicated
native receive session, separate bounded inbox/outbox hints and reconnect registration. `QueueConsumer`
supplies actual typed library callers in fresh scopes, a durable input handler, outgoing publisher
and a supervisor preserving terminal accounting faults while cancelling/awaiting the remaining tasks.
Fallback polling proceeds independently when the listener is disconnected or deliberately absent.

The [owning provider tests](../../OutboxPostgresTests/WakeupTests.cs) cover backlog and trigger rollout,
notification-before-wait, coalesced/untrusted hints, eligibility without writes, reconnect, exclusive
wait cancellation, pooled reset, multiplexing refusal, restricted grants and fatal worker supervision.
`Earlier` is a separate native writer with no Gloaming references; its identical SQL works before/
after trigger installation and removal. Its ASP.NET shared runtime reference matches the test output
used by ChildHost and supplies the native driver's logging dependency; it uses no ASP.NET APIs.

The [public native recipe](../../../../../modular-monolith-patterns/postgres-wakeups.md) owns setup and
limits. The [package-only adopter](../../../../../tools/release/consumers/Wakeups.cs) owns a different
bounded notification/polling composition with restricted credentials and its own initial schema.
That initial fixture layout is not a populated migration certificate; compiled rollout is proved
by the separate native host/tests. No real Umbrine application or transport matrix is implemented.
