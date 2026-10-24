# ADR 0006: Transactional main inline state for registered aggregate writes

Status: accepted and implemented, 2026-10-06. Supplements
[ADR 0005](0005-aggregate-write-contract-and-native-append.md); native direct staging and
optimistic rebuilding follow [ADR 0008](0008-native-optimistic-aggregate-rebuilding.md).

## Context

Ordinary aggregate commands need current state for eligibility, and native EF reads need
persisted queryable state. Asynchronous main-state updates could lag the stream. A transaction
makes included writes atomic but does not ensure required state was included.

## Decision

For streams registered for aggregate writes, require one main inline state carrying the
version it represents. Accepted events, stream header and that state advance to the same
version in one explicit consumer-owned native EF transaction. A participant failure rolls
back all of them. Ordinary loading validates current inline state; historical replay and
regeneration remain separate explicit maintenance operations.

Keep domain rules, state evolution, state definitions, tenant admission and final save/commit
in the consumer. Ordinary loading never silently repairs state or republishes historical effects.
Independent event-history utilities do not require registration for aggregate writes.
Secondary inline, asynchronous and multi-stream projections require a separate contract.

## Consequences

Ordinary reads can use native EF against current persisted main state. Aggregate writes pay
for synchronous state maintenance and reject broken required state under the documented policy.

The native model and save validation check aggregate keys, the active transaction, stream
header, complete inserted event range and required state metadata before SQL. State-body
semantic validation remains consumer-owned. Raw SQL, bulk writes, external writers and
bypassed save validation need separate enforcement.

See the [current store contract](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/README.md).
