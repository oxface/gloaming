# A2.5: automatic transaction-local RLS context

Implemented for owner review on 2026-10-16. The owner requested simplifying the
[A2.4 session/interceptor implementation](a2-4-ownership-rls-capability.md), preferred EF's
transaction interceptor, and explicitly selected keeping transaction ownership with the
existing operation/request owner, including protected reads. The existing staged review
snapshot is preserved; this refinement is unstaged.

## Result

Declare ownership and the existing per-table RLS opt-in, then register/install once:

```csharp
entity.HasTenantOwnership(row => row.OrganizationKey, () => RequiredOrganizationKey, "OrganizationScope")
    .WithRls(settingName: "app.tenant_key");

services.AddTenantOwnershipValidation<OrdersContext, string>(db => db.RequiredOrganizationKey);
services.AddPostgresTenantRowSecurity<OrdersContext>();
services.AddDbContext<OrdersContext>((provider, options) =>
    options.UseNpgsql(runtimeConnection).UseRls<OrdersContext>(provider));
```

Existing operation work now needs only its ordinary transaction:

```csharp
await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);
// Local tenant context is established before the transaction is returned to this caller.
var rows = await database.Orders.ToArrayAsync(cancellationToken);
await database.SaveChangesAsync(cancellationToken);
await transaction.CommitAsync(cancellationToken);
```

No explicit tenant-database setup call or extra RLS scope is needed. Existing HTTP middleware
already admits/initializes immutable tenancy once per request; workers establish each operation.
The current Sales read paths do not universally begin transactions. A selected RLS read lane
must have its existing owner begin one around the work; no central request transaction owner
or middleware reordering is added in this increment.

| Earlier implementation | Current implementation |
| --- | --- |
| Generic native interceptor factory/requirement registry | Removed; direct feature-specific `UseRls` installation. |
| Shared EF command registration guard / Relational dependency | Removed; shared EF returns to its original save-only interface and EF Core/DI dependencies. |
| Separate connection interceptor, command interceptor and session coordinator | One scoped native transaction interceptor implementing the command interface too. |
| Per-connection lease/readiness table | Removed; require the owning EF transaction and reject adopted transactions. |
| Session-level `set_config(..., false)` | Transaction-local `set_config(..., true)` after each native begin. |
| Repeated model inspection per SQL command | Cached static selection/validation per completed model. |

The remaining command callbacks check presence and exact identity of the current native
transaction; they perform no tenant setup or SQL rewriting. The existing save capability
also rejects saves without an explicit caller transaction before EF can choose an implicit
save-only transaction. Unselected models need neither tenancy nor a transaction.

## Review-worthy implementation

- [Registration and options](../../src/Gloaming.Persistence/Gloaming.Persistence.EntityFrameworkCore.Postgres/PostgresTenantRegistration.cs):
  one idempotent scoped registration, existing save validation binding, direct native installation.
  `UseRls` also installs the existing save coordinator; no additional configuration call is needed.
- [Native interceptor](../../src/Gloaming.Persistence/Gloaming.Persistence.EntityFrameworkCore.Postgres/PostgresTenantInterceptor.cs):
  synchronous/asynchronous transaction-started setup using the callback's native transaction,
  failed-setup disposal, adopted-transaction rejection and native command checks.
- [Model/policy helpers](../../src/Gloaming.Persistence/Gloaming.Persistence.EntityFrameworkCore.Postgres/TenantRowSecurityExtensions.cs):
  retained EF ownership/filter metadata, ordinary save requirement and static model selection cache.
  Explicit ENABLE/FORCE and USING/WITH CHECK policy helpers remain unchanged in purpose.
- [Independent consumer](../../src/Gloaming.Persistence/tests/PostgresTests/ConsumerDatabase.cs) and
  [proofs](../../src/Gloaming.Persistence/tests/PostgresTests/RowSecurityTests.cs): ordinary DI scopes,
  caller transactions and business calls without explicit database-context establishment.

