# E2 explicit EF tenant and module persistence

Status: overall E2 scope inventory, 2026-10-03. The owner confirmed the initial storage
approach and immutable tenant ownership, then approved the first interface for implementation.
[E2.1](e2-1-tenant-ownership.md) was owner-reviewed and checkpointed as `50e7933`;
[its report](../reports/e2-1-tenant-ownership.md) records the limited first proofs.
[E2.2](e2-2-module-migrations.md) was owner-reviewed and checkpointed as `f2dcf2b`;
[its report](../reports/e2-2-module-migrations.md) records the consumer setup and new evidence.
[E2.3](e2-3-tenant-relationships.md) was owner-reviewed and checkpointed as `d67c7fc`.
[E2.4](e2-4-versioned-profile-changes.md) adds separate version conflicts and an explicit
two-save profile operation with caller-owned commit/rollback, owner-reviewed and checkpointed
as `6069c05`. The initial supported E2 scope is complete; shared cross-module transactions
remain a separate named-workflow increment, and documented provider/bypass limits still apply.
[Its report](../reports/e2-4-versioned-profile-changes.md) records the new evidence and limits.
Read [the design](../design.md), [extraction plan](library-extraction.md) and
[design findings](../reports/e2-persistence-design.md).

## Outcome and confirmed choices

An ordinary EF consumer explicitly configures tenant ownership, validates tracked writes,
and continues using native queries, saves and transactions. The complete E2 sample will
demonstrate two module-owned DbContexts and schemas in one PostgreSQL database, with tenant-discriminated
tables. Ordinary tenant-scoped persistence rejects tenant changes; any transfer is a
separate, consumer-owned workflow.

The library removes repeated ownership-filter construction and tracked-write validation.
It does not create DbContexts, populate entities, select tenants, grant permission, open
transactions, save, commit, retry or register services automatically. Tenantless execution
does not grant access to tenant-owned rows. Global records have explicit sample policy.

Start the sample with string tenant keys from Tenancy. An independent consumer
fixture uses GUID keys and a differently named ownership property/schema to exercise
customization. Domain-ID mapping is consumer code. This proves two consumer mappings,
not multiple database providers or storage strategies.

## Archived evidence and gaps

- [Shared ownership filters](../../archive/proof-sample/shared/Persistence/ModelBuilderExtensions.cs)
  rewrite a consumer predicate for entities implementing the consumer ownership interface.
- [Inventory](../../archive/proof-sample/modules/Inventory/Inventory/Persistence/InventoryDbContext.cs),
  [Sales](../../archive/proof-sample/modules/Sales/Sales/Persistence/SalesDbContext.cs) and
  [Purchasing](../../archive/proof-sample/modules/Purchasing/Purchasing/Persistence/PurchasingDbContext.cs)
  reuse it with separate schemas. Their workflow tenant fallback rules differ; E1 replaces
  that establishment with one immutable operation identity.
- [Customer persistence tests](../../archive/proof-sample/tests/PersistenceTests/CustomerPersistenceTests.cs)
  exercise module authorization, scoped lookup, tenant uniqueness and transactional failure.
  They do not establish general detached-write protection.
- [Customer mapping](../../archive/proof-sample/modules/Sales/Sales/Customers/Persistence/CustomerConfiguration.cs)
  has an ID-only primary key; its tenant is not a concurrency predicate.
- [Order mapping](../../archive/proof-sample/modules/Sales/Sales/Orders/Persistence/SalesOrderConfiguration.cs)
  has a separate version token and ID-only customer foreign key. A version is not an ownership
  predicate; a relationship by ID alone does not prove same-tenant linkage.
- [Architecture checks](../../archive/proof-sample/tests/ArchitectureTests/ModulePersistenceRulesTests.cs)
  inspect models/migrations. Their raw-SQL schema annotation is a declaration, not SQL parsing.

No module save override or save interceptor supplying a general tenant-write guard was
found in the archive. These are source observations, not new database proof results.

## Candidate library and dependencies

First-increment project: `src/Gloaming.Persistence/Gloaming.Persistence.EntityFrameworkCore/`.

Use native EF types, without a repository, unit-of-work or base DbContext abstraction. The
first increment uses EF Core Relational for explicit relational mapping validation;
Npgsql, Testcontainers, hosting and DI stay in consumers/tests. It need not reference Tenancy:
consumers supply their current storage tenant key. Ordinary EF adoption requires no template or Access.

