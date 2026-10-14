# B1: Sales-owned replayable customer-profile feed

Status: owner approved the concrete consumer implementation scope on 2026-10-13 with
“proceed with implementation.” Implemented and verified for code review; no Rootbolt
interface or package is introduced. [Fresh evidence](../reports/b1-replayable-integration-feed.md)
and [preserved experiment](../../experiments/b1-retained-profile-feed/README.md)
record the resulting behavior and limits. The proposal below retains the reviewed scope.
On 2026-10-14 the owner closed B1 as the
[documentation-only retention/contract assessment](../reports/b1-retention-and-contract-assessment.md),
without further implementation. This retained protocol is experimental evidence; adoption and
its writer gate remain deferred until a later slice establishes a real receiver requirement.
Existing code stays intact for review; closure does not approve its commit or merge.
The owner then requested excluding it from the common Wholesale sample. Source, tests and
the unmerged migration are preserved in the [experiment snapshot](../../experiments/b1-retained-profile-feed/README.md),
with an exact patch over the baseline below. The original scope/file map and code examples
that follow describe that historical experiment, not active sample setup.
Base: fetched `origin/main` at
`d674e39a95f4aec8fa617b3382d423dc748bf166`, containing merged WF2.

## One bounded outcome

Sales exposes retained, versioned customer-profile changes through its own Contracts.
A finite consumer reads bounded pages, durably records each change and resumes from its
saved cursor. Prove the producer's progress protocol on PostgreSQL, including late
commits, sequence gaps, repeated pages, rollback, retention and restart.

Use the existing state-stored Sales profile operation. This deliberately establishes
that a replay feed need not depend on domain-event storage. Snapshot export, initial
population of existing customers, snapshot/feed cutover and a serving read model belong
to B2. Reading a retained feed from its beginning does not establish a complete customer
catalog. There is no new deletion command, broker, background worker or projection engine.

## Reconciliation with active consumers

| Current evidence | Consequence for B1 |
| --- | --- |
| [CustomerProfiles.ChangeAsync](../../experiments/b1-retained-profile-feed/snapshot/samples/Wholesale/modules/Sales/Sales/CustomerProfiles.cs) checks an expected version, saves customer then address/audit and commits its own native transaction. | Add the retained change to that same transaction; preserve the two-save rollback behavior and existing result Contract. |
| [Sales rows](../../samples/Wholesale/modules/Sales/Sales/ProfileRows.cs) are ordinary state-stored customer/address entities; [seeding](../../samples/Wholesale/modules/Sales/Sales/SalesDemoSeed.cs) stages initial demonstration data. | Feed accepted profile edits after activation. Seeds and pre-existing rows are outside the change history; no inferred historical export. |
| [Inventory commands](../../samples/Wholesale/modules/Inventory/Inventory/StockPositions/StockPositionCommands.cs) explicitly enqueue stock issues, while opening/receipt commands do not enqueue a complete state feed. Reservation messages have additional consumer-selected semantics. | Retained Inventory outbox rows cannot certify complete integration history. Do not export private aggregate facts as a public feed. |
| [Outbox dispatch](../../src/Rootbolt.Messaging/Rootbolt.Messaging.EntityFrameworkCore.Postgres/PostgresOutboxDispatcher.cs) claims available work with SKIP LOCKED and orders by availability/time/ID. [Outbox records](../../src/Rootbolt.Messaging/Rootbolt.Messaging.EntityFrameworkCore/OutboxMessageRecord.cs) contain delivery lifecycle, without a replay position. | Dispatch order, timestamps and delivery retention supply no safe replay cursor. Leave outbox schema and dispatch unchanged. |
| [Current event-store capabilities](../../src/Rootbolt.EventSourcing/docs/capabilities.md) describe bounded per-stream history and effect-free full rebuilding, with optimistic header stamps. | No global feed, pre-read gate or snapshot protocol can be inherited from ES2. Older extraction-plan gate/secondary-view proposals remain historical. |
| [Current workflows](../../src/Rootbolt.Messaging/docs/workflows.md) execute the saved continuation through provided process storage. WF2 is merged. | Replace stale current-status references to the uncommitted sequential experiment; preserve its dated reports as history. B1 does not extend workflows. |
| [Persistence](../../src/Rootbolt.Persistence/docs/capabilities.md) protects selected tracked tenant writes; [audit](../../src/Rootbolt.Auditing/docs/transactional-audit.md) excludes name/address content and has no export service. | Feed admission, data disclosure and retained public DTO selection stay Sales/host policy. Neither utility provides feed completeness or privileged retention. |

