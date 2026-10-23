# ADR 0006: Transactional main inline state for registered aggregate writes

Status: accepted consistency direction, owner-confirmed on 2026-10-06. The owner subsequently
endorsed a provided write store and authorized implementation for review on 2026-10-07.
The exact configuration/implementation remains subject to line-by-line review. Supplements
ADR 0005; see [the store surface](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/es1-library-write-store.md).

## Context

Ordinary aggregate commands need current state for eligibility, and native EF reads need
persisted queryable state. Updating that main state asynchronously would allow it to lag the
stream and would require a different command-loading contract. The owner requires events
not to commit without their main inline state, but also wants independent raw-stream usage.
The current history-only counter demonstrates a consumer without an inline view.

ES1's module stores prepare required views in the consumer's native transaction. The public
appender can stage events/header without any view. A transaction ensures included writes
commit together; it does not ensure required state was included. Existing consumer proofs
therefore do not establish universal library enforcement.

## Decision

For streams registered for aggregate writes, require one main inline state carrying the
version it represents. Accepted events, stream header and that state advance to the same
version in one explicit consumer-owned native EF transaction. A participant failure must
roll back all of them. The owner-approved ES2 reduction limits active library orchestration to
that one aggregate state; secondary inline/async projections need a future reviewed contract.

Explicit raw streams remain permitted without main inline state. Implementing the aggregate
bookkeeping interface alone does not register a stream for the stronger consistency guarantee.
The existing counter may retain its history-based loading and direct JSON encoding.

Keep domain rules, evolution, view definitions, tenant admission and final save/commit in the
consumer. Ordinary aggregate writes load validated inline state; historical reconstruction
remains an explicit alternative read path. Do not introduce snapshot-tail repair or async
projection processing as part of this decision.

## Consequences

Ordinary reads can use native EF against current persisted main state. Aggregate streams
pay for synchronous state maintenance and reject broken required state under the consumer's
documented policy. Raw streams keep independent adoption without an implicit projection.

The provided store declares one required aggregate state explicitly. The earlier ES1
secondary-view contract is historical checkpoint evidence, superseded by the
[reviewed ES2 reduction](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/es2-aggregate-only-reduction.md). Native model
registration and validation in both consumer save overrides check the active transaction,
header, complete inserted event range and required state metadata before SQL. New omission,
corruption, rollback and recovery proofs appear in [the follow-up report](../reports/es1-library-write-store.md).
This is a tracked native-save contract: raw SQL, bulk updates/deletes, external writers and
bypassed save overrides require a separate database enforcement contract. State-body semantic
validation remains consumer-owned. No RLS or async consistency mechanism is established.

See [the capability directions](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/event-sourcing-capabilities.md),
[ADR 0005](0005-aggregate-write-contract-and-native-append.md) and
[existing ES1 evidence](../reports/es1-bounded-event-append.md).
