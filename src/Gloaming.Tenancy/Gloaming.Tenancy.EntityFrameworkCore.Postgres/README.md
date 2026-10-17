# Explicit PostgreSQL tenant context

An optional utility connecting an established Gloaming tenant to a PostgreSQL custom setting
inside the caller's native EF transaction. Dependencies are Tenancy core, EF Core Relational
and Npgsql. There is no dependency on Access, ActorIdentity, Persistence, shared save
capabilities, ASP.NET Core or the sample module layout. Tenancy core remains package-free.

## Consumer usage

Use the tenant accessor and scoped DbContext from the same immutable operation. Existing
host registration supplies those objects; this utility adds no DI registration or interceptor.
Choose the custom setting name explicitly and use that same name in consumer-owned policies:

```csharp
using Gloaming.Tenancy.EntityFrameworkCore.Postgres;

await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
await database.EstablishTenantDatabaseContextAsync(
    tenancy,
    settingName: "my_application.tenant_key",
    cancellationToken: cancellationToken);

// Protected queries; accepted changes and SaveChanges when required.
await transaction.CommitAsync(cancellationToken);
```

Call after beginning each transaction and before its first protected read/write, including
read-only transactions. There is one public extension, implemented in
[`TenantDatabaseContextExtensions`](TenantDatabaseContextExtensions.cs). It:

1. Requires an initialized, selected tenant from `ITenantContextAccessor.Current.RequireTenant()`.
2. Requires an existing native Npgsql transaction on the context's exact open connection.
3. Executes `SELECT set_config(@setting, @tenant, true)` with both inputs parameterized.
4. Checks that PostgreSQL returned the exact opaque tenant value; failures propagate.

The setting name is required: null, blank or unqualified names such as `role` are rejected.
The name must contain a dot; PostgreSQL validates its full custom-setting syntax. No Sales
setting, schema, table, column, tenant encoding or UUID parsing is implicit. Tenant values
are preserved exactly, without trimming or case conversion. Configuration names are trusted
consumer configuration, not request input.

The caller authenticates identities, authorizes mandates, admits tenancy, creates/disposes the operation
and DbContext, begins the transaction, saves, commits or rolls back, and owns retries.
Missing tenancy is not an empty/global tenant; deliberate tenantless work requires a
separately authorized path. Do not reuse a DbContext across tenant operations or share it
concurrently. The utility reads the supplied accessor; it cannot prove that an unrelated
accessor and DbContext were obtained from the same operation.

## Lifetime and failures

| Condition | Result / caller obligation |
| --- | --- |
| Missing/disposed accessor context | Existing `InvalidOperationException` / `ObjectDisposedException` propagates before SQL. |
| Deliberate tenantless context | `TenantRequiredException`; no unrestricted setting is supplied. |
| No current transaction, wrong native connection/transaction or closed connection | `InvalidOperationException`; the utility opens/starts nothing. |
| Invalid setting argument | Native argument exception before setup. Further PostgreSQL name/configuration errors propagate. |
| SQL failure or cancellation | Abort the attempt; roll back/dispose and use fresh database state for retry. No fallback context is selected. |
| Commit or full rollback | PostgreSQL ends the local setting, revealing any earlier session value. Every new transaction needs setup again. |
| Savepoint rollback before setup | Setup is undone. Establish again before protected SQL; setup before ordinary EF savepoints is preferable. |
| Retry or reconnect | Begin and establish inside each whole-transaction attempt using the operation's fixed tenant facts. No retry/idempotency/ambiguous-commit handling is supplied. |

The extension is async only. It installs no automatic transaction/command interception and
does not participate in the Prepare/Validate save phases: reads and bulk SQL need context
before SaveChanges. It neither disposes nor commits the caller's transaction. Unrelated native
transaction sharing, ambient transactions, DbContext pooling, multiplexing and external
poolers have no new support guarantee here.

See [PostgreSQL transaction-local configuration](https://www.postgresql.org/docs/18/functions-admin.html#FUNCTIONS-ADMIN-SET)
and [SET/savepoint lifetime](https://www.postgresql.org/docs/18/sql-set.html).

## RLS remains consumer-owned

Setting context does not enable or prove RLS. Consumers own versioned policy deployment,
USING/WITH CHECK, the complete protected-table inventory, tenant-inclusive relationships,
actual runtime roles/memberships/privileges and separate migration authority. An absent or
disabled policy, an additional permissive policy, an unprotected table or a bypass role can
invalidate the intended isolation. This utility does not inspect those facts.

Use reset-enabled physical connections and no authorizing session/role defaults; local
settings can reveal an earlier session setting when they end. This utility does not validate
pool options or create a database-wide denial protocol. The Sales composition separately
checks its runtime configuration. Native SQL and bulk DML on the same transaction receive
the database setting, but still need actual policies and business-specific protocols.

A custom setting is mutable trusted input: a credential holder can forge another tenant.
RLS tenant equality does not authenticate, admit membership, authorize resources within a
tenant, validate aggregates or retract data already in memory. Read the
[A2.2 protection matrix](../../../docs/reports/a2-2-sales-postgres-rls.md) for exercised bypass
controls and [primary-source evidence](../../../docs/reports/tenant-authorization-primary-sources.md).

## Executable consumers and proofs

- [Independent PostgreSQL consumer](../tests/TenancyPostgresTests/ContextTests.cs): a plain
  DbContext, alternate schema/discriminator/setting, actual restricted login and unusual opaque
  key; commit/rollback, physical pool reuse, cancellation, missing requirements and server failure.
- [Sales executable](../../../samples/Wholesale/CqrsDemo/TenantIsolation/README.md): adopts this
  extension in its explicit opt-in RLS composition. Its existing role/permission/raw/bulk/
  savepoint/retry/reconnect/COPY proofs now cross the public utility.
- [Extraction scope](../../../docs/plans/a2-3-postgres-tenant-context.md) and
  [fresh report](../../../docs/reports/a2-3-postgres-tenant-context.md) distinguish the reusable
  setup mechanism from consumer-owned policies and unsupported integration.

```bash
dotnet test --project src/Gloaming.Tenancy/tests/TenancyPostgresTests/TenancyPostgresTests.csproj
```

This suite runs in the Tenancy CI lane using PostgreSQL 18.6 Testcontainers. On the repository's
rootless Podman workstation, use the socket environment in the
[development guide](../../../docs/development.md#postgresql-tenant-context-utility).

The later [A2.4 capability research](../../../docs/reports/a2-4-rls-capability-reference-research.md)
compares Bytefish's connection hook and Marten's fluent/schema/runtime split with an optional
`HasTenantOwnership(...).WithRls()` integration. The separate optional
[Persistence/PostgreSQL adapter](../../Gloaming.Persistence/Gloaming.Persistence.EntityFrameworkCore.Postgres/README.md)
now supplies that fluent opt-in with automatic transaction-local setup. This package retains its explicit
transaction-local contract and independence from ownership mapping; do not mix lifetime modes.
