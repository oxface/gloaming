# ADR 0013: optional fixed sequential saga progress

Status: superseded on 2026-10-12 by [ADR 0014](0014-bounded-durable-continuations.md), before
checkpoint. Retained as historical design/evidence for the discarded fixed-sequence experiment.

## Context

WF2 demonstrated separately committed stock reservations and acknowledged compensating release.
Its consumer retained expected command IDs, original outcomes and retry attempts explicitly.
The owner's follow-up requested a small reusable coordinator rather than concluding that
existing delivery/transaction primitives removed all worthwhile saga bookkeeping.

## Decision

Provide optional Gloaming.Messaging.Sagas for one fixed ordered sequence. Persist its copied
plan and requested attempts with their outcomes in an explicit versioned state document.
Derive technical progress from that ledger. Equal old outcomes do nothing; conflicting outcomes
fail. Only an explicit business refusal initiates reverse compensation of earlier accepted
steps. Each undo waits for acknowledgement. Unsupported undo or refused undo requires attention;
only a refused command compensation permits an explicit new attempt.

Provide an optional EF adapter that maps the document inside a consumer-owned entity. Use an
independent snapshot for mutation detection and the original document as a native optimistic
concurrency token. PostgreSQL JSONB is the first proved mapping. Other providers and a relational
status projection need their own assessment rather than inferred compatibility.

Consumers retain entity/resource identities, full business reply validation, compensation
declarations, payloads/routes, admission and native transaction control. Existing inbox/outbox
composition commits progress, returned next action and completion together. No bus runtime,
discovery, saga store, transaction facade, worker or automatic business retry is introduced.

## Consequences

Repeated technical ordering and attempt bookkeeping move into one mechanism, exercised by
Sales and an independent ordinary EF adopter with two reverse undos. Explicit consumer wiring
remains visible. The library cannot enforce that custom code enqueues its returned action.

The serialized ledger grows with retries; full-document comparison and bookkeeping reconstruction
have no throughput claim. Nested progress members are not native LINQ query fields. A failed
database operation requires a fresh scope because rollback does not restore the mutable object.
The fixed sequence cannot replace branching/parallel workflows, cancellation, deadlines,
workflow migrations, retention or operator tooling. Missing replies never imply business failure.

[Reviewed scope](../plans/wf2-sequential-saga-coordinator.md),
[fresh evidence](../reports/wf2-sequential-saga-coordinator.md), and
[current replacement contract](../../src/Gloaming.Messaging/docs/workflows.md) distinguish the
historical experiment from the supported continuation composition.
