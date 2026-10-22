# P1: projection assessment outcome

Date: 2026-10-22. Baseline: `790a8b7`; isolated topic branch
`docs/v1-projection-assessment`. Scope: the
[bounded decision and proof contract](../plans/p1-projection-assessment.md), with no runtime,
library interface, dependency, migration, solution, CI or template changes.

## Verdict

Native module-owned queries meet the active Sales draft search/summary, Inventory availability
and Purchasing point-summary requirements. The concrete unmet requirement is B2's selected
Dispatch customer contact/address directory: complete local search across independently
owned Sales customers, including pre-subscription state, available while Sales is unavailable.
Select that one consumer-owned asynchronous persisted view and its ongoing population/update
proofs. P1 does not build an additional receiver or select a general projection engine.

This is a positive scoped projection decision. Deferring a general library engine does not
defer the selected Dispatch view, nor claim that EF can query across private module contexts.
B2's implementation and fresh results remain its own evidence; this assessment does not
count planned directory proofs as passing tests.

## Evidence and corrected assumptions

| Assumption assessed | Evidence and disposition |
| --- | --- |
| Aggregates cannot be joined for reads | Rejected within an owning module. [Sales queries](../../samples/Wholesale/modules/Sales/Sales/DraftOrders/DraftOrderQueries.cs) join draft/customer and calculate line totals. `JoinedFiltersPrecedeCountAndDeterministicPaginationWithoutTracking` independently expects count 3, total 40, two lines and ID-based tied-total paging. |
| SQL filters/count/page can ignore permission completeness | Rejected. [Authorization proofs](../../samples/Wholesale/CqrsDemo.Tests/QueryTests.Authorization.cs), especially `ResourceGrantsFilterDetailsTotalsAndPagesBeforeMaterialization`, cover sparse visibility and overlapping grants. Current EXISTS intersects subject/tenant grants before the query shape/count/page. |
| A current customer name must be copied into draft state | Rejected for this requirement. `NameFilterUsesCurrentJoinedCustomerAndLiteralCaseSensitiveSubstring` changes the profile and observes new joined results while draft version/total remain unchanged. |
| Every event-sourced read needs replay or another persisted projection | Rejected. `InventoryFiltersReservedAvailabilityInSqlWithoutReplayingHistory` verifies availability 3 at threshold 3, none at 4, native JOINs, tenant isolation, no tracking or stored-event reads. [Purchasing](../../samples/Wholesale/modules/Purchasing/Purchasing/PurchaseOrders/PurchaseOrderQueries.cs) derives its point summary from checked inline state. |
| Purchasing's point-summary tests establish server-side JSON line total search | Rejected. Source inspection shows materialization before `state.Total`; list filtering/sorting/counting over those totals has no current requirement/proof. [ES2's reviewed replacement](../plans/es2-native-ef-simplification.md) already removed the dormant summary table through a forward migration. Earlier E6 multi-view results are historical. |
| Deterministic sorting means count/page form one snapshot | Rejected. [Transaction proofs](../../samples/Wholesale/CqrsDemo.Tests/QueryTests.Transactions.cs) show concurrent insert drift under ReadCommitted and stable count/page under caller-selected RepeatableRead. A2.1 repeats the distinction for grant revocation. |
| One same-module query proves a cross-module common snapshot | Rejected. [CqrsJourney](../../samples/Wholesale/CqrsDemo/CqrsJourney.cs) uses Inventory and Sales Contracts in separate operations. It supplies advisory availability, without a common snapshot, reservation or cross-module transaction. |
| A shared PostgreSQL server authorizes private-context joins | Rejected by [current module ownership](../../samples/Wholesale/modules/README.md). Contracts export immutable business results, not rows/IQueryable. C1's SQL observation also asserts the Sales search does not query `inventory.`. |
| Lazy lookup or a one-time offline export meets Dispatch complete independent search | Rejected for the selected requirement. Lazy lookup cannot discover a complete population and requires Sales availability; an offline export alone cannot maintain ongoing changes. B2 owns the explicit export/update overlap and readiness protocol. |
| Private event headers/outbox timestamps give a safe public global checkpoint | Rejected. [B1 findings](b1-retention-and-contract-assessment.md) distinguish precommit sequence/time from committed progress and private facts from public messages. Dispatch needs a disclosed state revision/tombstone contract and a proven population boundary. |
| Current single-stream rebuild covers asynchronous multi-stream repair | Rejected. [Current event-store capabilities](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/docs/capabilities.md) cover one aggregate's captured history and native concurrency token. They do not define shared-row progress, directory population, subscriptions or projection cutover. |

