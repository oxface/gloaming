# ADR 0018: Serialized inbox failure accounting and conditional operator retry

Status: interface/scope owner-approved, 2026-10-18; supervision approved 2026-10-20;
dead-letter interface approved 2026-10-21.
Implementation remains uncommitted for line-by-line owner review.
[Reviewed scope](../plans/m2-inbox-failure-recovery.md),
[current contract](../../src/Gloaming.Messaging/docs/inbox-recovery.md),
[fresh evidence](../reports/m2-inbox-failure-recovery.md).

The existing post-transaction pending-only retry update protects successor completion but
cannot safely own counters/dead-letter state alongside newer pending failures or operator redrive.
Reading a revision inside an attempt that fully rolls back does not establish durable attempt
identity: competing failed attempts can observe the same committed revision.

Retain native PostgreSQL row exclusion. Acquire the delivery lock before a savepoint, execute
local handling/save/completion after it, and on failure roll local work back to that savepoint.
The surviving transaction retains the original inbox lock and commits only confirmed failure
accounting and the consumer's retry/dead-letter state decision. Failed tracked proposals are discarded.
There is no later accounting transaction, expiring inbox lease or public claim protocol.

Cancellation, transaction loss and process death can leave no recorded failure. Counts mean
committed failure observations, not all executions. Commit response loss is ambiguous; do not
issue compensating failure writes after attempting completion/accounting commit. Fresh retained
state governs the next operation.

The [approved supervision follow-up](../plans/m2-inbox-failure-supervision.md) distinguishes
unconfirmed failure accounting with InboxFailureAccountingException, derived from
AggregateException and retaining delivery identity and both original errors. The optional
inbox worker faults without polling again for that exception. Native host supervision applies
the consumer's configured response; the library installs no host-wide setting. This rejects
blind repeated handler execution when accounting is broken, while preserving the native
transaction protocol. Consumer alerting, repair and restart remain required; stopping does
not establish progress for unrelated messages on that worker or count unrecorded crashes.

A separately registered typed recovery operation conditionally changes only an unprocessed,
dead-lettered delivery at the observed revision. It preserves the entire envelope and lifetime
failure count, resets cycle failures and records redrive count/time. Revision increments on
failure, completion and redrive prevent old requests reopening a later dead-letter state. PostgreSQL
locking and condition rechecks arbitrate competitors.

Consumers own scoped keyed failure policy, classification/codes/budgets, producer/tenant admission,
operator authentication/authorization, presentation and audit. No Access/Tenancy/ActorIdentity
dependency or generic management framework is introduced. Optional worker composition remains.
Old processors must stop before dead-letter state is used; consumer migrations and restricted grants
precede deployment. Outbox recovery, retention, payload editing and schema evolution are excluded.

This supersedes ADR 0010's separate retry-write implementation, while preserving retained intake,
deduplication, independent module commits and atomic successful local handling.

The owner approved the [dead-letter follow-up](../plans/m2-inbox-dead-letters.md) on 2026-10-21.
IInboxDeadLetters replaces the unreleased recovery interface and adds bounded metadata list/detail
around the same conditional retry. DeadLetter/DeadLetteredAt replace public quarantine names;
the original `quarantined_at` storage column and historical migrations remain. Live keyset paging
uses database timestamp/UUID order, grants no authority and reserves no work. Consumer auth/RLS,
repair and restart policy remain explicit. Forward migrations add only the lookup index.
