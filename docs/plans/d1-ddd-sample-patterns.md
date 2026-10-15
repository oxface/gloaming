# D1: state-stored and event-sourced domain decisions

Date: 2026-10-14. Scope authorized by the owner after the D1 recommendation.
Implementation and evidence remain subject to owner review; no library promotion is selected.
[Implementation and fresh verification](../reports/d1-ddd-sample-patterns.md) are now prepared
for that review, with [executable consumer usage](../../samples/Wholesale/DddDemo/README.md).

## One bounded outcome

Use Sales customer profiles and Purchasing purchase orders to demonstrate creation, domain
values, accepted changes and rejection without partial accepted state. Sales owns its one-address
profile and text limits; Purchasing owns positive quantities, nonnegative prices, line replacement
and a representable final total. These are existing sample rules, not new product policy.

Introduce an internal immutable Sales profile aggregate and a validated profile-details value.
Finite Sales setup creates this aggregate before tracking either row. Normal profile edits load
the existing state, calculate a complete candidate and copy it into native EF rows only after
validation. Preserve expected-version concurrency, trusted-human admission, audit and the
existing service-owned transaction. Purchasing continues using its existing EventSourcedAggregate
and caller-owned save/commit. No new aggregate base or domain-event retention is proposed.

Provide a finite DddDemo executable using both modules through their existing Contracts, native
contexts and setup helpers. Each run requires a fresh disposable database. Demonstrate Sales
creation through finite seed setup, accepted/rejected profile edits and Purchasing draft/line
changes, including a rejected overflowing total. This is not a production customer-creation API.

## Proof and review boundary

- Pure domain proofs cover value equality and validation, immutable Sales candidates, creation,
  version overflow, and Purchasing rejected batches preserving accepted state/pending facts.
- PostgreSQL consumer proofs execute the sample, reject invalid Sales input before tracking,
  retain a committed winner after stale edits, reject invalid Purchasing candidates before
  tracking, and preserve native schema/migration setup.
- Rerun the existing HTTP profile suite for concurrent updates, save/commit faults, cancellation,
  audit and admission; rerun native event consumer and architecture checks.
- Keep library code, module Contracts, migrations, archived fixtures and T1 template unchanged.
  Internal domain types are visible only to the dedicated proof assembly for direct tests.

The report will distinguish newly executed proofs from prior evidence and compare reusable
bookkeeping with consumer-owned validation/persistence. Editable sample patterns are an acceptable
outcome. A new utility requires meaningful repetition and a separately reviewed interface before
promotion; no package is promised by this assessment.

## Owner-review follow-ups — 2026-10-14

The owner agreed [D1.1](d1-1-rich-state-stored-aggregate.md) for a state-stored root with
relational children and [D2](d2-domain-events-and-required-reactions.md) for domain facts and
required reactions, including assessment of dispatch inside SaveChanges. These are separate
future increments. Current D1 establishes the small customer/address boundary and Purchasing
line decisions; it does not settle rich collection persistence or domain-event handling.
