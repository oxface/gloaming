# Gloaming.Persistence.EntityFrameworkCore.Postgres

Optional transaction-local PostgreSQL RLS context for tenant-owned tables. It reads the
already established Gloaming Tenancy scope. Core Tenancy and ownership remain independent
of this package, PostgreSQL and consumer Access/FGA policy.

## Setup once

Keep ordinary ownership filtering/validation and select the protected tables:

```csharp
notes.HasTenantOwnership(
        row => row.Partition,
        () => RequiredPartition,
        "PartitionScope")
    .WithRls(settingName: "consumer.partition_key");
```

`RequiredPartition` reads the same operation's
`ITenantContextAccessor.Current.RequireTenant().Value`. One context uses one explicit,
qualified lowercase setting name. Supported mappings are ordinary unshared single tables
with unconverted PostgreSQL `text` or `character varying` (`varchar`) ownership. Blank-padded
`character` remains unsupported; exact trailing spaces distinguish keys. Named EF filters, ownership concurrency and
tracked-write validation stay in place. Static model validation is cached per completed
model; tenant values and services are resolved from the runtime scope.

Register the capability and install its native interceptor:

```csharp
services.AddTenantOwnershipValidation<NotesContext, string>(db => db.RequiredPartition);
services.AddPostgresTenantRowSecurity<NotesContext>();
services.AddDbContext<NotesContext>((provider, options) =>
    options.UseNpgsql(runtimeConnection).UseRls<NotesContext>(provider));
```

`UseRls` installs one scoped interceptor implementing EF's transaction and command interfaces.
It also installs the existing shared save coordinator, so a separate
`ConfigureGloamingCapabilities` call is unnecessary here. Registration and installation are
idempotent. There is no general native-interceptor registry or connection-lease state machine.

## Existing operation owner begins the transaction

```csharp
await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
// Tenant context is already established in PostgreSQL when BeginTransactionAsync returns.
var notes = await database.Notes.ToArrayAsync(cancellationToken);
await database.SaveChangesAsync(cancellationToken);
await transaction.CommitAsync(cancellationToken);
```

No explicit `EstablishTenantDatabaseContextAsync` call, additional DI scope or RLS-specific
scope is needed per query/transaction. Existing HTTP middleware admits and initializes tenancy
once per request; worker hosts do so once per operation. The existing request/operation owner
still begins and ends the native transaction. This package adds no request transaction owner.

The interceptor's `TransactionStarted`/async callback receives the native transaction before EF
publishes `CurrentTransaction`. It executes a separate parameterized
`set_config(setting, tenant, true)` using that exact native transaction. The setup refuses a
nonempty authorizing baseline and verifies the returned tenant. If setup fails/cancels, it
disposes the unpublished native transaction, rolling it back before the failed attempt returns.
The command guard checks transaction presence and exact connection/transaction identity; it
performs no setup and rewrites no SQL/results. The existing save capability rejects saves
without a caller transaction before EF can choose an implicit save-only transaction.

**All EF commands on a selected context require a transaction**, including reads, bulk/raw SQL
and queries of its global tables. Reads without a transaction fail instead of silently returning
no rows. Unselected models require neither tenancy nor a transaction. If a protected request
currently has no transaction, its existing operation owner must begin one once around the work.

Commit/full rollback ends local context immediately, even on an open connection. Each new
transaction establishes it again. Ordinary savepoints are created after setup, so rolling back
to them retains the tenant. Retry the whole failed attempt from fresh database state; do not
change tenancy or the setting behind EF. There is no in-memory readiness flag or tenant rebinding.

The [explicit utility](../../Gloaming.Tenancy/Gloaming.Tenancy.EntityFrameworkCore.Postgres/README.md)
remains independently usable for manually integrated consumers. The interceptor performs the
same local-setting primitive with EF's callback transaction; the DbContext extension cannot
be called unchanged in that callback because `CurrentTransaction` is not assigned yet.

## Versioned policies protect reads and writes

Add explicit reviewed migration operations using the actual mapped table/column and setting:

```csharp
// Up
migrationBuilder.EnableTenantRowSecurity(
    table: "notes", tenantColumn: "partition",
    settingName: "consumer.partition_key", schema: "workspace");
// Down
migrationBuilder.DisableTenantRowSecurity(table: "notes", schema: "workspace");
```

These quote identifiers and emit ENABLE/FORCE plus a `FOR ALL` policy with both clauses:

```sql
USING ("partition" COLLATE "C" = NULLIF(current_setting('consumer.partition_key', true), ''))
WITH CHECK ("partition" COLLATE "C" = NULLIF(current_setting('consumer.partition_key', true), ''))
```

USING restricts eligible stored rows for reads/updates/deletes; WITH CHECK rejects foreign
inserts and ownership changes. Exact `C` comparison preserves opaque tenant distinctions even
on case-insensitive columns. These policies apply to bulk/raw SQL and native commands using
the same transaction, including when EF filters are disabled. Ownership stays consumer-populated.

`WithRls` selects runtime setup; it does not scaffold/deploy policies. Migrations enable table RLS
permanently; the interceptor supplies the current transaction's tenant, without runtime DDL.
Automatic model-diff/snapshot scaffolding and EnsureCreated policy creation remain deferred.
The Down helper disables table RLS, so review any additional policies before using it.

## Limits and consumer obligations

Use separate migration authority and a restricted runtime login: no owner/superuser/BYPASSRLS,
privileged memberships, schema CREATE or policy-altering rights. Audit the full table inventory,
both policy expressions, table flags and extra permissive policies. Runtime setup does not
certify deployed policies/roles. Missing/empty settings deny rows and reject inserts under the
matching policies. Permission/grant checks within a tenant remain consumer-owned.

Use scoped, unpooled DbContext options and ordinary reset-enabled, non-multiplexed Npgsql.
An already-open connection is supported when this context begins the transaction. Adopted
transactions and ambient transactions are rejected; DbContext pooling, singleton/cross-operation
options, other transaction interceptors replacing/altering setup, and provider failover/execution-
strategy recovery have no support guarantee. External transaction poolers need a separate real
proxy proof; transaction-local lifetime alone is not a tested deployment guarantee.

Direct ADO.NET commands are not intercepted: use the initialized native transaction and do not
alter its setting or lifetime behind EF. A credential holder can forge the mutable custom GUC.
This is tenant isolation, not tenant authentication, membership admission, within-tenant
permissions, ownership stamping, tenant transfer or relationship enforcement. Global/transport
work needs a separately authorized context/lane.

Both capability registration and `UseRls` installation are required. Missing registration fails
at options installation; a selected save detects missing `UseRls`. Omitting all interception can
bypass application checks, so it is unsupported setup rather than a certified failure mode.

## Consumer and proofs

[Independent consumer](../tests/PostgresTests/ConsumerDatabase.cs),
[policy/role provisioning](../tests/PostgresTests/ProofDatabase.cs) and
[PostgreSQL proofs](../tests/PostgresTests/RowSecurityTests.cs) exercise this without Wholesale,
Access or HTTP dependencies. Run from the repository root:

```bash
dotnet test --project src/Gloaming.Persistence/tests/PostgresTests/PostgresTests.csproj
```

Use the container environment in the [development guide](../../../docs/development.md).
The [transaction refinement report](../../../docs/reports/a2-5-transaction-rls-simplification.md)
records fresh evidence. The [earlier session implementation](../../../docs/reports/a2-4-ownership-rls-capability.md)
and [reference research](../../../docs/reports/a2-4-rls-capability-reference-research.md) retain history.
The [optional Sales HTTP composition](../../../samples/Wholesale/Api/README.md#optional-sales-http-row-security)
adopts this capability for six finite tables under separate restricted credentials. Its
[report](../../../docs/reports/a2-6-sales-http-rls.md) records native varchar adoption and proofs.
Default Wholesale HTTP/AppHost/worker/template rollout remains separate; the earlier opt-in
CQRS executable retains explicit local setup.