EF invokes TransactionStarted before assigning CurrentTransaction. Therefore the interceptor
uses the native callback argument, rather than calling the public DbContext extension unchanged.
This is the same parameterized local-setting primitive as
[EstablishTenantDatabaseContextAsync](../../src/Gloaming.Tenancy/Gloaming.Tenancy.EntityFrameworkCore.Postgres/TenantDatabaseContextExtensions.cs),
which remains independently usable and unchanged. On setup failure, the caller has not received
an EF transaction object; disposing the newly started native transaction rolls back the attempt.
An adopted transaction is rejected without disposing it because its original caller still owns it.
The native callback is documented in [EF interception](https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/interceptors);
the ordering is visible in [EF 10.0.12 source](https://github.com/dotnet/efcore/blob/001bfac21c1910cdf1110ca256c9ea0a06f374ca/src/EFCore.Relational/Storage/RelationalConnection.cs#L332).
PostgreSQL defines the local setting/rollback behavior in [SET](https://www.postgresql.org/docs/18/sql-set.html).

## Protection and limits

| Condition | Current outcome |
| --- | --- |
| Begin a selected native EF transaction | Establish admitted tenant automatically before business SQL and EF savepoints. |
| SELECT / tracked INSERT, UPDATE, DELETE / bulk or raw EF SQL | Require that transaction; matching PostgreSQL policies enforce tenant equality. |
| Foreign stored row / changed or inserted foreign owner | USING suppresses foreign update/delete targets; WITH CHECK rejects foreign row results. |
| Query filters disabled / direct ADO.NET on the same transaction | Database policies continue to apply independently of EF tracked-write validation. |
| No owning transaction | EF commands and selected saves reject; no silent empty read or implicit save-only fallback. |
| Commit/full rollback with the connection still open | Local context ends immediately; a fresh transaction establishes again. |
| Ordinary savepoint rollback | Setup precedes the savepoint, so tenant context remains. |
| Failed or cancelled setup | Newly started native transaction is disposed; no published EF transaction or protected SQL follows. |
| Already-open connection | Supported when this DbContext begins the transaction; connection opening no longer establishes context. |
| Adopted or ambient transaction / unsafe pooling modes | Rejected; no new support guarantee. |
| Missing capability registration / missing UseRls | Options installation fails without registration; selected saving detects omitted UseRls. Omitting the entire integration remains unsupported. |
| Policy absence, privileged role, extra permissive policies or forged custom GUC | Not certified/contained by interception; production policy and credential audits remain consumer-owned. |

Migrations keep table RLS enabled; runtime interception supplies transaction context only.
Policy scaffolding remains explicit. Exact text-key comparison, non-owner runtime role proofs
and both policy clauses remain. There is no mandatory Tenancy-to-Access/FGA dependency.
Within-tenant permissions, tenant-inclusive relationships, ownership stamping and maintenance/
global/transport lanes remain consumer policy. Native commands must not change settings or
transaction lifetime behind EF. External poolers/provider failover need separate real proofs.

## Verification and extraction outcome

Fresh focused PostgreSQL/declaration suite: **30 passed, zero skipped**. It covers sync/async
begin/save paths and RETURNING, cached model reuse, same-backend Alpha/Beta/Alpha reuse with
prepared statements, immediate commit/rollback cleanup, savepoints, actual native transaction
identity, database read/write policies, missing transactions/context/installation, cancelled
setup disposal, role defaults, adopted ownership and case-insensitive column isolation.
The cancellation proof also observes that EF has not published CurrentTransaction in the callback.

Fresh affected regression checks also passed: ownership/model 23, Persistence PostgreSQL 6,
EventReactions core 21, independent EventReactions consumer 104, Wholesale CQRS 62, explicit
Tenancy/PostgreSQL utility 11 and architecture 96. Including the focused suite, **353 native
tests passed, zero skipped**. Earlier A2.4's 28 proofs and 351-test aggregate remain historical.

The active solution built with zero warnings/errors using the per-invocation
`-p:NuGetAudit=false` workaround for the previously observed unrelated MessagePack advisory
recorded in A2.4; no dependency versions or repository audit configuration changed. Full active
style/analyzers passed; CSharpier checked 789 files including generated source. Archive validation
preserved all 800 original files. Local documentation validation resolved 658 links/anchors across
25 changed Markdown files without errors; whitespace checks passed.

The existing T1 proof passed with two external generated consumers, five PostgreSQL adoption
tests each, builds, style/analyzers and creation/provenance/failure/race checks. This verifies
compatibility of the restored shared EF snapshot without adding an RLS template preset.
The dedicated disposable server was removed. The original index and staged diff are unchanged;
all refinement edits remain unstaged.

The refined reusable mechanism is proven for this bounded native transaction composition;
no new generic interception or request/transaction execution framework is introduced. Library
interfaces and implementation remain subject to owner line-by-line review. This increment
changes no default Sales HTTP/AppHost/worker adoption or template preset. Existing Sales
RLS usage keeps its explicit A2.3 protocol; template compatibility is a separate verification.
