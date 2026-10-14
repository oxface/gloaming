# ADR 0008: Native optimistic aggregate rebuilding

Status: owner-approved replacement, 2026-10-07. Supersedes
[ADR 0007](0007-pre-read-admission-for-inline-rebuilding.md) and the public two-phase append
mechanism described in ADR 0005; the aggregate contract and transactional required-state
choice remain. [Reviewed scope](../plans/es2-native-ef-simplification.md).

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

The shared EF package references Relational and DI abstractions without a provider dependency.
PostgreSQL is the tested runtime. A future concrete pessimistic-locking obligation may justify
*.Postgres anew; no empty abstraction/package is retained. Scheduling, scaling, retry/window
policy, upcasting, catch-up, secondary/multi-stream projections and a maintenance worker remain
separate capabilities with their own proof obligations. T1 remains event-free.

[Current consumer contract](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/README.md)
and [dated executions](../reports/es2-single-stream-rebuilding.md) distinguish this contract from
historical gate/SQL diagnostics.