E2.1 pins EF 10.0.12/Npgsql 10.0.3 after audited restore. PostgreSQL is the first exercised
provider; broader compatibility is not claimed.

## Proposed interface

The two utilities below were approved, reviewed and checkpointed in E2.1.
Later capabilities in this document remain proposals:

```csharp
EntityTypeBuilder<TEntity> HasTenantOwnership<TEntity, TTenant>(
    this EntityTypeBuilder<TEntity> entity,
    Expression<Func<TEntity, TTenant>> tenantProperty,
    Expression<Func<TTenant>> requiredTenant,
    string filterName)
    where TEntity : class
    where TTenant : notnull;

void ValidateTenantChanges<TTenant>(
    this DbContext context,
    Func<TTenant> requireTenant)
    where TTenant : notnull;
```

`HasTenantOwnership` is explicitly called in consumer model configuration. It identifies
one mapped, non-null ownership property, records ownership metadata, constructs a named
equality filter and marks that property as a native concurrency token. Document all effects.
It does not discover/register entities, select a schema, choose keys, create relationships
or populate values.

The `requiredTenant` expression references the actual DbContext instance. Never evaluate
it while constructing the model or capture the first operation's tenant as a constant.
Test cached-model reuse across tenants. The consumer property rejects uninitialized or
tenantless execution when a tenant-owned query needs its key.

`ValidateTenantChanges` explicitly detects changes, even when automatic detection is disabled,
then inspects registered ownership metadata. Resolve the tenant only when tenant-owned
writes exist. Compare applicable current/original values using EF property comparison semantics:

| State | Required behavior |
| --- | --- |
| Added | Supplied owner exists and matches the operation tenant. Never fill or correct it. |
| Modified | Current and original owner both match; reject ownership modification. |
| Deleted | Applicable current/original owner matches; reject forged foreign original values. |
| Unchanged | No save validation claim; this helper is not read authorization. |
| Global | Apply consumer policy; do not infer ownership from property names. |

Check ownership/filter/concurrency configuration before accepting protected writes. Reject
unsupported key-type combinations instead of silently converting them. Proposed typed
failures distinguish foreign ownership, ownership changes and invalid configuration. Enum
reasons have explicit numbers from 1. Do not turn native/provider concurrency failures into
a misleading membership verdict.

## Detached writes and concurrency

A detached entity may carry the current tenant key but target a foreign row by ID. A
tracked-value check alone cannot establish the stored owner. Native ownership concurrency
predicates should make UPDATE/DELETE compare original owner and key; a foreign target then
affects no row and produces native concurrency failure. This needs real PostgreSQL proof.

The guard rejects a forged foreign original owner before saving. The predicate checks the
actual stored target. Consumers may also use tenant-bearing composite primary keys; this
helper does not mandate a key shape.

