# D2.1: required local domain reactions

Current review entry: [EventReactions consolidation](../reports/event-reactions-consolidation.md)
supersedes the earlier package names, aggregate base and marker requirements. Descriptions and
test counts below retain their dated scope; source links lead to current replacements.

Date: 2026-10-14. Owner-authorized continuation after D1.1 checkpoint `f55d803`.
Implements the local increment in [the D2 scope](d2-domain-events-and-required-reactions.md).
All mechanisms start as sample-owned code; no library interface is selected.

## Selected behavior

Sales draft submission accepts the captured draft terms and makes the draft immutable.
The accepted domain candidate carries a private `DraftSubmitted` fact. An explicit adapter
registers that fact with the owning Sales context; hydration and rejected decisions do not.
A required handler loads a separate customer order allowance and invokes its reserve behavior.
The allowance has one currency, a trusted-caller-supplied positive ceiling, reserved amount and
native version. A submission cannot exceed the remaining allowance. This is a bounded sample
policy, not customer credit underwriting, payment, production authorization or stock reservation.

One native Sales transaction contains submission and allowance reservation. Separate order
roots still compete on the allowance version. No network publication occurs before commit.
Opening an allowance is explicit sample setup; existing D1.1 draft creation/editing is unchanged.

## Save and event protocol

SaveChanges dispatches the registered facts before existing tracked-write guards and the native
save. Both sync and async families have native I/O paths, without sync-over-async. An explicit
callable staging method exercises the same handler before saving for comparison. Default save
dispatch prevents a caller from forgetting that staging call.

Use a bounded single batch, in registration order. Further registrations during handling and
nested saves are rejected rather than drained recursively. Handlers only stage tracked writes.
Facts are consumed after successful native save, not after staging and not on commit. Failed
handling/saving/cancellation poisons the participating context: roll back/dispose and re-decide
in a fresh scope. A successful save still requires caller commit. A context that handles facts
is bound to that native transaction and must be discarded after commit/rollback.

Multiple saves in one transaction handle newly accepted facts without repeating prior effects.
After SaveChanges(false), explicitly AcceptAllChanges before further work. A repeated save
without acceptance is rejected. No historical-read, rejected-decision or row-diff dispatch.
Tracked submission without its registered fact is rejected. Bulk SQL/provider commands bypass
this protocol; they cannot stand in for the supported aggregate commands.

## Required evidence and handoff

Finite executable usage plus real PostgreSQL proofs cover both save overload families, explicit
versus automatic staging, multiple saves, false acceptance, no-event saves, required handler/save/
commit failures, cancellation, reentrancy, bounded handling, ownership, and competing submissions
on different roots. Verify handler-staged changes pass the existing guards. Preserve D1.1 proofs
and migration history; document direct-write counterexamples without claiming universal coverage.

Report new versus inherited evidence, sample policy versus extraction candidates, and library/
template findings. D2.2 durable deferred work is separate. No generic dispatcher, mediator,
aggregate base, event store or automatic integration-message mapping is selected.
