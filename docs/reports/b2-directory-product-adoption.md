# B2 Directory adoption in the canonical Wholesale product

Date: 2026-10-22. Scope: finish S1/B2 product adoption after the original B2 checkpoint.
The temporary Directory executable is removed. The canonical Worker now admits, populates,
relays, processes and searches Dispatch's directory; ongoing protected API profile edits
continue to publish whole-state updates in their native Sales transaction.

## Move, merge and remove proof map

| Earlier artifact | Canonical owner / evidence |
| --- | --- |
| DirectoryDemo composition and relay | `samples/Wholesale/Worker/Directory`, with relay under `Delivery` and explicit receiver/source registration |
| DirectoryDemo Program operations | Worker `--role directory --Operation setup/admit/populate/relay/process/run/search/progress/export` |
| DirectoryDemo.Tests population/atomicity/wire tests | `samples/Wholesale/IntegrationTests/Directory`; all fourteen original cases, including omitted-`listed` literal wire rejection, retained |
| Actual API ongoing publication | Existing protected profile handlers and Sales transaction; `Api/DirectoryAdoptionTests.cs` invokes those handlers and launches canonical Worker child processes |
| Standalone Directory project files | Removed; existing Worker/IntegrationTests projects own code and tests; Dispatch/Dispatch.Contracts registered in the root solution |
| Module and transport fences | Architecture checks include Dispatch implementation/Contracts and nonempty Directory relay types; no peer implementation, persistence or broker dependencies in relay |

No module implementation, migration or Access fixture is relocated or rewritten. Historical consumer domains
and finite setup remain unchanged. No library or template business policy was introduced.
Existing Wholesale CI commands and path globs already cover Worker, IntegrationTests and all
module paths; they now include Directory without an extra shipped product or test command.
Package workflow/path additions and all three WF2 solution entries remain intact.

## New executable proofs and failure paths

`CanonicalWorkerPopulatesThenConsumesActualHttpEditsWithRestrictedRelayAndReceiverOnlySearch`
uses real PostgreSQL source/receiver databases, the admitted protected API edit, and separate
canonical Worker processes for setup, admission, population, relay, inbox processing and search.
The relay is provisioned with the existing restricted outbox-only Sales login and verifies its
native grants; it receives no Sales business credentials. Search receives only Dispatch
credentials and succeeds while the source profile table is unavailable. A forged Organization
is rejected by the existing Dispatch admission policy. Named system actor `dispatch.directory`
identifies trusted Worker operations; no human login is fabricated by this CLI.

`RestrictedBrokerExecutableLeavesDirectoryRouteUnclaimedWhileStockPublicationProgresses`
starts the actual restricted broker Worker and proves Inventory publication progresses while
Directory attempts, revision, leases and completion remain untouched. `LocalSalesPublisherLeavesDirectoryRowUnclaimedWhileReservationProgresses`
proves the same route fence for native local reservation composition. These route selectors are
claim policy only: the restricted login can read/update delivery state across allowed outbox
rows, and no selector is an authorization boundary. Existing database grants/RLS tests remain.

The original fourteen B2 proofs still cover historical population without old messages,
updates behind the cursor, late commits, source outage, revision/tombstone ordering, admission,
conflicts, copy/cursor rollback, durable restart, source publication rollback and inbox effect/
completion rollback. They are historical assertions rerun in the canonical suite, not fourteen
newly invented adoption tests. Readiness remains baseline traversal plus eventual convergence,
not a source-head barrier or zero-backlog assertion.

## Mechanism demonstrated by this composition

Canonical adoption exposed three publishers sharing Sales's native outbox. Unfiltered dispatch
would let an Inventory publisher claim Directory state and reject it before the correct relay
could deliver it. Repeating native SQL/provider dispatchers in each consumer would duplicate
lease/failure mechanics. A narrow `OutboxDispatchOptions.RouteKeys` claim selector now removes
that burden without adding a router, provider abstraction or multi-version engine.

The optional init-only `IReadOnlyCollection<string>?` defaults to all routes. The dispatcher
copies keys at construction; empty collections and blank/null entries reject before I/O.
PostgreSQL applies exact C-collated parameterized `ANY` selection before native skip-locked
claiming. Fresh provider proofs cover excluded rows unchanged, concurrent disjoint selection,
literal quoted/case-sensitive keys, mutable-input copying and invalid configuration. Existing
all-route defaults and lease/completion uncertainty behavior remain unchanged. See the
[self-contained family contract](../../src/Gloaming.Messaging/docs/route-selection.md) and
[extraction record](../plans/library-extraction.md).

This is new reusable mechanism evidence discovered during product composition. Original S1/B2
reports' earlier “no new reusable mechanism” findings remain true of their dated checkpoints.
Route assignment, trusted publisher binding, tenant admission, revision disclosure, listing,
merge/readiness, credential provisioning and deployment operations remain consumer-owned.
Templates gain no policy or new framework.

## Verification

Fresh verification on the final source:

| Check | Result / log |
| --- | --- |
| Full canonical Wholesale IntegrationTests, real PostgreSQL and RabbitMQ | 713/713, 0 skipped, 3m50s; `/tmp/directory-integration-final.log` |
| Outbox PostgreSQL provider suite | 108/108, 0 skipped, 25s; `/tmp/directory-outbox-tests.log` |
| Architecture module/relay fences | 114/114; `/tmp/directory-architecture-tests.log` |
| Full 83-project solution build | 0 warnings/errors; `/tmp/directory-solution-final.log` |
| Native `dotnet format ... style --verify-no-changes --no-restore` | Passed; `/tmp/directory-style-final.log` |
| CSharpier including generated source | 962 files passed; `/tmp/directory-csharpier-final.log` |
| Frozen archive integrity | All 800 files unchanged; `/tmp/directory-archive.log` |
| Changed Markdown links and whitespace | 0 missing local targets; `git diff --check` passed |

The 713 cases consist of the previous 696 canonical cases, fourteen retained B2 cases and three
new product adoption/route cases. The provider suite adds six cases to its previous 102. The
focused canonical Worker process journey also passed independently before the final full run
(`/tmp/directory-process-focused.log`); its strengthened admission rejection then passed in the
full run. No packaged-consumer verification is claimed here; root owns final release proof.
The first full run completed 713 cases and exposed three inherited earlier-schema migration
seeds using the current EF model, two new raw route fixtures missing native attempts, and an
older membership assertion counting a permitted Beta B2 publication. Corrections preserve the
migration targets and assertions using literal old-schema customer/address rows, complete the
new native queue fixture, and separately assert blocked Alpha and permitted Beta publications.
No actual migration or Access fixture was changed.

## Remaining boundaries

The existing finite two-Organization admission and trusted local intake remain deliberate sample
policy. External authenticated transport, receiver least-privilege provisioning, source read-role
provisioning, directory UI, tombstone/receipt cleanup, reset/replacement orchestration and a
caught-up barrier are not claimed. Registration/listing public module commands remain available
for admitted application composition; this change does not invent a human CLI identity or UI.
Browser/runtime sources are unchanged, so the prior six real runtime/browser proofs were not
rerun for this host-only addition. The canonical Worker process proof is new executable evidence.
