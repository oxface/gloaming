# ADR 0022: Complete-state population for Dispatch's delivery directory

Date: 2026-10-22. Status: accepted under the owner's B2 scope approval; implementation
review corrections are prepared for exact change-set review.

Dispatch needs the complete customer delivery directory for local search while Sales is
unavailable. [B2](../plans/b2-dispatch-directory-population.md) selects a bounded public
current-state export plus overlapping durable whole-state updates. Targeted hydration cannot
discover unknown customers; retained broker state would add a state store and publication
bridge without demonstrated need.

Sales commits its existing profile version as the public revision for this particular
complete-state contract, including withdrawal and relisting. Each supported live register,
profile edit and listing change publishes the resulting replacement state in the same native
transaction. This revision promise belongs to Sales, rather than following automatically from
aggregate versions, timestamps or transport positions. Customer identity, revision and the
`Listed` wire discriminator must be explicit; missing `Listed` must never imply withdrawal.

Withdrawals omit public text but retain permanent source rows and receiver revision fences.
Physical deletion, tombstone cleanup and CustomerId reuse are unsupported: without a retained
fence, a delayed older page or update could resurrect withdrawn state. Relisting advances the
same customer's public revision. Historical seed is finite setup before serving/population;
all live writers must participate in publication, so old binaries and bypass SQL must stop
before rollout.

Dispatch persists Organization admission and binds durable capture before its first export
read. Updates rejected before admission remain pending in the source outbox. Once admitted,
the receiver durably accepts deliveries before source completion. Copy pages and ongoing
updates share a private merge: newer complete state wins, older state is ignored, and unequal
state at equal revisions fails. Page effects/progress and handler effects/receipt completion
each commit in their owning receiver transaction. Source reads occur outside receiver locks.

Readiness means the baseline traversal completed with overlapping durable capture. Pages read
coherent values/revision per key but do not form one coherent source snapshot. New keys behind
the cursor and late commits converge through eventual delivery, potentially after readiness.
Readiness therefore promises neither a source-head barrier, an empty backlog nor synchronous
freshness. These limits are visible in the [consumer guide](../../samples/Wholesale/modules/Dispatch/README.md).

This decision does not revive [B1's rejected retained feed](0015-producer-owned-retained-profile-feed.md),
Organization-wide source writer gate or diagnostic journal. It introduces no generic replication
abstraction or new Gloaming mechanism. Public revision meaning, disclosure, permanent key
lifecycle, admission, conflicts and readiness stay consumer-owned. The [B2 report](../reports/b2-dispatch-directory-population.md)
records the executable PostgreSQL evidence and remaining product limits.
