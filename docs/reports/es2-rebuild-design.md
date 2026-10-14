# ES2 rebuilding design findings

Historical diagnosis of the former gate protocol. The rejection of optimistic repair below
concerns an **unchanged event-version predicate**. The reviewed technical stamp alternative
is recorded in [ADR 0008](../adr/0008-native-optimistic-aggregate-rebuilding.md),
[the replacement scope](../plans/es2-native-ef-simplification.md) and
[its actual EF/PostgreSQL results](es2-single-stream-rebuilding.md#native-ef-replacement-2026-10-07).

Status: proposal evidence, 2026-10-07. ES1 is checkpointed as `ab85ec9`; family ownership is
checkpointed as `39c1ab3`. This report supports the [proposed ES2 interface and scope](../plans/es2-single-stream-rebuilding.md).
No new C# library implementation, approved interface or library integration guarantee follows
from these findings. Proposed runtime files have not been edited.

## Current consumer and library inspection

Inventory and Purchasing stores both load through EventStore.GetForWritingAsync: a native
transaction is required, the header is captured untracked, required projection rows are loaded
and version/time checked, then the main adapter restores a root with no pending events. Native
Version concurrency tokens protect the later advancing append. No pre-read exclusion currently
exists. The raw counter remains independently adoptable without required inline state.

The shared InlineProjectionStorage loader intentionally rejects missing/behind rows and
validates ahead/time/key mismatches. Calling it to prepare a repair would fail before rebuilding
could restore a missing row. Repair needs a separate internal raw-row observation path, without
old-body restoration, while ordinary loading keeps its fail-closed policy.

ValidateEventStreamChanges requires inserted events, an advancing tracked header and each
registered changed projection. Projection-only changes are explicitly rejected. Removing that
check or disabling the save validator for maintenance would weaken ordinary writes. A prepared,
context/transaction/key-bound repair path is the proposed alternative.

Main adapters already encode fresh detached candidates. Purchasing's summary independently
owns line amounts and can Evolve(null, completeHistory); repair must use that reducer rather
than derive summary fields from the main JSON. Both existing history readers capture a head,
select its ordered prefix, validate range/end timestamps, decode through the explicit registry
and use consumer-owned evolution. The proposal reuses those native selection paths; it does
not add a general history reader/projector engine.

Fixture bootstrap uses public InventoryHistorySeed.Stage and PurchasingHistorySeed.Stage
followed by native save/commit in DemoJourneys and HistoryReadTests. Enabling gate enforcement
on those models affects these direct native callers too. Proposed asynchronous admission
wrappers preserve the existing literal payload/version/time construction and synchronous
helpers. Ordinary ungated/raw adoption remains valid; a new general save bypass is not proposed.

## Archived reference, not a new execution

[StockPositionWriteGate](../../archive/proof-sample/modules/Inventory/Inventory/StockPositions/Persistence/StockPositionWriteGate.cs)
opens a native transaction and obtains a shared or exclusive advisory transaction lock using
an Organization-derived key. Normal writers obtain shared admission before looking up state;
[StockPositionProjectionRebuilder](../../archive/proof-sample/modules/Inventory/Inventory/StockPositions/Rebuild/StockPositionProjectionRebuilder.cs)
obtains exclusive admission before head capture. It replays retained events, replaces the
write model and saves/commits with consumer-owned authorization/audit/metrics.

The [archived rebuild tests](../../archive/proof-sample/tests/PersistenceTests/StockPositionPersistenceTests.Rebuilds.cs)
cover already-loaded writers, later writers, deleted/corrupt views, unreadable history,
replacement failure, cancellation and permission/context denial. The
[correctness-gate tests](../../archive/proof-sample/tests/PersistenceTests/StockPositionPersistenceTests.CorrectnessGate.cs)
explicitly show Organization-wide exclusion, including other positions and creation, while
readers/another Organization continue. These files were inspected, not rerun in this proposal.
Purchasing has replay/read support but no analogous archived repair implementation.

ES2 proposes a narrower complete-key scope for existing identified streams. It does not inherit
the archive's business-key discovery/creation guarantees, Organization gate, audit atomicity
or historical performance evidence. Current stream IDs remain explicit caller inputs.

## Fresh PostgreSQL coordination diagnostics

Executed against **PostgreSQL 18.6 (Debian 18.6-1.pgdg13+2)** in one disposable rootless Podman
container. No host ports were published. The container was removed on completion. Reproduce:

```sh
node src/Gloaming.EventSourcing/docs/proofs/es2-coordination.ts
```

The [standalone script](../../src/Gloaming.EventSourcing/docs/proofs/es2-coordination.ts)
uses separate persistent psql sessions and observes advisory waits through pg_stat_activity.
This is native SQL design evidence, not EF, domain/codec, proposed-interface or integration proof.
The initial tables contain one stream header at version 1, history deriving amount 10 and a
projection that is either correct (10) or deliberately wrong (99).

| Diagnostic | Actual result |
| --- | --- |
| Writer reads wrong projection; repair takes a header row lock, restores amount 10 without advancing version, commits; writer later saves against version 1 | Both commits succeed. Persisted amount becomes 104 while retained history derives 15 at version 2. A repair-only row lock/version predicate does not invalidate the old state observation. |
| Writer obtains shared advisory admission before load; rebuild requests exclusive admission | PostgreSQL reports the rebuilder waiting. Writer appends/commits version 2; rebuild then reads head 2 and restores amount 15. |
| Repair holds exclusive admission, updates wrong amount to 10; later writer requests shared admission | PostgreSQL reports the writer waiting before load. After repair commit it reads 10. A distinct key obtains shared admission while repair holds its key. |
| Two writers obtain shared admission and both capture version 1 | Both enter. The first advancing header update succeeds; the second expected-version update affects zero rows. Shared admission preserves native optimistic competition. |

Four diagnostics passed. Their limited native update predicates represent the important
same-version observation race; they do not prove EF's actual statement order, provider gate
key derivation, exact tracked-save participation, state-dependent command eligibility,
transaction cancellation or any business Contract. Those require the executable consumer
proofs listed in the brief after interface review.

The initial execution used Python. During owner review on 2026-10-07 the retained harness
was translated to TypeScript, requiring Node 24.21+ and rootless Podman, with only Node built-ins.
All four diagnostics passed again on PostgreSQL 18.6; strict TypeScript checking and Prettier
also passed. This follow-up execution remains native SQL evidence, not a rerun of the C# suites.

The first result rejects the simpler proposal of adding a repair operation protected only
by existing append Version tokens or a row lock taken solely by the repairer. The latter
results support investigating shared/exclusive admission before state loading. They do not
establish universal fairness, lock throughput or an external-writer enforcement mechanism.

## External behavioral references

Marten documents both broader projection rebuilding and a single-stream rebuild operation.
Its optimized rebuilding mode includes a multiple-view limitation, so Purchasing's independent
main/summary projections require our own explicit contract and proof. Marten remains a design
reference; no Marten runtime, daemon or generated code is selected.
[Official rebuilding documentation](https://martendb.io/events/projections/rebuilding)

Owner-review follow-up, 2026-10-07: the inspected
[Marten single-stream implementation](https://github.com/JasperFx/marten/blob/master/src/Marten/AdvancedOperations.cs)
opens a lightweight session, reconstructs one document type from stream events, stores it and
saves its own session. The GUID overload explicitly disables concurrency checks. This method
does not show ES2's shared-writer/exclusive-rebuild admission before reads, or replacement of
all required views together. That is a comparison of this helper's method body on the moving
master branch, not a claim about every Marten projection or release. No Marten runtime tests
were run. Its broader daemon rebuilds inline and asynchronous projections; its multiple-view
restriction belongs specifically to optimized rebuilding, not all rebuilding.

Our greater coordination scope is online same-version repair alongside state-dependent
writers. A simpler offline repair with writers paused would have a different contract.
Registration boilerplate and module maintenance facades are separate from that race; the
[review follow-up proposal](../plans/library-extraction.md#es2-owner-review-follow-up)
records candidates for simplifying those integration steps without weakening coordination.

PostgreSQL provides shared/exclusive advisory locks with application-defined keys, and
transaction-level locks end with the transaction. Their cooperation contract does not constrain
code that never takes them. The proposed package would keep that provider-specific implementation
outside the EF/core libraries.
[PostgreSQL 18 locking documentation](https://www.postgresql.org/docs/18/explicit-locking.html)

The proposed full-key scope and ReadCommitted restriction are design conclusions from the
inspected operations and PostgreSQL snapshot/lock ordering, not capabilities inherited from
Marten or certificates of other isolation levels/providers. Any future alternative needs its
own selected interface and real database contract proofs.

## Extraction and policy findings

Candidate shared mechanisms are pre-read cooperative admission bound to native transactions,
full captured-prefix metadata validation, all-required-row preparation/staging and precise
native-save association. The proposed single handler operation concentrates those obligations
across one-view Inventory and independently evolved two-view Purchasing.

Consumer-owned policy remains admission, tenant selection, durable event decoding, business
sequence/evolution, projection shape, ordinary query composition and final save/commit.
Business-key discovery, already-committed bad decisions, external SQL writers and privileged
history mutations remain outside this repair contract. Existing Events utilities are optional;
standalone direct-JSON provider tests must establish independent adoption.

**No new reusable mechanism is proven yet.** The diagnostics reject an insufficient concurrency
approach and narrow the proposed coordination contract. They do not justify a generic projector,
worker/lease engine, history reader or provider matrix. Owner review must precede the new C#
interface and implementation; the eventual slice report must compare actual complexity removed
against configuration/public-surface costs and record all remaining gaps.