The native query verdict is concrete and deliberately narrow. No new arbitrary grouping,
query-plan/capacity, arbitrary provider, cross-module join or historical-metric guarantee is
inferred from it. These queries do not remove the need for persisted consumer-owned views
when ownership, availability or retained-history requirements demand them.

## Verification

The source inspection above used the active baseline, not `archive/proof-sample`. The
[C1 report](c1-cqrs-consumer-patterns.md) records its original 20 cases on 2026-10-15;
[A2.1](a2-1-sales-resource-permissions.md) records its 33-case CQRS execution on 2026-10-16.
Those report totals are historical evidence. P1 adds no duplicated runtime tests.

Fresh P1 verification used .NET SDK 10.0.401 and PostgreSQL 18.6 Testcontainers on rootless
Podman (`DOCKER_HOST=unix:///run/user/1000/podman/podman.sock`,
`TESTCONTAINERS_RYUK_DISABLED=true`, `DOTNET_PROCESSOR_COUNT=4`).

| Check | Fresh result |
| --- | --- |
| Targeted CQRS project restore/build | Passed; zero build warnings/errors. Restore used normal package/audit sources with network access and serialized MSBuild. |
| Existing `QueryTests` class, including C1 transaction and A2.1 authorization cases | **33 passed, zero failed/skipped**, 21 seconds. This reruns existing proofs; it adds no new runtime mechanism. |
| Documentation | All 25 local file links across these two documents resolve. |
| Whitespace and review snapshot | New document whitespace checks pass. The initially empty index remained unchanged before the authorized local commit. |

Reproduce the successful run after restore:

```sh
DOTNET_PROCESSOR_COUNT=4 dotnet build samples/Wholesale/CqrsDemo.Tests/CqrsDemo.Tests.csproj --no-restore -m:1 -nr:false
DOCKER_HOST=unix:///run/user/1000/podman/podman.sock \
TESTCONTAINERS_RYUK_DISABLED=true DOTNET_PROCESSOR_COUNT=4 \
dotnet test --project samples/Wholesale/CqrsDemo.Tests/CqrsDemo.Tests.csproj --no-build --no-restore --filter-class '*QueryTests'
```

The first sandboxed attempt failed before execution: NuGet audit needed network access,
then the test runner's named-pipe socket was denied. Retrying with the required access
preserved normal audit settings and ran all 33 cases successfully. Purchasing inline/
rebuild results remain the separate dated ES2 evidence; they were not rerun for P1.
The new B2 directory must provide its own actual execution record; the proof-contract
table in the plan is an obligation inventory, not execution evidence.

## Review-worthy files and findings

Review the [plan](../plans/p1-projection-assessment.md) for the native-query/ownership
boundary, selected Dispatch view, per-key revision/tombstone assumptions and population/
checkpoint/backfill obligations. Review this report for evidence attribution and remaining
gaps. Runtime query/test files are evidence pointers only and are unchanged by P1.

**Library:** no new reusable mechanism was proven. Existing EF query composition and
Messaging primitives remain the mechanisms. No projector API or package is proposed.
Potential repeated population/checkpoint mechanics need comparison after real B2 adoption;
public-state meaning, ordering, tombstones and serving policy remain consumer-owned.

**Template:** module-local Queries/Filters and explicit receiver registration/population
are editable recipe candidates. No generated T1/T2 composition changes or new supported
preset follow from the assessment. **Sample/product:** use one coherent Wholesale model
for Sales/Inventory/Dispatch stories, keeping focused executables as proof entry points;
retain independent family adoption examples with intentionally narrow dependencies. This
is a consolidation recommendation, not a file move or a claim of a complete product.

Remaining gaps are explicit: B2's fresh directory proofs, performance/indexing at product
scale, future Purchasing list translation, any real historical metric, shared-row
multi-stream maintenance, cross-module atomic snapshots/writes, general asynchronous
event-store progress and online versioned projection cutover. None is silently supplied
by the native query or single-stream maintenance evidence.
