# Optional PostgreSQL queue wakeups

Use notifications to shorten an application worker's idle wait while keeping periodic polling
authoritative. This is native application composition using existing typed Gloaming inbox/outbox
operations, Npgsql and bounded channels. It establishes **no new reusable library mechanism** and
adds no dependency to the provider-free hosting package. The standard polling hosts remain useful
without this optional recipe.

## Native session and startup

The application owns a dedicated direct PostgreSQL connection, independent of all business/
processing contexts. Use `Pooling=false`, `Multiplexing=false`, `Enlist=false`, an explicit endpoint,
bounded connection/command timeouts and a reconnect delay. A listener must retain one server
session; do not use transaction-pooled endpoints. The executable proof uses direct PostgreSQL18
with Npgsql10.0.3. Other proxies/topologies have no execution certificate here.

Attach `Notification` before issuing fixed reviewed `LISTEN` commands. Commit registration outside
a business/ambient transaction, then force each selected worker to poll durable state. A notification
may be consumed while processing an ordinary command before a later `WaitAsync`; the callback must
retain a hint. Never assume the next wait replays an already consumed event.

Give the inbox and outbox worker **separate capacity-one signals**. The callback only performs a
nonblocking signal write; it runs no handler, query or publication. Bursts coalesce with bounded
memory. The proved composition has one reader per signal; additional workers need their own signals
or an explicitly selected application fan-out policy. A signal is neither work nor authority.

Run one exclusive native receive pump. On connection failure, dispose that session, report degraded
wakeups, wait the selected reconnect delay, open/register again and force fresh polls. Worker polling
continues independently throughout listener outages. Hints lost during disconnect are harmless to
correctness. Cancel and await the pump before disposing its connection; never command or close a
connection concurrently with its wait. Cancellation/keepalive may break a session: discard it.

Npgsql's integer `WaitAsync(timeout, token)` needs a positive bounded millisecond timeout when
bounded waiting is intended;0/-1 mean indefinite. Multiplexing is unsupported. Driver pooled reset
removes LISTEN registrations; a new/reopened session always registers again. Each held listener
consumes a database connection even when no queue work exists.

## Two ordinary callers

For each inbox poll, create a fresh established scope and call
`IInboxProcessor<ModuleDatabase>.ProcessNextAsync(selectedSubscription, token)`. For each outbox
poll, independently create a fresh scope and call
`IOutboxDispatcher<ModuleDatabase>.DispatchNextAsync(token)`. Dispose each operation scope before
idling. After `NoWork`, consume its retained hint or wait for a hint **or a positive fallback delay**,
then poll again. A hint never selects a message, tenant, subscription or acknowledgment.

Use a cancelled-and-awaited losing read, not an abandoned `Task.WhenAny` read that could steal the
next hint. The executable recipe uses a linked cancellation source with the fallback deadline and
awaits its channel read to completion. Eligibility, retry availability and lease expiry can change
solely with time; insert/update notifications cannot replace that fallback.

The consumer supervises listener and workers together. Unexpected worker completion or any thrown
processing/publication/accounting fault reaches the host and cancels the other tasks. Listener
connection errors only degrade wakeups; they must not convert fatal `InboxFailureAccountingException`,
`OutboxFailureAccountingException` or `OutboxCompletionException` into ordinary retries. Cancellation
and cleanup have independent budgets; preserve original failures. The recipe selects terminal
supervision for thrown worker errors. Backoff, restarts, logging and readiness remain application
policy; no universal retry loop or Gloaming runtime is introduced.

## Additive migration and grants

The consumer installs reviewed `AFTER INSERT` triggers on its selected inbox/outbox tables. A fixed
`SECURITY INVOKER` function calls qualified `pg_catalog.pg_notify` with a constant channel and **empty
payload**, using a trusted fixed search path. Schema/table/function ownership stays with migration
credentials. Runtime credentials need normal queue/handler grants plus database CONNECT; they do not
need schema CREATE, function ownership, trigger changes or deletion. Prove actual grants, including
any locally revoked native function privileges. Notifications do not change RLS or admission policy.

Pause/coordinate native DDL as appropriate to the application's migration process. Install additively
over retained rows; the initial post-registration poll drains pre-trigger work. Unchanged older
writers signal through the database trigger. Removing triggers leaves ordinary polling functional.
Keep owned table mappings, migrations and compiled old/new writers; no library release compatibility
claim follows merely from compiling the current source twice.

Notifications commit with the producer transaction and are not durable offline delivery. Identical
hints within one transaction can fold; spurious/untrusted hints may cause an extra poll. Notifications
are visible to database users, so channels/payloads must contain no secrets. A saturated native
notification queue can fail the notifying transaction's **commit**; selecting a trigger therefore
adds a producer failure mode. Monitor native queue usage and transaction duration, and recover failed
producer decisions through their existing fresh-context transaction protocol. There is no external
exactly-once, FIFO, zero-latency or always-available-listener guarantee.

## Executable owners

The [native application fixture](https://github.com/oxface/gloaming/blob/main/src/Gloaming.Messaging/tests/Fixtures/Wakeups/README.md) contains
the complete pump, typed callers and fault supervisor. [Provider tests](https://github.com/oxface/gloaming/blob/main/src/Gloaming.Messaging/tests/OutboxPostgresTests/WakeupTests.cs)
cover actual queue processing, disconnected fallback/reconnect, cancellation, pooled reset,
notification-before-wait, bounded coalescing, restricted grants and separately compiled earlier
writes across trigger installation/removal. The [independent package consumer](https://github.com/oxface/gloaming/blob/main/tools/release/consumers/Wakeups.cs)
uses both real callable roles and native notifications from NuGet packages without Hosting.

Native semantics are described in [PostgreSQL LISTEN](https://www.postgresql.org/docs/18/sql-listen.html),
[NOTIFY](https://www.postgresql.org/docs/18/sql-notify.html) and
[Npgsql notification waiting](https://www.npgsql.org/doc/wait.html). The application chooses whether
the reduced idle latency justifies session/notification overhead; no throughput benchmark or
production latency objective is certified by these bounded proofs.
