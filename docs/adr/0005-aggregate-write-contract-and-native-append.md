# ADR 0005: Aggregate write contract and configured native append

Status: accepted aggregate contract, originally owner-reviewed on 2026-10-06.
The original prepared append surface is superseded by [ADR 0008](0008-native-optimistic-aggregate-rebuilding.md);
the current EF store stages writes directly.

## Context

The initial technical append concentrated EF obligations but exposed repeated header/version/
timestamp/factory assembly in commands. Owner review requested an aggregate/store journey and
library ownership of append metadata. The archive supplies aggregate/store behavioral evidence;
Marten supplies a behavioral reference. Neither is a selected replacement engine or runtime
dependency. Existing inline/history loading already demonstrates different consumer needs.

## Decision

Require IEventSourcedAggregate for event-sourced append. Provide a package-free EventSourcing
core with optional EventSourcedAggregate inheritance to preserve captured version and accept
complete candidates/pending facts atomically. Domain operations, Evolve and candidate policy
stay in owning consumers. Historical initialization creates no pending facts and runs no
current decision policy. ApplyChanges mutates bookkeeping; Evolve computes whole-batch state.

The native EF segment provides a configured EventStore with direct header/event/required-state
staging. It assigns GUIDs, contiguous positions and one configured-clock timestamp, owns payload
lifetime and validates complete mapped keys/native lifecycle. Consumer mappings supply domain
encoding and reconstruction. [ADR 0006](0006-transactional-main-inline-state.md) requires the
one registered inline aggregate; ADR 0008 removes prepared handles and pre-read admission.

The caller starts the native transaction, saves and commits/rolls back. History reading and
rebuilding are independently selected operations, not implicit write/load repair. The
[current contract](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/README.md)
defines the actual public surface; no transaction wrapper or event-discovery runtime follows.

## Consequences

Independent adoption costs a core reference plus the native EF segment. The core has no EF,
JSON or package dependency. Domain facts/state contain no persistence payload type. Shared
pure reducers can remain encapsulated inside modules without exposing aggregate mutation.
Different view shapes retain different reducers; business Contracts and policies stay local.

Pure immutable consumer state/facts/evolution are required for candidate atomicity. Persistence
failure or rollback requires disposal and fresh loading/redecision; no pending reset, retry,
ambiguous-commit recovery or arbitrary side-effect rollback is supplied. PostgreSQL is the
proved provider. State-stored adopters need no event-sourcing dependency. Future engine/reader/
projector capabilities need separate evidence and owner review.

[Reviewed interfaces and exact scope](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/es1-bounded-event-append.md#concrete-aggregate-based-replacement-for-interface-review),
[implementation and verification](https://github.com/oxface/modulith-foundry/blob/bd2adb7dea58de304aa2eb3df3c587c6fe348320/docs/reports/es1-bounded-event-append.md).
