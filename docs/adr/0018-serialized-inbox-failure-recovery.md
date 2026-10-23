# ADR 0018: Serialized inbox failure accounting and conditional operator retry

Status: accepted and implemented, 2026-10-18; supervision and dead-letter refinements
follow the same protocol. Receipt-generation refinement is in
[ADR 0020](0020-inbox-expiry-and-receipt-generations.md).
See the [current contract](../../src/Gloaming.Messaging/docs/inbox-recovery.md).

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

InboxFailureAccountingException distinguishes unconfirmed failure accounting. It derives
from AggregateException and retains delivery identity and both original errors. The optional
inbox worker faults without polling again for that exception. Native host supervision applies
the consumer's configured response; the library installs no host-wide setting. Consumer alerting,
repair and restart remain required. Stopping does not establish progress for unrelated messages
or count unrecorded crashes.

A separately registered typed recovery operation conditionally changes only an unprocessed,
dead-lettered delivery at the observed receipt generation and revision. It preserves the entire envelope and lifetime
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

IInboxDeadLetters supplies bounded metadata list/detail and conditional retry.
DeadLetter/DeadLetteredAt are public lifecycle names; the native quarantined_at column remains
compatible with existing migrations. Live keyset paging uses database timestamp/UUID order,
grants no authority and reserves no work. Consumer auth/RLS, repair and restart remain explicit.
Forward migrations add the lookup index.
