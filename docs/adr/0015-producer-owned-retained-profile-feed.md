# ADR 0015: Retained profile-feed experiment rejected for adoption

Date: 2026-10-13. Disposition: rejected for adoption on 2026-10-14.
The owner closed B1 as an assessment and requested removal of the implementation, tests,
snapshot and reconstruction patch. This ADR records the trade-off and rejection;
it establishes no active Sales persistence requirement or Rootbolt interface.

## Context

The experiment tested a producer-owned retained pull feed over selected state-stored
Sales profile changes. Timestamps and allocated sequence positions alone could not
provide safe ongoing progress when transactions committed out of order.

## Investigated choice

A per-Organization feed-state lock, held through the business transaction, ordered
post-lock allocations and updates to a committed head. Short consistent-snapshot pages
and atomic prefix deletion/retention-floor advancement provided paging and expiry.
A diagnostic receiver committed receipts and cursor progress together.

The protocol passed the [dated PostgreSQL/process proofs](../reports/b1-replayable-integration-feed.md)
for late commits, gaps, duplicates, rollback, retention and restart. Those results apply
to the discarded implementation and its complete-writer-participation assumptions.

## Rejection and consequences

The experiment did not establish a concrete receiver need or business value for its
Organization-wide writer serialization. Its journal was diagnostic rather than a useful
business projection. Initial state population and snapshot/live cutover were not implemented.

The owner chose to retain [findings and constraints](../reports/b1-retention-and-contract-assessment.md)
only. All experiment source, tests, migration, snapshot and patch artifacts are removed.
Common Sales writes, Contracts, mapping and setup have no feed dependency.
No library mechanism, template preset or universal bootstrap approach was adopted.

A later slice may revisit retention or state transfer when a concrete receiver requirement
justifies it. That requires a new reviewed implementation/proof scope rather than restoring
this rejected design by default.