## Producer-owned public Contract for review

Place these proposed types in `Sales.Contracts`, which stays independent of EF, Npgsql,
HTTP and Rootbolt types. A feed call uses the already admitted, fixed Organization context;
it does not accept an Organization from a request body. Generation is a persistent UUID
unique to that Organization's profile-feed incarnation. It binds a cursor to this source;
it is not a credential. Changing organizations requires a new operation scope and cursor.

```csharp
public sealed record CustomerProfileFeedCursor(Guid Generation, long AfterPosition);

public sealed record CustomerProfileFeedBounds(
    CustomerProfileFeedCursor EarliestAvailable,
    CustomerProfileFeedCursor Head);

// V1 fields and meaning are selected by Sales, independently of private persistence rows.
public sealed record CustomerProfileChangedV1(
    Guid CustomerId, Guid AddressId, string Code,
    string DisplayName, string AddressLine, long Version);

public sealed record CustomerProfileFeedChange(
    long Position, Guid ChangeId, CustomerProfileChangedV1 Profile);

public abstract record CustomerProfileFeedRead
{
    public sealed record Page(
        CustomerProfileFeedBounds Bounds,
        IReadOnlyList<CustomerProfileFeedChange> Changes,
        CustomerProfileFeedCursor Next,
        bool HasMore) : CustomerProfileFeedRead;

    public sealed record CursorExpired(
        CustomerProfileFeedBounds Bounds) : CustomerProfileFeedRead;

    public sealed record InvalidCursor : CustomerProfileFeedRead;
}

public interface ICustomerProfileFeed
{
    Task<CustomerProfileFeedBounds> DescribeAsync(CancellationToken cancellationToken);
    Task<CustomerProfileFeedRead> ReadAsync(
        CustomerProfileFeedCursor after, int pageSize, CancellationToken cancellationToken);
}
```

Detached immutable results expose no query provider or JSON lifetime. V1 is the explicit
payload schema, not a CLR-name-derived identity. Bound `pageSize` to 1–100; existing field
length limits also bound row size. Malformed IDs, negative positions and invalid page sizes
throw native argument exceptions. Wrong generation or a position above the captured head
returns InvalidCursor without changes or a replacement position. A valid generation with
`AfterPosition < floor` returns CursorExpired. Database faults and cancellation propagate.
Missing feed activation is a configuration error, not an empty feed.

Describe returns the earliest still-supported cursor and current committed head. Starting
at EarliestAvailable explicitly opts into retained history only. Existing subscribers
must never replace a saved expired cursor with that value automatically. Generation changes
invalidate the old cursor; regeneration/reset is outside B1. A position in a legitimate
sequence gap is valid when it lies between floor and head.

## Native PostgreSQL producer protocol

Add consumer-owned Sales storage through one new forward migration:

- `sales.customer_profile_feed_state`: Organization PK, unique generation, committed head H,
  floor F, with `0 <= F <= H`. Explicit finite setup activates each selected Organization
  at H=F=0 with a new generation. Runtime reads never create/reset feed state.
- `sales.customer_profile_changes`: `(organization_key, position)` PK, per-Organization
  unique ChangeId, positive position and profile version, explicit V1 payload JSONB and
  Organization FK to feed state. No FK to mutable customer/address rows. Prefix retention
  must not alter business data, audit or message lifecycle. Entries are immutable.
- `sales.customer_profile_feed_positions`: bigint PostgreSQL sequence, NO CYCLE. Its
  allocation is shared across Organizations; per-feed positions may therefore have gaps.
  Sequence allocation alone never establishes a committed high-water mark.

