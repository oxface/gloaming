# ADR 0008: Native optimistic aggregate rebuilding

Status: accepted and implemented, 2026-10-07. Replaces cooperative pre-read admission
and the earlier two-phase append surface. The aggregate bookkeeping contract and required
transactional inline state remain.

Add an application-managed Guid ConcurrencyStamp on the stream header alongside the native
Version concurrency token. Every append and full rebuild changes the stamp. Repair preserves
retained facts, event version and recorded timestamps. EF retains observed original tokens in
its UPDATE predicates, so a same-version repair invalidates a writer's stale decision, append
invalidates captured repair, and competing repairs have one winner. Any already-issued state
or event SQL remains inside the consumer's explicit transaction and must roll back after failure.

Use native optimistic conflicts rather than cooperative pre-read admission. Operations can
load concurrently and lose at save. Consumers must dispose/reload/redecide after failure; they
may add operational exclusion when needed. Neither this mechanism nor the previous gate undoes
bad commands already committed or fences writers bypassing the model/transaction contract.

Writing and maintenance are independent scoped implementations. The store requires its event
and aggregate-state mappings, not a history reader or maintenance configuration. The public
rebuilder uses consumer full-prefix reading and effect-free evolution, then directly applies
state to the native EF unit of work. Remove public prepared handles and the advisory-lock provider
package. A private exact maintenance-write association belongs in the native save guard; no public
bypass or reusable permit framework is introduced. SaveChanges and commit remain consumer-owned.

Maintenance association, proposal validation and conflict identity live together in a private
module. An unchanged participant must match its native stored values in the original transaction;
tracking state cannot stand in for successful persistence or survive a rolled-back savepoint.
The synchronous guard uses native synchronous reads, while registered async saves and the
optional executor use cancellable async reads. This preserves healthy stored state and accepted
repeated saves without adding a public lifecycle hook or changing the storage model.

The shared EF package references Relational and DI abstractions without a provider dependency.
PostgreSQL is the tested runtime. ADR0028 subsequently adds a separately selected committed
feed/projection protocol with its own native write-lock cost; it changes neither this adapter's
dependencies nor optimistic rebuilding. Scheduling, scaling, retry/window policy, upcasting and
secondary projection workers remain separate selections with their own contracts. State-stored
adopters need no event-sourcing dependency.

See the [current consumer contract](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/README.md) and its native provider tests.
