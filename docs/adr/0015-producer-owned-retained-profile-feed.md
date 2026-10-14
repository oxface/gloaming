# ADR 0015: Producer-owned retained profile-feed progress

Date: 2026-10-13. Status: experimental source protocol; adoption deferred on 2026-10-14
when the owner closed B1 as an assessment. Implementation remains uncommitted for code review.
The decision below records the implemented experiment, not a settled requirement to add
this feed or its writer gate to normal Sales usage. See the
[retention and contract assessment](../reports/b1-retention-and-contract-assessment.md).
No Rootbolt library interface or B2 snapshot protocol is established.
At the owner's request the experiment is now
[preserved outside the common sample](../../experiments/b1-retained-profile-feed/README.md).
The choices below apply only to that snapshot; current Sales has no feed mapping, gate or API.

## Context

Sales profile edits are state stored and already commit customer/address/audit atomically.
Selective outbox messages and per-stream event history provide no complete public change
feed. A sequence allocated before commit can commit out of order; advancing to MAX skips
late lower positions. Timestamps do not resolve that problem.

## Decision

Keep V1 profile changes and feed state in Sales-owned storage, separate from outbox delivery
lifecycle. Each admitted Organization has a persistent generation, committed head H and
retention floor F. Native profile transactions acquire the feed-state row lock before
business loads/changes, allocate from a PostgreSQL sequence only after acquisition and hold
the lock through final commit. The entry and H share that business/audit transaction.

Read bounded ordered suffixes in one short read-only RepeatableRead snapshot of F/H/entries.
Truncated pages advance only through returned work; exhausted pages advance to captured H.
Allow sequence gaps, including rollback and other Organizations' allocations. Treat F as
the last discarded boundary. Explicit locked retention commits prefix deletion and F together;
C=F is valid, C<F expires, and another generation/future head is invalid. No silent recovery.

Public module Contracts expose stable business DTOs/cursors/results independently of EF
and technical library types. Consumer-native receipt/checkpoint transactions demonstrate
repeated pages and restart. Initial snapshot/population and cutover remain B2.

## Consequences

Accepted profile edits serialize within one Organization; this trade-off bounds the first
protocol but provides no throughput claim. Activation must drain old writers; raw SQL or
mixed deployments can bypass completeness. Tracked feed mutations are rejected, while
explicit module SQL owns append/activation/retention. Database grants remain consumer policy.

Readers hold no snapshot across requests. Pruning can expire a slower subscriber between
pages, even after it read some history. B1 reports that loss and stops; it supplies no
subscriber pinning, deletion export, bootstrap recovery or global multi-source order.
No new reusable library mechanism is extracted. Exact interfaces and independent adoption
require further evidence and owner review before promotion.

See [preserved experiment setup](../../experiments/b1-retained-profile-feed/README.md),
[reviewed scope](../plans/b1-replayable-integration-feed.md) and
[fresh evidence](../reports/b1-replayable-integration-feed.md).