For every accepted ChangeAsync operation:

1. Preserve existing input, tenant and Human-actor checks; begin the native ReadCommitted
   transaction already owned by the profile operation.
2. Lock this Organization's feed-state row with parameterized SELECT FOR UPDATE **before
   loading/mutating business rows**. All participating profile writers use this lock order.
   Fail if activation is missing. Hold the lock through the final native commit/rollback.
3. Perform the existing expected-version decision and first customer save. A rejected or
   conflicting edit creates no feed entry. Construct the public V1 candidate from the
   accepted customer and address values, validating/encoding before staging feed data.
4. In a separate statement **after the lock has been acquired**, allocate nextval. Stage
   exactly one immutable change with stable ChangeId; update H to that position in this
   same transaction. Stage address and existing audit, then perform the second save.
5. Commit explicitly as today. Any participant fault/cancellation rolls back the customer,
   address, audit, retained change and H. A consumed sequence value can remain a gap.

Updating the head is transactional; allocating a sequence is not. Holding one writer lock
until commit prevents another writer for the same feed from committing a higher position
while a lower one is unresolved. A transaction that began early but acquires the gate later
gets a later position. Neither commit timestamps nor waiting for gaps by age is required.
The PostgreSQL proofs now establish this protocol for the supported consumer; see the
dated report for results, independently of the primary-source design basis.

The lock serializes accepted edits within an Organization and can reduce throughput. Other
Organizations have separate gates. No global multi-tenant ordering is promised. Every
production writer for this feed must follow the protocol; raw SQL/bypassed writers and
mixed old/new application deployments cannot inherit completeness. Activation requires
draining old profile writers before enabling the new writer path. Preserve ordinary seeds
as setup data, without rewriting them into historical changes.

Native command timeout/cancellation bounds waits. Roll back and dispose the whole context
after failure; no nested/ambient transaction, implicit retry or context reuse is introduced.
Existing expected-version concurrency remains necessary. An ambiguous producer commit is
reconciled by a fresh profile/version read; a retry with the same expected version must not
append a second accepted change. This does not add a generic business-command deduplicator.

## Safe bounded pages and retention

ReadAsync owns a short, read-only RepeatableRead transaction on a fresh Sales context,
without pending writes or an existing transaction. Materialize F, H, generation and the
ordered entries from **one snapshot**, then dispose it before returning the page. No locks
or database snapshots persist across calls. No offset paging or private-row export.

After validating cursor C, select positions `C < position <= H`, ascending, limit
`pageSize + 1`. Return at most pageSize rows. If a look-ahead row exists, Next is the last
returned position and HasMore=true. Otherwise Next=H and HasMore=false, including an empty
page. Bounds always describes this call's snapshot. Consumers checkpoint Next, never H
from a truncated page. Later calls can capture a newer H. HasMore=false means caught up
through this page's H, not that no future change can occur.

Gaps do not require fabricated entries or contiguous-range validation from Events.History.
Their safety derives from the producer gate and transactional H. Arbitrary deletion above
F is corruption outside the protocol; an integer hole cannot distinguish that from a
rolled-back allocation. Do not claim automatic tamper/gap detection.

Provide an explicit Sales maintenance operation, callable by finite setup/proofs, that
accepts an admitted generation/position cutoff K with `0 <= K <= H`. It owns a fresh
ReadCommitted transaction, locks the same feed-state row, deletes entries through K,
sets F=max(F,K), and commits both together. Repeating a cutoff is idempotent. K may be in
a gap; F is the last discarded boundary, not necessarily a retained entry's predecessor.
F never moves backward. Reject a wrong generation or future cutoff. Failures roll back
both deletion and F; do not install a time-based retention worker or subscriber registry.

C=F is valid; C<F expires, including a genesis cursor after pruning. A read begun before
pruning can return its old complete page using MVCC. A later read sees the new floor and
expires if necessary. Retention does not pin subscribers or silently skip lost history.
An expired subscriber stops and reports that reconciliation is required; B2 will own the
eventual snapshot/bootstrap recovery protocol. Bounds does not supply that protocol.

