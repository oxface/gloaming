# B1: Sales-owned retained integration feed

Date: 2026-10-13. Status: implementation and dated verification of the originally approved
consumer experiment on `feat/replayable-integration-feed`; no commit or push.
On 2026-10-14 the owner closed B1 as the
[retention and contract assessment](b1-retention-and-contract-assessment.md), with no further
implementation. Experiment adoption is deferred to a later concrete receiver need.
The owner has staged the implementation as a review snapshot;
subsequent documentation edits remain unstaged. The original verification and initial-index
record below describe the 2026-10-13 handoff, not the current staging state.
The owner subsequently requested removing the experiment from the common Wholesale sample.
Its [source, tests and reconstruction patch](../../experiments/b1-retained-profile-feed/README.md)
are preserved outside the active solution/CI. The outcome, commands and runtime results below
describe the original experiment. Current Sales has been restored to the merged baseline.

## Outcome

The experiment exposed retained V1 customer-profile edits through ICustomerProfileFeed in
Sales.Contracts. The existing state-stored profile operation includes its immutable change
and committed feed head alongside customer/address/audit in one native transaction. A
per-Organization feed-state row lock, acquired before business loads and held through final
commit, orders sequence allocations performed after lock acquisition. Rollback gaps remain
valid; outbox timestamps/order and private domain events are not progress positions.

One short read-only RepeatableRead snapshot captures generation, retention floor, committed
head and bounded ordered entries. Truncated pages advance only through returned work;
exhausted pages advance to their captured head. Explicit prefix retention commits deletion
and its floor together. C=F is valid; C<F expires; another generation/future position is
invalid. Readers stop on expiry without resetting their persisted subscription.

[Executable usage and current limits](../../experiments/b1-retained-profile-feed/README.md)
include finite setup/write/subscribe/read/prune commands. The reader owns a separate
PostgreSQL diagnostic receipt/checkpoint journal. Equal pages have no second diagnostic
effect; conflicting content fails, competing checkpoints use native version predicates,
and stale pages cannot skip suffixes or regress progress. Actual child-process death before
save, after save before commit, and immediately after commit demonstrates restart.
This does not build a serving customer view or export existing customer state.

The owner approved the [concrete contract/protocol/file scope](../plans/b1-replayable-integration-feed.md)
with “proceed with implementation.” [ADR 0015](../adr/0015-producer-owned-retained-profile-feed.md)
records the persisted consumer choices. WF2 is already merged at the fetched starting base
`d674e39a95f4aec8fa617b3382d423dc748bf166`; stale current roadmap descriptions are reconciled.

## Fresh verification

Executed against the active implementation on 2026-10-13, separately from archived/family
reports. PostgreSQL uses the existing Testcontainers 18.6 fixture, per-case disposable databases,
rootless Podman and real connections. Process tests launch and kill only their own built
IntegrationFeedDemo children. Barriers observe PostgreSQL lock waits or explicit executed
transaction/query boundaries; sleeps alone do not establish ordering.

| Check | Result |
| --- | --- |
| Whole active solution restore/build | Passed, zero warnings/errors. |
| IntegrationFeedDemo.Tests | 23 passed, zero failed/skipped. |
| HttpIdentityDemo.Tests regression | 105 passed, zero failed/skipped. |
| ArchitectureTests | 83 passed, zero failed/skipped, including feed-host dependency boundary. |
| Whole-solution semantic style and analyzers | Passed, verify-no-changes. |

Commands used from the repository root:

```bash
DOTNET_PROCESSOR_COUNT=4 dotnet build ModulithFoundry.slnx -m:1
# For PostgreSQL suites, also set DOCKER_HOST and
# TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE to the local Podman socket.
dotnet test --project samples/Wholesale/IntegrationFeedDemo.Tests/IntegrationFeedDemo.Tests.csproj --no-build --no-restore
dotnet test --project samples/Wholesale/HttpIdentityDemo.Tests/HttpIdentityDemo.Tests.csproj --no-build --no-restore
dotnet test --project tests/ArchitectureTests/ArchitectureTests.csproj --no-build --no-restore
dotnet format style ModulithFoundry.slnx --verify-no-changes --no-restore
dotnet format analyzers ModulithFoundry.slnx --verify-no-changes --no-restore
```

The new source proofs establish:

- Rejected naive sequence/MAX paging: B commits its higher allocated position before A;
  advancing MAX then committing A loses A from the next suffix.
- Supported late commit: A blocks after allocation; a different customer's same-Organization
  writer B waits at the feed gate. Readers remain at the previous committed head until A
  resolves, and subsequently see both independent expected changes in order.
- An earlier-started transaction that acquires the gate later receives a later position and
  remains visible after the receiver's saved cursor.
- Rollback/other-Organization gaps, one-row paging, new writes between pages, empty pages,
  cursor-in-gap validity, bounded head progress and tenant isolation.
- Retained insert, head update, address/audit save and deferred commit faults roll back
  profile/address/audit/entry/head together. Cancellation after first save/allocation does
  the same; a fresh accepted edit recovers with a legitimate gap.
- Retention through real positions and gaps, boundary validity, expiry, invalid future/source
  cursors, idempotent/older cutoffs, all-rows-pruned pages and deletion/floor rollback.
- A reader paused between bounds and entries finishes a complete old page while pruning
  commits; the next read observes the new floor and expires.
