# Lease fenced outbox failure accounting and operator retry

Status: interface and scope approved by the owner on 2026-10-21; implementation prepared
for owner line-by-line review. Extends [ADR 0009](0009-module-owned-transactional-outbox.md).

## Context

Outbox publication occurs after its claim transaction commits. The inbox's retained lock
and savepoint protocol cannot make external publication atomic. Existing Attempts counts
committed claims, including abandonment and uncertain completion, and cannot supply a
publication-failure budget. An unrestricted retry write could overwrite a successor claim.

## Decision

Record publisher-exception observations only while the message's current token and
database-clock lease remain valid. Acquire a row lock in a new short ReadCommitted
transaction, read counts, invoke consumer policy, and recheck the fence at the final UPDATE.
Completion likewise locks before its final expiry check. Never hold a database transaction
across publication. Completion errors never invoke publication-failure policy.

Keep Attempts unchanged in meaning; add lifetime/cycle failure counts, monotonic revision,
safe last-failure metadata, dead-letter state and operator retry evidence to the retained
outbox row. Consumer policy selects nonnegative delayed retry or dead-letter state. Omission
retains indefinite fixed-delay retry with durable observations. An observed exception does
not establish transport rejection; lost responses, cancellation and process death preserve
their uncertainty.

Expose one context-typed IOutboxDeadLetters for bounded metadata List/Get and route/message/
revision-conditional Retry. Retry changes eligibility and preserves the complete envelope,
Attempts and lifetime failure evidence. Keep the existing IInboxDeadLetters/public DTOs and
inbox storage intact. Share only the private timestamp/UUID position codec, retaining inbox
token compatibility. No generic queue protocol or duplicate recovery interface is introduced.

OutboxFailureAccountingException preserves publication/accounting errors. The optional worker
faults without another poll; native host supervision and consumer repair/restart govern
recovery. Consumer code owns classification, budgets, acceptance, admission, operator authority,
credentials, presentation and audit. Column grants do not enforce legal lifecycle transitions.

## Consequences

Forward consumer migrations and restricted-grant updates precede deployment. Old dispatchers
must stop before dead letters are used because they ignore the new predicate. Counts cap
committed observations, not every execution or accepted delivery. An obsolete publisher
can still accept externally after successor dead-lettering or operator retry; its database
writes remain fenced. Receiver deduplication is required.

Payload editing, completed-message replay, retention, lease renewal, a generic administration
framework and template presets are excluded. See the [current family contract](../../src/Gloaming.Messaging/docs/outbox-recovery.md),
[reviewed proposal](../plans/m2-outbox-dead-letters.md) and
[new evidence report](../reports/m2-outbox-dead-letters.md).