## Executable usage after owner review

Add `samples/Wholesale/IntegrationFeedDemo` with finite explicit setup/write/read/prune
commands. Use native Sales migrations and selected tenant/actor scopes. The host admits
its fixed demonstration Organizations; no public unauthenticated feed endpoint is added.
The reader calls only ICustomerProfileFeed and references no internal Sales rows. The
finite consumer owns a separate diagnostic receipt database/context; it copies no current
customer table and exposes no serving customer projection.

```csharp
// subscription is already configured with this producer/Organization/generation.
var read = await feed.ReadAsync(subscription.Cursor, pageSize: 2, token);
if (read is CustomerProfileFeedRead.Page page)
{
    // Consumer-owned native transaction: equal retained identities are no-ops;
    // changed content under the same identity fails; receipts and Next commit together.
    await journal.RecordAndCheckpointAsync(page, token);
}
// CursorExpired stops this subscription; it never silently starts at the floor.
```

Receipt identity is `(subscription, generation, ChangeId)`; position/content are validated
against retained receipts. Native checkpoint concurrency fences competing reader instances.
Reject stale out-of-order page completion so the checkpoint cannot regress. Applying a
duplicate page has no second local effect. Restart reopens the journal and resumes only
from committed Next. This diagnostic checkpoint is B1 evidence, not B2's snapshot import,
live-view publication/cutover or exactly-once external side effects.

## Required PostgreSQL proofs

Use PostgreSQL 18.6 through the existing shared Testcontainers fixture, separate databases
per proof and real connections. Synchronize races with barriers/observed lock waits and
bounded deadlines; a sleep alone is not evidence of the interleaving. SQL may arrange
faults/barriers; assertions read the public source Contract and consumer journal.

| Proof | Required independent observation |
| --- | --- |
| Unsafe baseline | Allocate n in A and n+1 in B using a naive sequence feed; commit B first, advance MAX cursor, then commit A. Observe the missed A row. This rejects the naive design, separately from the supported path. |
| Late allocated commit | Hold A after allocation and before commit. B attempts another customer edit in the same Organization and is observed blocked at the gate. Reads cannot advance past A; after release both changes appear in safe order. |
| Early-start transaction | A starts before B but obtains the gate after B commits. A receives the later position and remains visible after the reader's prior checkpoint. |
| Gaps | Roll back after allocation, then accept another edit; also allocate for another Organization. Read with page size one and a cursor within the gap. Assert both expected accepted changes, no phantom changes and correct final Next. |
| Bounded progress | More than one page, equal business values/times, new writes between calls, empty pages and two Organizations. Assert no cross-tenant leakage or skipping the truncated suffix, ordered results and captured-head bounds. |
| Producer rollback | Fail retained insertion, audit/address second save, final transaction or cancellation after first save. Neither business changes, entry nor H commits; a fresh accepted retry is visible. |
| Duplicates/ambiguity | Re-read the same cursor, repeat a page after receipt commit, and retry an accepted profile edit with its old expected version. Assert stable ChangeId/content and one committed diagnostic effect per change. |
| Receiver rollback/restart | Interrupt after recording receipts but before checkpoint commit; reopen with a fresh process/context and re-read. Also kill the finite reader immediately after commit. Assert receipts/checkpoint atomicity and exact resumption without duplicate effects. |
| Competing readers | Race journal transactions for one subscription and finish an older page after a newer one. One wins natively; fresh retry cannot regress the checkpoint or duplicate effects. |
| Retention | Prune through a real position and a gap; C=F succeeds, C<F expires, future/wrong-generation cursors reject. Prove retention rollback, idempotent repeat and all-rows-pruned empty feed behavior. |
| Retention/read race | Pause between bounds and entries in a RepeatableRead page, commit pruning, then finish. Assert a complete old page; the following call sees the new floor. |
| Compatibility/isolation | Apply the new migration over existing Sales/customer/audit/WF2 data. Preserve previous migration histories and peer schemas, existing profile HTTP behavior and architecture Contract boundaries. |