- Repeated source/journal pages, stale expected-version producer retry, content conflicts,
  failed receipt/checkpoint commit, competing readers, and stale out-of-order page completion.
- Actual reader death before save and after save/before commit leaves neither receipts nor
  checkpoint; death immediately after commit preserves both. Fresh processes resume and
  produce one retained diagnostic effect per change. An expired reader exits without
  advancing/replacing its saved cursor.
- A new forward migration over populated prior Sales storage preserves native migration
  history, profiles, accepted audit, a real WF2 process and its outgoing request. Activation
  produces no historical feed entries and repeated finite setup preserves progress.

Existing HTTP profile concurrency/fault/admission checks still pass. The concurrency barrier
now observes one blocked business UPDATE and one competing feed-gate wait, reflecting the
reviewed serialization instead of waiting for two business UPDATEs. Its winner/conflict,
committed version and profile assertions are retained. No production assertion or failure
classification was disabled. CI wires B1 into Wholesale consumer composition and includes
the reused child-host helper in that lane's inputs.

## Review-worthy files

- [Sales public feed types](../../experiments/b1-retained-profile-feed/snapshot/samples/Wholesale/modules/Sales/Sales.Contracts/CustomerProfileFeed.cs)
  and [interface](../../experiments/b1-retained-profile-feed/snapshot/samples/Wholesale/modules/Sales/Sales.Contracts/ICustomerProfileFeed.cs):
  source-owned cursor, detached V1 payload and explicit results, with no technical dependencies.
- [Native storage/gate/append](../../experiments/b1-retained-profile-feed/snapshot/samples/Wholesale/modules/Sales/Sales/ProfileFeed/FeedStorage.cs),
  [snapshot paging](../../experiments/b1-retained-profile-feed/snapshot/samples/Wholesale/modules/Sales/Sales/ProfileFeed/CustomerProfileFeed.cs),
  [activation/retention](../../experiments/b1-retained-profile-feed/snapshot/samples/Wholesale/modules/Sales/Sales/CustomerProfileFeedSetup.cs)
  and [profile participant](../../experiments/b1-retained-profile-feed/snapshot/samples/Wholesale/modules/Sales/Sales/CustomerProfiles.cs).
- [New forward migration](../../experiments/b1-retained-profile-feed/snapshot/samples/Wholesale/modules/Sales/Sales/Migrations/20261013150330_AddCustomerProfileFeed.cs)
  and current snapshot: add only Sales feed state/change/sequence storage. Older migration
  source and fixtures remain untouched. Both native save overrides reject tracked feed writes;
  explicit module SQL owns the supported storage protocol.
- [Diagnostic journal](../../experiments/b1-retained-profile-feed/snapshot/samples/Wholesale/IntegrationFeedDemo/FeedJournal.cs),
  [finite executable](../../experiments/b1-retained-profile-feed/snapshot/samples/Wholesale/IntegrationFeedDemo/Program.cs) and
  [PostgreSQL/process proofs](../../experiments/b1-retained-profile-feed/snapshot/samples/Wholesale/IntegrationFeedDemo.Tests/FeedTests.cs).
  DemoSetup/proof setup explicitly activate selected Organizations, with no API-startup writes.
- Solution/architecture/CI changes, current module/demo docs, scope/roadmap/extraction updates
  and ADR 0015 accompany the consumer capability; technical runtime library source is unchanged.

## Library, template and sample findings

No new reusable mechanism was proven. The concrete source progress protocol is demonstrated
in one consumer; it has no independently adopted library interface. Gate/committed-head
allocation and snapshot page/floor checks remain extraction candidates if later reuse or
meaningful shared complexity earns an exact reviewed adapter. No new Rootbolt package or
interface is introduced and no second producer is invented to justify extraction.

Sales owns payload meaning/disclosure, actor/tenant admission, activation, complete writer
participation, expected business versions and retention cutoff. The receiver owns subscription
identity, receipt/content conflicts, atomic checkpoints and serving policy. These obligations
are not folded into existing messaging/event libraries. No template preset was added; the
executable README is an exercised editable composition recipe.

## Limits and preserved review state

The gate serializes accepted edits within an Organization; throughput and other providers
are unproven. Native SQL bypasses/mixed old writers can break completeness; drain old writers
before activation. B1 cannot distinguish arbitrary deleted rows above F from legitimate
allocation gaps. It has no subscriber pinning, automatic retry, sequence reset/generation
migration, payload upgrade path, public export authorization or external exactly-once effects.

B2 snapshot/initial population/serving cutover/expiry recovery, deletion export, multi-source
order, production capacity and template/library extraction remain outside this slice.
No Aspire/browser deployment, broker or unrelated family suites were rerun here; historical
results are not fresh B1 evidence. The affected HTTP suite and active whole-solution checks
cover this consumer composition's existing behavior.

The initial index digest is
`dc3e7e2c907cc2a220bd2ca07c30b890addfe5215d034a2a37145c0712ebfa69`.
Final integrity checks confirm it remains unchanged and the cached diff stays empty.
All 800 frozen archive files and earlier migration sources remain intact; all 342 local
links across changed documentation resolve, and changed C#/project files pass CSharpier.
All new edits remain in the working
tree. Exact commit/push approval has not been requested or inferred.
