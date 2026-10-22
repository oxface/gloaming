# Wholesale persistence demonstration

A finite consumer of [EF ownership utilities](../../Gloaming.Persistence.EntityFrameworkCore/README.md)
and [Tenancy](../../../Gloaming.Tenancy/Gloaming.Tenancy/README.md), without actor identity or Access.
It uses native Npgsql/EF registration, model configuration and save overrides. No HTTP
endpoints or authentication substitute are introduced. Inventory and Sales own their native
DbContexts, schemas, migration artifacts and history tables in the same database.

## Run

Supply `WHOLESALE_DEMO_CONNECTION_STRING` through your shell/environment for an empty,
disposable PostgreSQL database, then run from the repository root:

```bash
dotnet run --project src/Gloaming.Persistence/samples/PersistenceDemo/PersistenceDemo.csproj
```

The consumer explicitly calls Inventory's native `MigrateAsync`, then Sales's, before
initializing operation tenancy and reading/seeding fixture data. Each migration commits
independently; there is no shared transaction. Use a fresh disposable database: databases
created by E2.1's former `EnsureCreated` fixture must be recreated. Do not print or commit
connection credentials.

Expected output on first and repeat invocation:

```text
wholesale-alpha: WIDGET availability=42
wholesale-alpha: BUYER customer=Alpha Retail
wholesale-alpha: BUYER address=42 Market Street
wholesale-alpha: BUYER profile-version=2
wholesale-beta: WIDGET availability=7
wholesale-beta: BUYER customer=Beta Retail
wholesale-beta: BUYER address=7 Dock Road
wholesale-beta: BUYER profile-version=2
```

## Ownership and language

Inventory owns the `inventory` schema, `StockReference` and `ReferenceCategory` tables and
reference-data reads. These are persistence fixtures, not rich stock aggregates or reservation
logic. Each Organization has its own SKU/quantity rows; Organization keys map explicitly from
the selected technical tenant. Initial quantities 42/7 are consumer-supplied. Soft deletion
has its own named filter; it can be disabled while Organization filtering remains active.

Reference categories are explicitly global sample data. Their saves/queries need no tenant;
that fixture policy does not grant access to Organization-owned rows. The sample has no
membership or permission mechanism; those enter in E3.

Sales owns the `sales` schema and the customer/address reference tables. Customer codes are unique within
an Organization; the same `BUYER` code can identify different reference customers in Alpha
and Beta. Names are fixture data, not an application-user, membership or complete customer
aggregate model. Both modules set row ownership explicitly before saving.

An address is a separately mapped, tenant-owned child with a customer ID and address line.
The customer's alternate key `(OrganizationKey, Id)` is referenced by the address's required
foreign key `(OrganizationKey, CustomerId)`. An Alpha address cannot point to a Beta customer,
even when supplied only its ID. Both rows explicitly register ownership; protection is not
inferred from the relationship. The consumer selects native restricted deletion: addresses
must be explicitly removed before deleting their customer. No cascading deletion is configured.

The Organization is now an alternate-key component on customers. Native EF key immutability
can reject changing it during change detection before the utility returns a typed ownership
failure. Ordinary detached edits to non-key fields remain supported. There is no automatic
exception translation or tenant transfer; these references remain persistence fixtures.

- **Stock reference**: consumer-owned fixture availability for a SKU in an Organization.
- **Reference category**: global fixture classification, independent of an Organization.
- **Customer reference**: Sales-owned customer code/display name in an Organization.
- **Customer address reference**: Sales-owned address line belonging to a customer in the same Organization.
- **Organization** and **Tenant** follow [the project glossary](../../../../CONTEXT.md).

## Versioned profile changes

[CustomerProfileChanges.ApplyAsync](Sales/CustomerProfileChanges.cs) is consumer-owned
coordination code. It loads the selected Organization's customer/address pair, uses the
request's expected customer version in the native concurrency predicate, explicitly advances
that version, saves the name, then saves the address. It requires a caller-owned Sales
transaction and returns the staged version; the caller decides whether to commit.

[Program](Program.cs) demonstrates native `BeginTransactionAsync`, the ordinary operation
call, `CommitAsync` and explicit `RollbackAsync(CancellationToken.None)` on failure. Use a
dedicated context without unrelated pending changes. After failure, roll back and discard
the context: native rollback does not restore the accepted change tracker. No library
scope, retry, exception translation or runtime interceptor participates.

New customers explicitly start at Version 1. A native database default backfills existing
customers during migration, while EF's `ValueGenerated.Never` preserves consumer-supplied
values. The console creates drafts and performs one profile change to reach Version 2 on a
fresh database; repeat invocation leaves matching fixture values unchanged. An upgraded
database with already-matching values can remain at Version 1. This setup is not a general
idempotency protocol.

