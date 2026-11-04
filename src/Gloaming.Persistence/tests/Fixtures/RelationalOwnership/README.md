# Native relational complex ownership consumer

[RelationalDocumentsContext](RelationalDocumentsContext.cs) maps required scalar records,
nested values and optional values to ordinary columns in one tenant-owned entity's table.
It also maps a separate JSON value and root JSON collection. The tenant stays a directly
mapped entity scalar; values have no entity identity or independent table. The alternate
context configures ownership before all complex values, using the same public APIs.

[RelationalOwnershipJourney](RelationalOwnershipJourney.cs) is an executable consumer with
no test-framework dependency. Both native save paths run with automatic change detection
disabled. It checks insert/reload/replacement, nullable leaves, nested-only edits, root/nested
optional absence/presence/replacement/removal, mixed JSON and JSON-only edits, named filters,
foreign insert/attached update/delete, loaded foreign nested-only edits, tenant-key mutation,
forged detached nested-only update/delete, same-owner detached delete and rollback. Forged
current-owner claims must fail through native ownership concurrency rather than an in-memory
ownership mismatch.

The [provider tests](../../PersistenceTests/RelationalOwnershipTests.cs) run the journey for
both registration orders on EF 10.0.13, Npgsql 10.0.3 and PostgreSQL 18.6. They independently
check model identity/table count and actual PostgreSQL column names, types, length and
nullability. The [model tests](../../EntityFrameworkCoreTests/RelationalMappingTests.cs) cover
nested-only detection, removed/replaced filters, disabled tokens, unsupported entity mappings
and attempted ownership inside a complex member. Existing shape/JSON tests remain separate.
The [restricted RLS tests](../../PostgresTests/RelationalRowSecurityTests.cs) cover nested-only
inline writes on a mixed entity with deployed policies and runtime credentials.

[ValueShapeJourney](ValueShapeJourney.cs) adds a native model/DDL control for required and
nullable record structs, root/inline primitive arrays and lists, root primitive JSON
collections, converted scalar payload leaves and optional values with a discriminator.
The native and guarded DDL must match. PostgreSQL proofs check replacement, in-place
mutations, optional presence/removal and foreign-write/concurrency refusal with automatic
detection disabled through both save paths and both registration orders.

[Graph regressions](../../EntityFrameworkCoreTests/MappingBoundaryTests.cs) prove that EF
accepts separate-table owned references/collections and TPH/TPT/TPC, while ownership rejects
them before or after registration, including child-only/derived-only changes. The
[independent RLS boundary tests](../../PostgresTests/MappingBoundaryTests.cs) deliberately
omit the base guard and require rejection before connection or tenant resolution. The
[native-limit controls](../../EntityFrameworkCoreTests/NativeComplexLimitTests.cs) distinguish
EF's inline-collection/nested-JSON limitations from library restrictions.

```sh
dotnet test --project src/Gloaming.Persistence/tests/EntityFrameworkCoreTests/EntityFrameworkCoreTests.csproj
dotnet test --project src/Gloaming.Persistence/tests/PersistenceTests/PersistenceTests.csproj
dotnet test --project src/Gloaming.Persistence/tests/PostgresTests/PostgresTests.csproj
```

Provider suites own disposable PostgreSQL containers/databases. `EnsureCreated` is proof setup;
applications own migrations and preservation of developer/production records. No consumer
schema or migration is changed by these fixtures. Optional complex values have required
members or discriminator markers to distinguish presence; complex collections use JSON and relationships remain
ordinary entities.

The [package-only profile](../../../../../tools/release/README.md#native-relational-complex-ownership)
copies this consumer outside the checkout and runs it against exact archives with zero
ProjectReferences, including a locked restore into a second empty cache. The delivery retains
the source, lock, source-mapped config, archive hashes and complete compatible package set.
