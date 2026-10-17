# A2.3: explicit Tenancy/PostgreSQL context utility

Owner selected this extraction on 2026-10-16 after the bounded A2.2 proof. The earlier
instruction to add no library interface applied to that assessment/consumer slice; the owner
now explicitly requests this optional adapter. Work continues in the existing checkout on
`feat/sales-postgres-rls`, preserving the A2.2 work and index. Commit approval is separate.

## Interface and complexity removed

Extract `TenantDatabaseContext` into `Gloaming.Tenancy.EntityFrameworkCore.Postgres`, with
one public DbContext extension:

```csharp
Task EstablishTenantDatabaseContextAsync(
    this DbContext database,
    ITenantContextAccessor tenancy,
    string settingName,
    CancellationToken cancellationToken = default);
```

The consumer begins a native transaction, calls this utility before protected SQL, then
owns queries/accepted proposals, save, commit, rollback and disposal. The host owns immutable
scope establishment and admission. Setting name is required consumer configuration; tenant
value comes only from the established accessor. Neither is interpolated into SQL.

The utility consolidates selected-tenant checks, native connection/transaction validation,
transaction-local parameterized setup and exact returned-value validation. Deleting it
would restore those mechanics at each consumer. The active A2.2 lifecycle proof, rather than
archived policy choices, supplies current transfer evidence. Remove the sample's private
copy rather than retaining a wrapper. No setting/schema/table defaults enter the library.

The package references only Tenancy core, EF Relational and Npgsql. No intermediate generic
adapter, new DI abstraction, model policy generator, automatic transaction ownership or
Access/FGA dependency is selected. Tenancy core and Persistence stay independently adoptable.

## Capability and lifetime choice

The current shared capability coordinator handles SaveChanges Prepare/Validate. RLS setup
must precede reads and native bulk SQL, so do not register it as a save callback. This
increment keeps setup explicit. Native transaction interception, if later selected, needs
its own attachment, failure cleanup, savepoint and command-before-transaction contract.

Require a selected immutable tenant and an existing open Npgsql transaction on the context's
exact connection. Missing/tenantless or invalid native setup fails before protected work.
Call once for each transaction, again after rollback before setup, and inside each new
retry/reconnect attempt. Roll back/dispose on error or cancellation; no context rebinding,
automatic retry, commit-on-dispose or ambiguous-commit policy is supplied. Scoped contexts
are not shared concurrently or across tenant operations.

RLS credentials, memberships, policies, migration/deployment and pool/session defaults remain
consumer-owned. A custom setting can still be forged by a credential holder; tenant equality
supplies no within-tenant permission or aggregate protocol.

## Executable proof and review

Adopt the extension in the Sales opt-in mode and rerun all 29 A2.2 cases plus existing CQRS
regressions. Add a plain independent PostgreSQL consumer with an alternate schema, setting
and discriminator and opaque Unicode/quote/newline key. Prove native transaction ownership,
commit/rollback, physical reuse/missing baseline, cancellation/setup failure and missing/
tenantless/configuration errors. Dependency declarations and assembly checks must prevent
mandatory Access, actor, sample or shared-save integration.

Add the test project to the active solution and Tenancy CI lane; update linked-fixture and
consumer path selection. Document guarantees/limits beside the package and report fresh
versus historical evidence in [the extraction report](../reports/a2-3-postgres-tenant-context.md).
Library interface and implementation remain available for line-by-line owner review.
