# B2: populate Dispatch's customer delivery directory

Status: implementation selected under the owner's preapproved V1 continuation, 2026-10-22.
This is a bounded Wholesale capability, not revival of B1's rejected public feed.

Dispatch needs to search the complete set of customer names, codes and delivery addresses
while Sales is unavailable. An accepted shipment would retain its own agreed delivery facts;
this directory supports planning and discovery, not live credit/stock/eligibility decisions.
Sales owns disclosure and listing withdrawal; Dispatch owns storage, admission and readiness.

| Candidate | Fit for this operation | Decision |
| --- | --- | --- |
| Targeted hydration | Useful for an already known customer, but cannot search unknown customers across the whole population; cold keys need Sales. | Rejected for complete directory search. |
| Retained broker state/watch | Could supply initial state plus updates, but requires a maintained public state store, publication bridge and broker lifecycle not present here. | No new broker infrastructure justified. |
| Public export plus durable overlapping whole-state updates | Existing transactional outbox and inbox support local durable handoff; producer-owned revisions resolve overlapping state. | Selected. |

Implement Sales public whole-state Contracts and a bounded immutable-key export. Every supported
live register, profile edit, withdraw and relist transaction publishes to the existing outbox.
The existing profile version explicitly becomes this one Sales public directory revision;
this is a producer commitment, not an inference from aggregate/event/transport metadata.
A permanent source listing fence is exported even after withdrawal. UUIDs are never reused
or physically removed. Finite historical seed remains setup-only, before serving/population.

Dispatch persists admission before source reads, then walks the public pages with a durable
cursor. Each page's records and cursor/readiness commit together. Both pages and ongoing inbox
messages use one revision merge while holding a short native Organization population lock.
Newer complete state wins, equal conflicting state fails, and tombstones retain their revision.
Inbox processing owns the state/receipt transaction. No source I/O runs in that transaction.
A copy crash resumes its stored cursor; a read whose response was lost is safely reread.

The export is a current per-key coherent traversal, not a single database snapshot. Readiness
means the baseline traversal completed with durable overlapping capture. It does not mean a
source-head barrier, zero backlog, or synchronous freshness. Concurrent new keys behind the
cursor and transactions committing late converge through their durable messages. Dispatch may
serve its eventually updated directory while Sales is unavailable. Operator monitoring and
further freshness decisions remain product work.

Prove real PostgreSQL source and receiver separation, historical rows with no messages, actual
profile edits, creation behind the cursor, late commit, concurrent copy/update locks, permanent
deletion fences, duplicate/conflicting messages, durable restart progress, tenant admission,
copy/checkpoint rollback, inbox completion rollback and source-publication rollback. Use native
module persistence and existing Gloaming delivery rather than a custom replication engine.

The [consumer guide](../../samples/Wholesale/modules/Dispatch/README.md), temporary
[executable composition](../../samples/Wholesale/DirectoryDemo/README.md) and
[result report](../reports/b2-dispatch-directory-population.md) record implementation and proof.
