# Tenant ownership example

Two native DbContexts own separate schemas and migration histories in one PostgreSQL database.
Each uses the optional ownership mapping and explicit native save validation, with a scoped
Tenancy accessor. The application supplies owner values, migrations, transaction boundaries,
SaveChanges and commits. It references neither ActorIdentity nor the Wholesale application.

```bash
export WHOLESALE_DEMO_CONNECTION_STRING='<disposable PostgreSQL connection string>'
dotnet run --project src/Gloaming.Persistence/samples/PersistenceDemo/PersistenceDemo.csproj
```

[Program](Program.cs) explicitly migrates Inventory and Sales, then creates or reads one row
per module for each of two selected tenants. Repeated execution retains those rows. No
transaction spans the two contexts; the same native cached model reads the current scope's
tenant through the mapped accessor. Quantities/names are demonstration data, not a stock or
customer management feature.

[InventoryDbContext](Inventory/InventoryDbContext.cs) shows an owned row, an independent
soft-deletion filter and a global table. [SalesDbContext](Sales/SalesDbContext.cs) also contains
a same-tenant relationship and native optimistic version field. Their existing migration chains
remain intact; the focused CLI does not run the older customer/profile update journey.

The [adoption tests](../../tests/OwnershipAdoptionTests/PersistenceDemo.Tests.csproj) own
stored-owner forgery, all native save overloads, detached updates/deletes, global data,
same-tenant relationship constraints, independent migration order and actual repeated CLI runs.
The older [two-save profile operation](../../tests/OwnershipAdoptionTests/Fixtures/CustomerProfileChanges.cs)
is a transaction fault fixture, retaining rollback, stale reload, cancellation and fresh recovery.
It is consumer policy, not a Gloaming transaction runtime.

```bash
dotnet test --project src/Gloaming.Persistence/tests/OwnershipAdoptionTests/PersistenceDemo.Tests.csproj
```

These tests use disposable PostgreSQL containers. See the [ownership contract](../../Gloaming.Persistence.EntityFrameworkCore/README.md)
for stored-owner concurrency, native SQL/bulk bypasses and supported mappings. RLS remains a
[separately selected adapter](../../Gloaming.Persistence.EntityFrameworkCore.Postgres/README.md)
with consumer-provisioned policies, grants and transaction discipline.