## Exact implementation map and extraction gate

| Proposed files | Responsibility |
| --- | --- |
| `samples/Wholesale/modules/Sales/Sales.Contracts/ICustomerProfileFeed.cs`, `CustomerProfileFeed.cs` | Reviewed producer-owned DTO/cursor/page seam above. |
| `samples/Wholesale/modules/Sales/Sales/ProfileFeed/` | Internal state/change mappings, gate/append, snapshot paging, explicit activation and retention. Parameterized SQL; no new Rootbolt API. |
| Sales `CustomerProfiles.cs`, `SalesDbContext.cs`, `SalesRegistration.cs` | Atomic participant, feed mapping/immutability validation and explicit Contract registration; preserve native transaction ownership. |
| Sales new forward migration and current model snapshot | Add only feed state/change/sequence storage. Existing migrations remain byte-for-byte intact. |
| `samples/Wholesale/HttpIdentityDemo/DemoSetup.cs`, `.Tests/CompositionTests.cs` and affected profile proof setup | Explicitly activate Alpha/Beta feeds alongside finite Sales setup, without turning seeded profiles into changes or moving setup into API startup. Preserve pre-migration seed proofs. |
| `samples/Wholesale/IntegrationFeedDemo/` and `.Tests/` | Executable producer/reader/receipt usage and real PostgreSQL/process proofs above. |
| Active solution, architecture tests and CI lane/path maps | Wire projects and proofs into Wholesale consumer composition; include the new host boundary. Family regression lanes still follow existing Sales dependencies. |
| Module/demo READMEs, this plan, extraction plan, roadmap and B1 report | Supported versus deferred contract, review decisions and separately dated actual executions. |

Recommend implementing this protocol first in consumer-owned source after review. No new
Rootbolt package, interface or mandatory family dependency is selected. The concrete
source/journal proofs must establish value before extracting anything. Existing Sales
dependencies remain as they are; the protocol itself uses native EF/Npgsql and does not
require EventSourcing, a codec, outbox dispatch or workflow execution.

Activation is a Sales composition/setup utility: stage a new feed-state row in the setup
caller's explicit transaction, leaving its save/commit visible. Repeating activation must
preserve an existing generation/head/floor, never reset them. Existing-profile deployments
activate only after draining old writers. The new finite demo can activate an empty source
then seed ordinary business rows, still with no synthetic feed history. Historical migration
proofs that seed before feed tables exist continue to use the unchanged SalesDemoSeed helper.

Extraction candidates are the locked producer allocation/committed-head protocol and the
snapshot-bounded page/floor checks. Sales payload selection, admission, feed activation,
retention cutoff, business versioning and consumer receipts/checkpoint policy stay outside
technical libraries. One source does not establish a general export framework. If the
proofs demonstrate meaningful reusable complexity, return with exact public types,
dependencies, standalone adoption and line-by-line file map for owner review **before**
adding library interfaces. Otherwise retain editable sample mechanics and report that no
new reusable mechanism was proven. Do not add a second source just to justify extraction.

## Primary-source basis and pending decisions

PostgreSQL documents waiting/rechecking a concurrently updated row and stable RepeatableRead
snapshots in [transaction isolation](https://www.postgresql.org/docs/18/transaction-iso.html).
Its [locking documentation](https://www.postgresql.org/docs/18/explicit-locking.html) describes
transaction-lifetime row locks, and [sequence functions](https://www.postgresql.org/docs/18/functions-sequence.html)
explain allocations surviving rollback. Combining these into this feed protocol is our
proposed design; those references do not replace executable proofs.

The owner approved Sales as the source, the public Contract, serialized writer protocol,
page/floor semantics, explicit activation/retention and the sample-only first implementation
on 2026-10-13. The implementation and fresh PostgreSQL/process proofs are available for
code review. No new reusable library mechanism is extracted. B2 and any later library
extraction require their own scope/interface review; no commit or push is authorized.