The version protocol covers coordinated profile changes only when all relevant writers
check and advance the customer version. Direct child writes do not advance it automatically;
fixture save examples are not a universally enforced aggregate protocol. Admission and
profile-input validation remain consumer policy.

## Template recipe and proofs

[DemoComposition](DemoComposition.cs) visibly registers tenancy aliases and native
DbContexts. [InventoryDbContext](Inventory/InventoryDbContext.cs) maps schema, entities,
keys/indexes, soft deletion, ownership and both save overrides. [Program](Program.cs)
selects the tenant before scoped reads and deliberately populates new rows before saving.
This is the exercised setup to copy/edit; no library registration facade or template generator.

[SalesDbContext](Sales/SalesDbContext.cs) reuses ownership registration/validation with its
own customer/address mappings and native relationship. Module-local [Inventory options](Inventory/InventoryDatabase.cs) and
[Sales options](Sales/SalesDatabase.cs) configure Npgsql and a separate `__EFMigrationsHistory`
table within each schema. Runtime DI and native design-time factories call the same options
method. Schemas express module ownership; they do not restrict the database credentials.

## Native migrations

Restore the pinned local `dotnet-ef` tool from the repository root:

```bash
dotnet tool restore
dotnet ef migrations list --project src/Gloaming.Persistence/samples/PersistenceDemo/PersistenceDemo.csproj --context InventoryDbContext --no-connect
dotnet ef migrations list --project src/Gloaming.Persistence/samples/PersistenceDemo/PersistenceDemo.csproj --context SalesDbContext --no-connect
```

For a later model change, choose a descriptive new migration name and scaffold only its
owning context. For example:

```bash
dotnet ef migrations add InventoryChange --project src/Gloaming.Persistence/samples/PersistenceDemo/PersistenceDemo.csproj --context InventoryDbContext --output-dir Inventory/Migrations
dotnet ef migrations add SalesChange --project src/Gloaming.Persistence/samples/PersistenceDemo/PersistenceDemo.csproj --context SalesDbContext --output-dir Sales/Migrations
```

Factories build a model with uninitialized tenancy, without executing the console or opening
a database. If no environment connection is supplied, they use a non-operational placeholder
for scaffolding. Actual database commands require the disposable environment connection.
Snapshots are kept beside their module migrations; review the tool's output paths as well
as its generated code.
Review generated C#, format it and apply repository semantic conventions before building;
generated code remains editable consumer code. Native context attributes select the correct
migrations/snapshot within the shared sample assembly. No separate migration projects or
runtime code generation are needed here.

Initial migrations create only the owning module's tables and indexes. Sales's incremental
`CustomerAddresses` migration adds the customer alternate key and address table/foreign key,
preserving existing E2.2 customers. Initial migrations are unchanged. The schema policy
also admits the incremental `CustomerVersions` column, which backfills Version 1 while
preserving E2.3 customer/address rows. It checks these actual relationship, key and column
operations; further operation kinds,
relationships and raw SQL require review. It does not parse SQL or promise a universal
migration-isolation checker.

## Proofs and remaining work

[Sample proofs](../PersistenceDemo.Tests/OwnershipTests.cs) run the real composition against
PostgreSQL. They cover cached-model isolation, tenantless/unestablished failures, global saves,
validation through all save overloads, foreign/missing/changed ownership, detached foreign IDs,
model classification and first/repeat console invocation. The separate GUID-key consumer in
`src/Gloaming.Persistence/tests/PersistenceTests` runs without either context library.

[Module migration proofs](../PersistenceDemo.Tests/ModuleMigrationTests.cs) initialize either
module first, verify its tables/history alone, preserve its rows when the other module is
initialized, verify independent histories and exercise Sales tenant filtering/save guards.
[Container-free model/artifact policies](../../../../tests/ArchitectureTests/ModulePersistenceTests.cs)
check both schemas, explicit entity classification, tenant-bearing relationship and snapshot/model consistency.

[Relationship proofs](../PersistenceDemo.Tests/CustomerRelationshipTests.cs) cover valid
child insertion/read and same-tenant reparenting, foreign-parent insert/update rejection
without loading the parent, child ownership validation, restricted deletion, detached
customer edits and migration from an existing E2.2 customer.

[Profile-change proofs](../PersistenceDemo.Tests/CustomerProfileTests.cs) cover caller-selected
commit visibility, stale request versions even with a fresh server context, a real second-save
constraint fault, cancellation after the first SQL write, explicit rollback/fresh recovery,
and an E2.3 database upgraded to execute the new operation.

Shared cross-module transactions require a separate named workflow and proof, as recorded in
[the E2 plan](../../../../docs/plans/e2-persistence.md). E2.2 through E2.4 establish consumer-owned
migration, relationship and transaction/version recipes. No new reusable library mechanism
was proven by those slices. PostgreSQL is the only exercised database provider.
