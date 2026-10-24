# ADR 0025: bounded recurring intents

Status: accepted architecture decision. Public interfaces and implementation receive normal
owner review; this record does not replace exact change-set approval before commit.

## Context

Recurring technical work repeats more than next-date calculation: stable occurrence identities,
atomic progress/finite handoff, bounded backlog recovery and cancellation must survive concurrent
planners and process failure. The owner selected explicit calendars/time zones and missed policies.
Finite due-time intents remain independently usable. Business calendars and relevance remain
with their consuming application.

## Decision

One optional PostgreSQL recurrence package selects Cronos and the finite messaging package. It
retains immutable generations and versioned progress, locks one complete generation and stages
progress plus finite work in caller-owned native ReadCommitted transactions. Consumers explicitly
register, plan and stop, then save/commit; no scheduler runtime or implicit clock is introduced.
The private proposal association observes all exact native participants, including unchanged rows.

Use forward-only bounded calendar evaluation. Coalesce freezes a window and retains scan/candidate
progress until the latest eligible occurrence is established; it emits no partial candidate.
Skip and bounded earliest CatchUp are explicit alternatives. One future occurrence is armed.
Clock regression refuses planning. The complete generation and nominal UTC instant derive stable
versioned identities; native retained keys enforce uniqueness.

Retain evaluator/runtime/definition and caller-attested rule-data identity. An exact mismatch
refuses planning before progress changes. Serialized zone text is a conservative object mismatch
check, not a portable complete semantic fingerprint or frozen restorable calendar. Consumers pin
and verify their actual host data source. Planned UTC occurrences are immutable.

Stop disables planning and suppresses pending finite work in bounded resumable transactions.
Released work cannot be recalled. Consumers choose current-generation relevance in their own
business row and apply their own replacement/admission/receiver rules. Independent generation
registration does not implicitly supersede an existing generation.

## Consequences

Atomic planning complexity has one module and tests through its public interface; native EF,
context/transaction ownership and module migrations remain visible. Runtime/rules upgrades may
require explicit generation replacement even when the operator believes behavior equivalent.
Retain evidence for the module lifetime; removal ends duplicate/progress protection. No FIFO,
business holiday engine, human workflow policy or external exactly-once guarantee is selected.
The [owning contract](../../src/Gloaming.Messaging/Gloaming.Messaging.Recurrence.EntityFrameworkCore.Postgres/README.md)
defines complete setup, operational and compatibility obligations.
