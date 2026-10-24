# ADR 0026: native entity-work exclusion

Status: accepted architecture decision. Exact changes remain subject to owner review before commit.

## Context

Independent deliveries may target one consumer entity and require technical mutual exclusion.
The owner explicitly selected exclusion without FIFO, leaving business policy with consumers.
The native PostgreSQL transaction advisory-lock operation already owns acquisition/waiting,
cancellation and lock release. A public wrapper with the same lifetime and arguments would add
little depth while creating another supported library interface.

## Decision

Document and execute an ordinary native consumer recipe instead of extracting a new reusable
mechanism. Cooperating writers share an unambiguous versioned full-key framing and separate
advisory namespace. Acquire before business decision reads in the original native transaction;
hold through state/outbox save and commit. Keep native constraints/concurrency guards.

Use transaction advisory locks, not pooled session locks. Equal keys exclude; a hash collision
adds contention. Different complete keys can proceed. Every writer must cooperate. Deployment,
key canonicalization/admission, bounded operation, retry/failure disposal and multi-key ordering
remain consumer responsibilities. No FIFO/fairness or retained duplicate evidence follows.

## Consequences

No package, library table, lock manager or worker runtime is introduced. Npgsql-only executable
workers and native provider tests preserve the selected protocol, actual blocked acquisition,
different-key progress, rollback/death/cancellation/commit release and restricted credentials.
The [owning recipe](../../modular-monolith-patterns/entity-work.md) documents complete setup,
transaction and mixed-writer rollout obligations. Revisit extraction only when actual consumers
demonstrate repeated complexity beyond this native composition.
