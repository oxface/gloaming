# E2.2 module-owned schemas and migrations

Checkpoint update, 2026-10-04: the owner reviewed and committed this 33-file slice as
`f2dcf2b`. The earlier review handoff below is historical; its unstaged status is not current.
[E2.3](e2-3-tenant-relationships.md) adds consumer-owned tenant-bearing relationships for review.

2026-10-04. Implemented after the owner's instruction to proceed from E2.1 checkpoint
`50e7933`. This slice is ready for owner review. Changes remain unstaged and uncommitted.
Scope and limits are in [the slice plan](../plans/e2-2-module-migrations.md).

## Outcome and evidence

The finite wholesale PersistenceDemo now composes Inventory and Sales in one PostgreSQL
database. Each module owns its DbContext, schema, initial migration, snapshot and migration
history. Native Npgsql options are shared between that module's runtime registration and
design-time factory. The consumer explicitly calls native migrations, then establishes
operation tenancy and explicitly seeds/saves fixture data. No library interface or
implementation changed; no new reusable mechanism was proven.

Sales introduces only an Organization-owned customer reference: GUID ID, customer code and
display name. Inventory retains stock references and explicitly global categories. The
same customer code is usable in two Organizations with independently expected names.
These are persistence fixtures, not complete aggregates, Access membership or module Contracts.

| Claim | Fresh supporting evidence |
| --- | --- |
| Each context owns its model/migration schema. | Two container-free cases inspect actual mapped entity sets and native Up/Down operations; only module-local schema/table/index operations are admitted. |
| Reviewed snapshots match current mappings. | Both actual design-time contexts report no pending model changes without database access. |
| Either module can initialize independently. | Two PostgreSQL cases cover Inventory-first and Sales-first; only the first module's tables/history exist before the second migration. |
| Initializing the second preserves the first. | Both ordering cases save a row before the second migration, then read its unchanged ID/value afterward. |
| History belongs to each context. | Database table inspection finds two schema-local histories and no public history; applied migration IDs match each context's own artifacts. |
| Sales wires the existing ownership utility. | Valid Alpha/Beta customers save/read independently; both sync/async save overrides reject foreign-owner inserts, with fresh reads showing no foreign write. |
| Actual entry point is repeatable. | First and repeated finite console runs migrate both modules and produce the four independently expected stock/customer lines. |
| Earlier protection survives migrations. | Existing Inventory ownership proofs now initialize with its real migration; independent GUID PostgreSQL proofs retain their separate fixture setup. |

Native `dotnet ef migrations add` successfully scaffolded both initial migrations without
running the console or requiring a database. Native offline `migrations list --no-connect`
then discovered only the selected context's migration. The tool intentionally cannot report
applied status offline; PostgreSQL cases establish the actual histories instead.

## Library, template and sample findings

**Library:** E2.1's explicit filter, ownership metadata and validation already support the
second consumer mapping. Neither a migration runner, base DbContext, registration facade
nor a custom persistence scope earned extraction. ActorIdentity and Tenancy remain independent;
the EF utility still requires neither. EF Design is a private sample dependency and native
`dotnet-ef` is a pinned development tool, not a runtime library dependency.

**Template:** copy/edit the exercised native options, design-time factories, module folders,
history configuration, explicit migration calls and scoped tenant establishment from the
sample. Runtime/design-time configuration must agree. Generated migrations are ordinary
consumer-owned C#; initial migration files were adapted to repository namespace/array
conventions without changing schema behavior. Snapshots live alongside their module's
migrations. A separate migration assembly/project is optional, not needed by this composition.
No template generator or extra implementation is introduced.

**Sample policy:** Inventory owns global categories and Organization stock references;
Sales owns Organization customer references. Names, keys, indexes, fixture population,
module initialization order and migration execution environment are consumer choices.
The sample schema checks reject unfamiliar migration operation kinds and foreign keys
until their ownership policy is deliberately reviewed. Raw SQL is rejected rather than
parsed; no synthetic xUnit/EF framework tests or generic architecture helper were added.

## Verification

All seven active suites passed: **119 tests, no failures or skips**, including **30 real
PostgreSQL cases** on disposable PostgreSQL 18.6 Testcontainers with resource reaping enabled.

| Suite | Passed |
| --- | ---: |
| ActorIdentity | 19 |
| Tenancy | 17 |
| ContextDemo | 15 |
| EF model/validation | 23 |
| Architecture/schema policies | 15 |
| PersistenceDemo PostgreSQL | 24 |
| Independent GUID PostgreSQL | 6 |

The active 12-project build passed with zero warnings/errors. CSharpier checked 75 files;
active style and analyzer verification passed. All 800 archived files passed checksum
verification. Archived suites were not rerun; their earlier results remain historical.
Remote CI has not been observed.

The audit checkpoint had 114 cases. E2.2 introduces six cases and removes the earlier
PostgreSQL model-classification case, whose assertions now live in the expanded fast policy,
for five additional cases net. The new tests exercise our module configuration and composition, not native
migration internals in isolation.

## Review files and remaining gaps

Review [runtime composition](../../src/Gloaming.Persistence/samples/PersistenceDemo/DemoComposition.cs) and
[explicit initialization/usage](../../src/Gloaming.Persistence/samples/PersistenceDemo/Program.cs), then
[Inventory options](../../src/Gloaming.Persistence/samples/PersistenceDemo/Inventory/InventoryDatabase.cs),
[its design-time factory](../../src/Gloaming.Persistence/samples/PersistenceDemo/Inventory/InventoryDesignTimeFactory.cs),
[Sales mapping/save overrides](../../src/Gloaming.Persistence/samples/PersistenceDemo/Sales/SalesDbContext.cs),
[Sales options](../../src/Gloaming.Persistence/samples/PersistenceDemo/Sales/SalesDatabase.cs) and
[its factory](../../src/Gloaming.Persistence/samples/PersistenceDemo/Sales/SalesDesignTimeFactory.cs).
Review the initial migrations, designers and snapshots under each module's `Migrations`
folder, [schema policies](../../tests/ArchitectureTests/ModulePersistenceTests.cs),
[database proofs](../../src/Gloaming.Persistence/samples/PersistenceDemo.Tests/ModuleMigrationTests.cs)
and the [updated consumer recipe](../../src/Gloaming.Persistence/samples/PersistenceDemo/README.md).

Schemas are ownership conventions, not hostile-code or credential isolation. This sample
uses fresh disposable databases; E2.1 databases created with `EnsureCreated` must be recreated.
There is no upgrade-import path, tested downgrade execution, parallel production migrator
protocol or multi-provider compatibility claim. Both modules commit independently.

E2.3 remains same-tenant parent/child constraints with native tenant-bearing keys. E2.4
remains separate version conflicts and failure/rollback through an actual consumer operation.
Shared cross-module transactions need their own named workflow and review. Trusted HTTP
ingress, membership, event sourcing, messaging and bootstrap delivery remain later slices.
