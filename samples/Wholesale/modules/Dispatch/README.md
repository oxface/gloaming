# Dispatch customer delivery directory

Dispatch owns a searchable local copy of Sales's public customer delivery state. It can search
names, customer codes and addresses while Sales is unavailable. Results are planning hints;
accepted shipping instructions must be captured as shipment facts by the future shipping flow.
This capability neither approves a shipment nor guarantees the current address at dispatch time.

The [Contracts](Dispatch.Contracts/ICustomerDirectory.cs) expose durable population progress,
page application, search and trusted Sales intake. Dispatch references Sales.Contracts, never
Sales implementation or its DbContext. Each module owns its own PostgreSQL database in the
[executable composition](../../Worker/Directory/README.md).

## Ownership and source contract

Sales's [public directory API](../Sales/Sales.Contracts/ICustomerDeliveryDirectory.cs) exports
complete replacement state in immutable CustomerId order, including withdrawals as tombstones.
The existing profile `Version` is explicitly the public directory revision for this view.
Every supported live creation, normal `ICustomerProfiles.ChangeAsync` edit, withdrawal and
relist advances/initializes that revision and commits its whole-state outbox message in the
same transaction. Rejected saves cannot commit the business state without publication.
This is not a generic aggregate revision convention. No timestamps or allocated message
sequences establish ordering or completeness.

Customer identity, revision and `Listed` are required JSON members. The relay and retained
inbox handler reject an omitted discriminator before it can become a withdrawal or advance
the permanent revision fence. Listed state requires all public text; withdrawn state omits it.
The hard-to-reverse protocol choices are recorded in [ADR 0022](../../../../docs/adr/0022-dispatch-directory-complete-state-population.md).

Names and addresses are deliberately disclosed to the admitted Dispatch module. The migration
lists existing profiles by default; withdrawing removes their public fields and search result,
while Sales keeps its private profile and permanent public revision fence. Relisting advances
the revision. Customer IDs and codes stay immutable; physical deletion and ID reuse are not
supported. All current live writers must use these APIs. Historical seed is finite setup only;
old binaries/native SQL bypasses are outside the contract and must stop before rollout.

## Population, overlap and readiness

1. Migrate both databases and bind the fixed `dispatch.customer-directory` relay to the intended
   receiver. Persist `BeginAsync` for the Organization before reading the first source page.
   Both Alpha and Beta are admitted by explicit sample policy. Other tenants are rejected.
2. Fetch a source page outside the receiver transaction. Its Organization must match the
   immutable operation tenant. The producer reads values and revision together in one SQL query.
3. Apply it with the expected population generation and cursor. Records, cursor, copied count
   and terminal readiness commit together. An old/repeated page submission fails visibly;
   after an ambiguous response read progress and resume, rather than blindly advancing.
4. Process overlapping changes through the durable inbox, independently of copy. Short native
   population-row locks serialize merges per Organization. Higher revisions replace state;
   lower revisions are ignored; unequal payloads at equal revisions fail. Tombstones keep
   their fences permanently. No network operation runs while holding these locks.
5. `SearchAsync` rejects incomplete populations. It always constrains Organization and searches
   the complete local population, returning at most 100 matching rows ordered by code.

The producer outbox exists independently of receiver admission. Before the receiver registers,
intake fails and producer work stays pending for retry. Once accepted, the receiver inbox owns
the durable delivery; the source may record publication completion. Its handler effect and
receipt completion share the existing Gloaming processor transaction.

Export pages form a **current per-key traversal**, not a single point-in-time snapshot.
Readiness means this baseline traversal completed while changes were durably captured. It is
not a source-head/freshness barrier. A new key below the cursor or a transaction committing
after its page is covered by the ongoing message, which may still be pending when readiness
becomes true. Unprocessed/delayed messages can leave results stale. `NoWork` is never used as
proof of caught-up readiness. A stale export response cannot overwrite a newer captured state.

The durable cursor survives process restart; each copy invocation uses a fresh operation and
reads saved progress. Permanent source rows/tombstones plus publication of every new key make
resumption safe even when source changes between pages. Rebuilding an entirely new receiver
starts with a fresh baseline and admission; routing to a replaced receiver must be coordinated
before resuming its relay. There is no in-place reset/receipt deletion API.

## Composition and limits

`AddDispatchDirectory(connection)` registers module persistence, public APIs, durable intake
and one inbox handler/processor. The host provides the immutable tenant accessor for operations.
`DirectoryComposition` demonstrates source/receiver wiring, a typed public-boundary relay and
bounded copy; it lives in the canonical product Worker, not a library dependency.
The relay binds Sales identity by trusted in-process composition. External transport would
need authenticated producer binding; payload tenant metadata itself grants no authority.

There is one fixed subscriber/route and no broker, subscriber feed, history replay, retention
expiry, tombstone cleanup, exact source-head checkpoint, source read replica, generalized
projector or external exactly-once effect guarantee. Source outbox/inbox rows and tombstones
are retained; capacity and cleanup policy remain future product decisions. The per-Organization
receiver lock bounds implementation complexity but has not been throughput-benchmarked.
Default identity/authorization hosting and independent workflows are not wired by this module.

Technical outbox/intake/receipt mechanisms reuse Gloaming. Key ordering, public revision meaning,
disclosure, listing lifecycle, merge conflicts, tenant admission, progress and readiness are
consumer-owned policies. The original B2 checkpoint proved no new reusable mechanism.
Canonical adoption subsequently proved a narrow outbox route claim selector; see the
[adoption report](../../../../docs/reports/b2-directory-product-adoption.md) and
[family contract](../../../../src/Gloaming.Messaging/docs/route-selection.md). See the
[B2 report](../../../../docs/reports/b2-dispatch-directory-population.md) for fresh proof evidence.
