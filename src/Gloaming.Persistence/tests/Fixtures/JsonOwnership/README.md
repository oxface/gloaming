# Native typed JSON ownership consumer

[JsonDocumentsContext](JsonDocumentsContext.cs) is an independent consumer with three JSON
columns on one ordinary tenant-owned entity. It uses init-only records, private parameterless
constructors for complex-containing records, `List<T>` declarations, optional nested selections,
two nested complex collections, a root collection and explicit nullable string-enum conversion.
Collection initializers are empty; row creation explicitly supplies initial defaults.

Both native saves invoke `ValidateTenantChanges`. The
[provider tests](../../PersistenceTests/JsonOwnershipTests.cs) exercise replacement and in-place
JSON-only edits after reload, owner/key mutations, foreign tracked writes, forged detached IDs,
filter composition, deletes and rollback. The [model tests](../../EntityFrameworkCoreTests/UnsupportedMappingTests.cs)
retain inheritance/split/owned/non-JSON/mixed-shape rejection and prove native constructor and
read-only-collection limitations. The [RLS tests](../../PostgresTests/JsonRowSecurityTests.cs)
exercise nested JSON edits with restricted credentials, deployed policies and both native save paths.

```sh
dotnet test --project src/Gloaming.Persistence/tests/EntityFrameworkCoreTests/EntityFrameworkCoreTests.csproj
dotnet test --project src/Gloaming.Persistence/tests/PersistenceTests/PersistenceTests.csproj
dotnet test --project src/Gloaming.Persistence/tests/PostgresTests/PostgresTests.csproj
```

Provider suites create/remove their own PostgreSQL 18.6 containers. No developer data is used.
The [independent journey](JsonOwnershipJourney.cs) repeats recovery, sync/async writes, optional
values, root collections, tenant isolation/concurrency and rollback without a test framework.
The [local package proof](../../../../../tools/release/README.md#native-json-ownership) copies
that consumer outside the repository and compiles/runs it against exact archives, including a
second empty-cache locked restore. `EnsureCreated` is explicit disposable proof setup; applications
own production migrations and preservation of existing data. No Umbrine DTOs or schemas are imported.
