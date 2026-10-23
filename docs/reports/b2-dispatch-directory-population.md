# B2: Dispatch directory population and ongoing public state

Date: 2026-10-22. Outcome: a concrete Wholesale receiver now populates and maintains its
own searchable customer delivery directory through public Sales Contracts. This implements
the selected [bounded plan](https://github.com/oxface/modulith-foundry/blob/86012044cdaf248c90f65a05eb9dc64e8accfb07/docs/plans/b2-dispatch-directory-population.md); B1's discarded
feed, writer gate and diagnostic journal are not restored.

## Concrete value and selected protocol

Dispatch staff can search the complete local set of public customer codes/names/delivery
addresses while Sales is unavailable. This is a real receiver-owned asynchronous persisted
view: same-module native queries remain sufficient for existing Sales/Inventory queries,
but cannot provide Dispatch's independent database and source-outage availability. The view
supports planning; accepted shipment facts and current execution authorization remain separate.

Targeted hydration was rejected because searching unknown customers requires the complete
population. Retained broker state/watch could work but would introduce a maintained state
store and publication bridge without established need. Public export plus overlapping durable
whole-state delivery reuses existing Gloaming outbox/inbox and is selected. No RabbitMQ,
retained-broker or historical-business-effect replay guarantee is claimed.

Sales owns the public `CustomerDeliveryState` Contract. Its existing profile version explicitly
orders this particular complete disclosed state, including listing withdrawal/relist. This
is a new producer commitment, not a generic aggregate version assumption. Actual existing
`CustomerProfiles.ChangeAsync` now enqueues complete public replacement/tombstone state inside
its existing native transaction. Public registration creates the real Sales profile/address,
and listing changes update that same profile/revision transactionally. New registrations and
listing changes retain accepted audit without publishing private text in audit details.

A native Sales migration adds the persisted listing flag, defaults historical profiles to
listed, and preserves existing schema/data. Physical customer deletion and ID reuse are
unsupported: withdrawn rows remain permanent exportable tombstones with public text omitted.
Every supported live source mutation publishes; finite historical seed is setup-only. Old
binaries or native SQL bypasses must stop before rollout. There is no per-tenant source writer
serialization gate, timestamp cursor or allocated-message-sequence progress assumption.

Dispatch owns separate persistence and public APIs, referencing Sales.Contracts only. A fixed
trusted in-process relay transfers producer outbox messages to receiver public intake. The
receiver persists Organization admission before any copy; unadmitted deliveries fail and stay
in source outbox retry. Accepted intake is durable before source dispatch completion.

## Correctness and readiness

The source exports bounded current pages ordered by immutable customer key, including
historical state that never emitted a message. Each row's values and public revision are
read together in one SQL statement. This is a current per-key traversal, **not** one consistent
source snapshot. Mutation coverage comes from durable overlapping publication.

Dispatch merges both copied and delivered state using one private helper. A short native
Organization population-row lock serializes receiver merges; no source/network I/O occurs
inside it. Higher revisions win; lower revisions cannot overwrite them; equal conflicting
state fails visibly. Permanent tombstones prevent old delivery/copy resurrection. Page
records and expected generation/cursor/copy count/readiness commit together. The existing
Gloaming inbox processor commits handler state and its receipt together. Failure tests
exercise native rollback after state writes and before progress/receipt completion.

Restart reads persisted page progress using a fresh operation scope. Immutable source keys,
retained source rows/tombstones and published new keys make resuming safe while source
changes. The copy API rejects stale progress and wrongly ordered/mismatched-tenant pages.

Ready means the baseline traversal completed with durable overlapping capture. It does not
promise exact source-head freshness, an empty backlog or a single point-in-time snapshot.
Late commits and new keys behind the copy cursor converge through ongoing messages, which
may still be pending when readiness turns true. `NoWork`, timestamps and source maximum
positions are never used as readiness barriers. Search rejects incomplete populations and
always constrains Organization; it returns up to 100 matching rows from the full local set.

## Fresh executable proof matrix

New tests use real PostgreSQL 18.6 containers and two distinct databases per proof. They run
the actual Sales API, transactional publication, dispatcher, public receiver intake, inbox
processor, copy and search. No mocked source/receiver DbContexts establish these results.

| Proof | Evidence |
| --- | --- |
| Historical population beyond replay; actual ongoing profile edit; source-outage query | `HistoricalPopulationAndOngoingChangesAreQueryableWithoutSales`: historical seed has zero outbox rows; receiver-only provider with no Sales registration serves updated address. |
| Durable capture before receiver admission | `ProducerRetainsUpdatesBeforeReceiverAdmissionAndRecoversWithoutLostCapture`: publication failure leaves pending source outbox; after admission it retries and completes. |
| Old copy response and restart | `OlderCopyCannotOverwriteCapturedUpdateAndRestartResumesDurablePageProgress`: newer delivered state survives older copy; fresh provider resumes persisted cursor/generation. |
| Late commit and creation below cursor | `LateCommitAndNewKeyBehindCopyCursorAreCapturedAfterBaselineCompletes`: actual source edit blocked after earlier save, baseline completes, transaction commits later; new low UUID also arrives through outbox. |
| Deletion before and after initial copy; relist and stale message | `WithdrawalsBeforeAndDuringCopyFenceOldMessagesAndRelistingHasNewRevision`: exported historical tombstone and ongoing withdrawals retain fences; relist advances revision. |
| Delete racing older copy | `WithdrawalDuringCopyCannotBeResurrectedByTheOlderExportPage`. |
| Tenant admission/isolation | `AdmissionAndTenantIsolationHoldAcrossExportIntakeAndSearch`: unregistered/unknown tenant rejected; Alpha/Beta source pages, inbox processing and search stay separate; mismatched page Organization rejected. |
| Duplicate identity, out-of-order replacement, equal-revision conflict | `DuplicateReceiptsAndOutOfOrderStateDoNotRepeatEffectsAndConflictsFailVisibly`: one receipt per delivery, stale state ignored, conflicting equal revision remains failed/pending. |
| Real simultaneous receiver lock overlap | `NativeConcurrentCopyAndInboxUpdateSerializeWithoutStaleOverwrite`: both native transactions observed waiting on population lock; final newer state survives either lock acquisition order. |
| Atomic copy effects/progress | `CopyFailureRollsBackRowsAndCheckpointAndRetryUsesSameProgress`: PostgreSQL trigger rejects progress update; rows/checkpoint roll back and retry succeeds. |
| Atomic handler effects/receipt | `InboxCompletionFailureRollsBackAppliedStateAndRetainedDeliveryRetries`: trigger rejects receipt completion after handler save; state/receipt roll back; scheduled retry succeeds. |
| Atomic source mutation/publication | `RejectedSourceSaveCannotCommitProfileWithoutItsPublicUpdate`: outbox insert failure rolls back earlier customer/address/audit saves. |

Initial implementation B2 run: **12 passed, 0 failed, 0 skipped** (10.6 seconds), SDK 10.0.401,
.NET 10.0.12, PostgreSQL 18.6, local Podman socket. Slice build passed with zero warnings/errors.
The first fault-retry test assumed immediate eligibility; it was corrected to observe existing
Gloaming backoff before retry. A stronger page-order guard also required correcting a stale
page test's expected validation exception. These are fresh implementation proofs, not claims
inherited from deleted B1 or archived samples.

Source adoption additionally required updating the existing finite Sales HTTP RLS composition:
outbox joins the protected model/tables with tenant equality and SELECT/INSERT only. HTTP
credentials still cannot dispatch/update/delete it; source worker credentials remain separate
from HTTP. The historical migration test now seeds its old schema explicitly, rather than
inserting the current EF model before the new column exists. Focused existing HTTP profile regression passed **13/13**, zero skipped (12.3 seconds),
including restricted-role profile/outbox commit and historical migration preservation.
CSharpier checked all 32 slice C#/project files including generated artifacts. Local
documentation links and whitespace checks passed; the initial empty index was
preserved until the explicitly preapproved coherent commit staging.

## Review corrections and fresh verification

### Standards findings

The reported statement-spacing omissions were valid. Guards in Sales's directory methods,
Dispatch search/merge/intake, and independent statements after the demo/proof loops and native
test control-flow blocks now have the required blank lines. CSharpier preserves those manual
separators. No repository-wide formatting or root solution/CI changes were made.

The duplicated Organization allowlist and subscription constant were also valid maintenance
findings. Both operation admission and incoming state validation now call Dispatch's private
`DirectoryAdmission.Require`; intake uses the existing public
`DispatchRegistration.DirectorySubscription`. These changes keep policy in the consumer.

The durable protocol choices were documented in the plan/guide but lacked the required focused
architecture record. [ADR 0022](../adr/0022-dispatch-directory-complete-state-population.md)
now records complete-state profile revisions, permanent source/receiver tombstones, no physical
deletion or ID reuse, capture before export, and eventual baseline readiness. It explicitly
distinguishes the selected B2 protocol from the rejected B1 writer gate/feed.

### Spec findings

The missing `Listed` discriminator was a correctness defect. System.Text.Json could decode
`{"customerId":"80000000-0000-0000-0000-000000000001","revision":999}` as a valid
withdrawal with default `Listed=false` and null text. Processing could remove an existing
listed customer and permanently advance its fence. `CustomerDeliveryState` now marks identity,
revision and `Listed` as required JSON members; the existing value validation still enforces
the conditional listed/withdrawn text shape. Both relay and handler use that same wire Contract.

Two new PostgreSQL proofs retain literal malformed JSON and start with an already ready,
listed revision-1 customer and completed valid delivery:

- `RelayRejectsOmittedListedWithoutAcceptingOrCompletingDelivery`: actual retained source
  outbox dispatch throws `JsonException`, remains pending and creates no receiver receipt.
- `DurableIntakeWithOmittedListedCannotAdvanceStateOrCompleteItsReceipt`: independently
  retained wire intake reaches the real processor, fails decoding and remains unprocessed.

Both assert that the existing identity, code/name/address, listed revision, durable population
progress and prior completion count stay unchanged. This verifies rejection before the
permanent fence can advance, rather than checking deserialization alone.

Fresh corrected suite: **14 passed, 0 failed, 0 skipped** (13.6 seconds), SDK 10.0.401,
.NET 10.0.12 and PostgreSQL 18.6 with separate source/receiver databases on local Podman.
The first new relay proof needed an explicit native transaction around its setup enqueue;
the corrected proof uses the actual outbox contract. An initial sandbox run could not bind
the test runner's IPC socket; the successful run had local socket access. These are fresh
post-review proofs, distinct from the earlier 12-test and 13-test HTTP results above.

Commands from the repository root:

```sh
dotnet build samples/Wholesale/IntegrationTests/Wholesale.IntegrationTests.csproj --no-restore --disable-build-servers -m:1 -v minimal
DOCKER_HOST=unix:///run/user/1000/podman/podman.sock TESTCONTAINERS_RYUK_DISABLED=true dotnet test --project samples/Wholesale/IntegrationTests/Wholesale.IntegrationTests.csproj --no-build --no-restore --no-progress --no-ansi --output Normal --show-stdout Failed --show-stderr Failed
dotnet csharpier check samples/Wholesale/IntegrationTests/Directory samples/Wholesale/Worker/Directory/DirectoryHost.cs samples/Wholesale/modules/Dispatch/Dispatch samples/Wholesale/modules/Sales/Sales.Contracts/ICustomerDeliveryDirectory.cs samples/Wholesale/modules/Sales/Sales/CustomerDeliveryDirectory.cs
git diff --check
```

Build passed with zero warnings/errors; CSharpier checked all 17 files selected by the command.
The empty staged snapshot remains unchanged; all review corrections are unstaged.
Review-worthy additions are the wire Contract, `WireContractTests`, `DirectoryAdmission` and
ADR 0022. No new reusable mechanism was proven; the existing library, template and remaining
product findings below still apply.

## Extraction and product findings

Existing Gloaming outbox dispatch, durable intake, inbox processing and worker loops removed
the repeated technical delivery/transaction work. Their reuse is concrete. There is no new
Gloaming public interface or general replication service.

The small `DirectoryMerge` helper is shared by copy and updates inside Dispatch. Revision
meaning, whole-state shape, conflict handling, permanent tombstones, key lifecycle, listing
visibility, tenant admission, cursor progress and readiness remain consumer policy. A generic
version merge or population coordinator would conceal these obligations; one adoption does
not yet justify that extraction. **No new reusable library mechanism was proven.**

The temporary executable contains only explicit setup/roles, public-boundary relay and bounded
copy orchestration; it can move into the Wholesale product Worker. Its registration is a
composition example, not a mandatory template preset. The actual source and receiver live in
module-owned projects, suitable for that consolidation. No library references sample modules.

Review-worthy files are Sales.Contracts/ICustomerDeliveryDirectory.cs; Sales's public-state
publication and existing profile transaction; Sales listing migration/model; Dispatch.Contracts;
Dispatch merge/population/intake; native Dispatch migration; Worker Directory relay/composition;
14 PostgreSQL tests; and the finite HTTP source RLS/migration compatibility changes.

Remaining gaps: source-head/catch-up barriers and monitoring, externally authenticated
transport, subscriber fanout, receipt/outbox/tombstone cleanup and capacity policy, source
read replicas, physical deletion/ID reuse, throughput benchmarks, in-place reset, and live
shipping/authorization policy. No broker proof is needed for the selected local transport.
Product consolidation, complete solution/CI registration and mandatory two-axis review remain
root integration work; this isolated branch intentionally avoids shared root metadata.

Canonical Directory adoption subsequently folds the temporary B2 host/tests into Worker and IntegrationTests. The [adoption report](https://github.com/oxface/modulith-foundry/blob/86012044cdaf248c90f65a05eb9dc64e8accfb07/docs/reports/b2-directory-product-adoption.md) records new executable and route-claim mechanism evidence separately from this checkpoint.