A stable tenant token does not detect competing edits inside one tenant. The sample adds
and explicitly advances a separate native version token. The utility generates no versions
and retries nothing. See [native concurrency semantics](https://learn.microsoft.com/en-us/ef/core/saving/concurrency).

## Explicit consumer wiring

Illustrative consumer; its entity/schema/key/index/relationship mapping stays visible:

```csharp
internal sealed class InventoryDbContext(
    DbContextOptions<InventoryDbContext> options,
    ITenantContextAccessor tenancy) : DbContext(options)
{
    private string RequiredTenantKey => tenancy.Current.RequireTenant().Value;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("inventory");
        // Explicit entity/property/key/index/relationship configuration here.
        modelBuilder.Entity<StockItem>().HasTenantOwnership(
            item => item.TenantKey,
            () => RequiredTenantKey,
            filterName: "TenantScope");
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        this.ValidateTenantChanges(() => RequiredTenantKey);
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        this.ValidateTenantChanges(() => RequiredTenantKey);
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}
```

These are editable template/sample overrides, not a library base class. Prove every native
save overload reaches validation. Explicit guard calls are another composition, but the
tracked-write guarantee requires complete wiring. The utility does not save independently.

Consumers register DbContexts through ordinary EF/DI, select schema/history names, and
establish identity before tenant operations. They own `BeginTransaction`, `SaveChanges` and
`Commit`. Disposal never commits. No custom persistence scope is justified by this proposal.

## Relationships and supported surface

Use tenant-owned roots and separately mapped child entities. Explicit composite alternate/
primary keys and foreign keys containing tenant identity must reject cross-tenant parent
links at the database, including detached IDs without loaded navigations. Do not add
cross-module EF relationships; collaboration uses Contracts.

Sample model tests classify every mapped entity as global or tenant-owned; a forgotten
registration fails the tests. Unregistered entities are not implicitly protected. Check
schemas, migrations/history, tenant uniqueness and relationship constraints with negative
fixtures and consumer-selected alternative names.

Start with single-table entities and separately mapped children. Inheritance, EF owned
types, table splitting and complex types need dedicated design/proofs before support.
Reject unsupported tenant-ownership mappings rather than provide partial protection. An
owned child is not assumed safe just because its root has a filter.

Filtered LINQ reads and completely wired tracked saves are the initial supported surface.
Filter disabling, raw SQL, bulk updates/deletes and privileged maintenance are separate
consumer responsibilities. Bulk operations bypass the tracker/save validator; an ownership-
changing bulk setter is outside this contract. See [native bulk operation semantics](https://learn.microsoft.com/en-us/ef/core/saving/execute-insert-update-delete).

Privileged work uses a fresh, separately composed context, never mutable identity or an
ambient bypass flag. Identity-map lookups/navigation fixup can return tracked objects; a
context deliberately tracking foreign data is not an ordinary isolated reader. Demonstrate
disabling an independent soft-delete filter while keeping tenancy enabled.

## Sample and implementation increments

Keep the DI-only ContextDemo as E1's independent identity proof. Add a finite PersistenceDemo
against PostgreSQL, with module-owned Inventory/Sales persistence, reference items, customers
and a child relationship. Include only behavior needed for the proofs. Document active module
ownership/domain language with implementation; do not inherit historical charters wholesale.
No HTTP endpoints or fake production authentication are added.

Split the work into coherent runnable increments if needed:

1. Utilities, minimal executable consumer, model tests and PostgreSQL isolation/write/
   forged-detached-target proofs together.
2. Complete two-module composition, migrations, child constraints, concurrency/rollback
   and schema/history checks while preserving the first runnable outcome.
3. A separately designed shared-transaction workflow/proof later, with native connection/
   transaction enlistment. Owning-module atomicity implies no cross-module atomicity.

The owner approved standard development-time `dotnet ef migrations add` scaffolding.
Generated migrations remain consumer-owned, ordinary C# reviewed with their owning module;
no runtime code generation is introduced.

## Proof matrix

| Area | New proof required |
| --- | --- |
| Reads | Same business key in two tenants gives independently expected results; foreign IDs are absent. |
| Missing context | Uninitialized/tenantless operations cannot query or save tenant-owned data. |
| Global records | Explicit tenantless global policy works without granting tenant access. |
| Model reuse | Sequential/overlapping scopes share a model but retain their own tenant; fresh scopes retain no old key. |
| Inserts | Valid owner saves; foreign/missing owner fails; no automatic value population. |
| Updates/deletes | Foreign entries, original/current mismatch and owner changes fail; every native save overload validates. |
| Detached target | Current tenant plus foreign ID cannot update/delete it; foreign original owner is rejected; fresh reads show unchanged foreign data. |
| Relationships | Cross-tenant parents fail through actual constraints, including detached references. |
| Competing writes | Separate versioned updates produce the declared native conflict without automatic retry. |
| Failure/rollback | Failed later participant leaves no earlier changes after explicit rollback; a fresh scope can succeed. |
| Filter composition | Soft deletion coexists; disabling it retains tenancy; deliberate tenancy bypass is identified as such. |
| Configuration | Missing registration, invalid ownership/filter/token metadata and unsupported mappings are diagnosed. |
| Module ownership | Tables, migrations/history and relationship targets obey chosen schemas; negative violations are detected. |
| Customization | GUID ownership/different property/schema fixture adopts the utility without Tenancy or Access. |
| Dependencies | No sample, web, transport, Aspire or Access dependency; identity core stays package-free. |
| Migrations | Empty-database migration and repeat invocation succeed with stable module histories. |

Add an active PostgreSQL CI lane during implementation. Fast checks stay container-free;
document local container setup. Archived test results do not satisfy new proof obligations.

## Exit and limits

Review the interface, consumer obligations and failure matrix before implementation, then
review code/proofs line by line. Leave changes unstaged; commit approval remains separate.
Completion requires real consumer use and these supported-operation proofs.

This is application-level protection for explicit supported wiring, not a hostile-code
boundary, RLS, database-per-tenant support or a SQL inspection engine. Authentication,
membership, actor requirements, business validation, audit and transaction ownership remain
consumer policy or later capabilities. No new reusable mechanism is proven by this plan.
