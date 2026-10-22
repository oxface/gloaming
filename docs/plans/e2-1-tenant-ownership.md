# E2.1 explicit EF tenant ownership

Status: owner-reviewed and checkpointed as `50e7933` on 2026-10-04, including the test audit.
The owner approved this interface
and scope after checkpoint `c8cbf64`, allowing revisions consistent with prior decisions,
and allowed native development-time EF migration scaffolding. [The complete E2 plan](e2-persistence.md) remains the scope inventory; this
increment makes the first supported mechanism small enough for line-by-line review.

## Outcome

One opt-in EF utility library, one executable Inventory consumer, container-free model/
validation tests and real PostgreSQL proofs of filtered reads and tracked writes. Neither
actor identity nor membership is required. Keep the existing ContextDemo unchanged.

Implemented project: `ModulithFoundry.Persistence.EntityFrameworkCore`. Reference native EF
Core Relational explicitly so unsupported relational mappings can be inspected and rejected;
Npgsql stays in the consumer. No Tenancy, ActorIdentity, Access, hosting or DI dependency.
PostgreSQL is the first proven provider, not a claim of provider-independent behavior.

## Interface for review

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

Model registration explicitly configures a non-null, consumer-populated ownership property,
a named equality filter, ownership metadata and a native ownership concurrency token.
Ownership uses `ValueGenerated.Never` and before-save behavior `Save`; it is not generated
or silently populated. The supplied key expression must refer
to the context instance; model construction must not evaluate its value. All consumer
entities/properties, keys, schema, indexes and native registration remain visible.

Validation explicitly detects changes, inspects protected Added/Modified/Deleted entries,
and compares owner values against the operation key. It resolves that key only when a
protected write exists. Globals do not demand a tenant. Require one ownership key type per
validator invocation; unsupported configurations fail rather than silently escape inspection.

For Added, require a supplied matching owner. For Modified/Deleted, current and original
owner must match the operation and ownership must not change. Distinguish changing the owner
value from EF's modified flag: ordinary `Update(detachedEntity)` may mark an unchanged owner
modified, and must remain usable. Expose structured failures for absent/foreign ownership,
ownership changes and invalid configuration, with explicit enum values starting at 1.
Native database concurrency failures stay `DbUpdateConcurrencyException`.

The native ownership token supplies the stored-row predicate: a detached row with the
current tenant key and a foreign ID must affect zero rows. Both in-memory validation and
the database predicate are necessary. A stable ownership token does not detect concurrent
edits inside the same tenant; consumers configure a separate version when needed.

## Consumer and first supported surface

Add a finite `src/Gloaming.Persistence/samples/PersistenceDemo` with one Inventory-owned table in an
explicit schema. Its reference rows have an ID, consumer-named tenant key, business key,
quantity and an independent soft-deletion flag. Independently seed two tenants with the same
business key and quantities 42/7; do not inherit archived entity types or migrations.

Use the Tenancy accessor in this sample alone; the utility accepts its native storage key.
A separate test consumer uses GUID owners and a different property/schema without Tenancy.
The console receives a consumer-supplied PostgreSQL connection, runs finite scoped operations
and prints business results without credentials. Tests own disposable PostgreSQL instances.

The consumer overrides native `SaveChanges(bool)` and `SaveChangesAsync(bool, token)` to
call validation before the native base implementation. Every public save overload needs
proof. Saves and transaction completion stay caller-controlled. Use explicit native
initialization for the first fixture; reviewed module migrations and repeatable migration
execution belong to the following E2 increment. Native migration scaffolding is allowed.

Initial model: ordinary single-table entities with explicitly mapped scalar ownership.
Inheritance, owned entities, shared-table mappings, complex ownership paths and unsupported
key mapping/conversion combinations require dedicated proofs before support. Reject invalid
protected metadata, overwritten ownership filters or disabled ownership concurrency tokens
before accepting protected writes. An unregistered entity receives no implied protection;
consumer model tests explicitly classify every table.

Supported reads use valid configured named filters and ordinary LINQ. The same cached model
must read the tenant from each current context instance. Disabling soft deletion should
retain tenancy. `IgnoreQueryFilters`, raw SQL, bulk writes and contexts already tracking
foreign data remain explicit consumer responsibilities, not claims of this increment.

## Required first-increment proofs

| Area | Required independent result |
| --- | --- |
| Tenant reads and model reuse | Same business key returns 42 or 7 by selected tenant; foreign IDs absent; shared cached model does not retain the first tenant. |
| Establishment | Uninitialized/tenantless consumer cannot query or save owned rows; global-only save avoids resolving tenancy. |
| Added owner | Correct owner succeeds; null/foreign owner rejected without stamping it. |
| Tracked owner | Foreign current/original owner and ownership changes rejected, including manual modification with automatic change detection disabled. |
| Detached target | Current tenant plus foreign ID cannot update/delete the stored foreign row; fresh reads confirm unchanged data. Same-tenant detached writes succeed. |
| Native save surface | Parameterless/bool sync and token/bool+token async saves all reach validation. |
| Filter composition | Soft-deletion filtering composes; ignoring only that named filter retains tenancy. |
| Invalid configuration | Invalid ownership property, overwritten filter, disabled token and unsupported mappings fail predictably. |
| Independent consumer | GUID owner/different property/schema works without either context library or Access. |
| Explicit control | Native saves and transaction ownership remain visible in consumer code. This utility owns no transaction protocol; a dedicated native rollback demonstration is not maintained after the test audit. |

The following increment adds the second module/schema, separately mapped children and
same-tenant relationship constraints, module migration histories, separate version conflicts
and richer transactional failure cases. A shared cross-module transaction remains its own
later workflow/proof; none is implied by this increment.

## Evidence and handoff

EF documents context-instance query filters, named-filter composition and native concurrency
predicates. See [query filters](https://learn.microsoft.com/en-us/ef/core/querying/filters),
[concurrency](https://learn.microsoft.com/en-us/ef/core/saving/concurrency) and
[value comparers](https://learn.microsoft.com/en-us/ef/core/modeling/value-comparers).
Installed EF 10.0.12 XML confirms the named-filter and metadata APIs. PostgreSQL 18.6 is
available locally. These are API/source observations, not new library guarantees.

The initial implementation and its 61-test result are recorded in [the E2.1 report](../reports/e2-1-tenant-ownership.md).
[The subsequent test audit](../reports/test-audit.md) removes redundant/framework-only coverage
and records the current 50 E2.1 tests without changing runtime behavior.
They prove the first supported ownership mechanism, not the later E2 capabilities. Review the
public signatures, model effects, implementation and executable consumers line by line.
All new changes remain unstaged; this increment has not been committed.
