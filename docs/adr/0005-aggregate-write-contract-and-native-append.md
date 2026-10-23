# ADR 0005: Aggregate write contract and configured native append

Status: accepted direction and interface/scope, owner-reviewed on 2026-10-06. Implementation
remains available for line-by-line review. Supersedes the uncommitted initial ES1 per-call
append surface; earlier accepted ADRs and event checkpoints remain in force.

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

The native EF event segment references that core and provides a configured EventAppender,
typed EventRecordAdapter and single-use PreparedEventAppend. The library assigns GUIDs,
contiguous positions and one configured-clock timestamp, clones JSON and validates complete
mapped keys and native lifecycle. Consumer row interfaces/mapping remain unchanged.

Consumer stores retain their existing load paths and observation/transaction association.
They choose and prepare required views explicitly. The caller starts the native transaction,
saves and commits/rolls back. No generic store, history reader, projector engine, transaction
wrapper, event discovery or template preset is introduced.

## Consequences

Independent adoption costs a core reference plus the native EF segment. The core has no EF,
JSON or package dependency. Domain facts/state contain no persistence payload type. Shared
pure reducers can remain encapsulated inside modules without exposing aggregate mutation.
Different view shapes retain different reducers; business Contracts and policies stay local.

Pure immutable consumer state/facts/evolution are required for candidate atomicity. Persistence
failure or rollback requires disposal and fresh loading/redecision; no pending reset, retry,
ambiguous-commit recovery or arbitrary side-effect rollback is supplied. PostgreSQL is the
proved provider. T1 remains event-free. Future engine/reader/projector capabilities need
separate evidence and owner review.

[Reviewed interfaces and exact scope](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/es1-bounded-event-append.md#concrete-aggregate-based-replacement-for-interface-review),
[implementation and verification](../reports/es1-bounded-event-append.md).
