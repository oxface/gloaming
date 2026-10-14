# Explicit EF tenant ownership

An opt-in EF Core Relational utility with no actor-identity, tenancy, Access, provider,
web or hosting dependency. PostgreSQL is the first proven provider; the consumer supplies
native storage keys, entities, schema, mappings, registration and saves.

## Explicit model configuration

```csharp
stock.HasTenantOwnership(
    row => row.OrganizationKey,
    () => RequiredOrganizationKey,
    filterName: "OrganizationScope");
```

The selected property must already be mapped as a direct CLR property. The required-key
expression must read a property or field of the DbContext instance, typically a private
property that resolves the consumer's selected tenant. Capturing a local key constant is
rejected. Model construction never resolves the key; cached models use each current context.

This call configures a non-null, consumer-populated property with `ValueGenerated.Never`,
before-save behavior `Save`, a named equality filter, ownership annotations and a native
concurrency token. It returns the native builder. Filter names, entity registration, keys,
relationships and schema stay
consumer choices. Ownership is not filled or corrected by the utility.

## Explicit save wiring

```csharp
public override int SaveChanges(bool acceptAllChangesOnSuccess)
{
    this.ValidateTenantChanges(() => RequiredOrganizationKey);
    return base.SaveChanges(acceptAllChangesOnSuccess);
}

public override Task<int> SaveChangesAsync(
    bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
{
    this.ValidateTenantChanges(() => RequiredOrganizationKey);
    return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
}
```

Keep the operation key fixed for the DbContext lifetime. Use the same key source for model
filtering and validation; the library cannot prove that two arbitrary consumer getters
describe the same operation. These two native
overrides cover all four public save overloads in the exercised consumer. Explicit validation
calls are also possible, with the consumer responsible for every supported write path.

The validator detects changes even when automatic detection is disabled. For protected
Added rows the supplied owner must match; for Modified/Deleted rows current and original
owner must match and ownership cannot change. Comparison uses EF property equality semantics.
An unchanged owner marked modified by native detached `Update` is permitted. Resolve the
operation tenant once and only when protected writes exist; unregistered/global entries
receive no implicit ownership policy.

`TenantOwnershipException` exposes `Reason`, `EntityTypeName` and `PropertyName`, without
owner-key values. Reasons are MissingOwner=1, ForeignOwner=2, OwnershipChanged=3 and
InvalidConfiguration=4. Constructor/argument errors use native argument exceptions; consumer
key-resolution failures propagate. Membership, authorization and HTTP presentation are local.

A detached object can claim the current owner while targeting a foreign row ID. The native
ownership concurrency token supplies the stored-row predicate; zero affected rows remains
`DbUpdateConcurrencyException`. In-memory checks alone cannot protect that target. Stable
ownership does not detect same-tenant competing edits; configure and advance a separate
version when the consumer needs it.

## Supported configuration and limits

The first proofs cover native string and GUID keys and ordinary unshared single-table
entities. Ownership conversion, inheritance, owned/complex entity mappings, shared tables,
entity splitting and view mappings are excluded. Registration and protected-write validation
reject the exercised unsupported configurations. Named-filter replacement, disabled tokens,
wrong key types, generated ownership and ignored insert ownership are rejected before
accepting protected writes.

The Sales consumer additionally proves an independently mapped child with explicit ownership
registration and a native tenant-bearing foreign key to its parent's alternate key. A
relationship does not automatically protect or register the child. Consumers configure
principal/foreign keys and deletion policy themselves; row validation alone does not prove
that a referenced parent has the same owner.

When ownership participates in a native EF key, key immutability can reject a change during
change detection before a typed ownership failure is produced. The utility does not translate
native key/relationship failures. Same-owner detached customer edits are proven with the
Sales alternate key; no general tenant-transfer operation is supplied.

Consumers keep the supported model intact. Validation is a save-time check, not a query
interceptor; an altered/bypassed filter cannot be assumed to protect reads. Changing/removing
ownership annotations deliberately is outside supported wiring. Unregistered entities are
not inferred from names; consumer model tests classify all expected tables.

Raw SQL, bulk writes, `IgnoreQueryFilters`, foreign tracked entities/identity-map lookups,
privileged maintenance and arbitrary same-process code remain consumer responsibilities.
The proofs use native PostgreSQL text/UUID comparison. Custom collations, key comparers
and storage strategies need their own proofs of consistent identity semantics. The library
neither opens transactions, saves, commits, retries nor registers services. Native saves/transactions remain
visible; no ambient scope, base DbContext or automatic stamping is introduced.

See [the executable Inventory/Sales consumer](../../../samples/Wholesale/PersistenceDemo/README.md),
[the first-slice plan](../../../docs/plans/e2-1-tenant-ownership.md) and
[the first proof report](../../../docs/reports/e2-1-tenant-ownership.md) and
[the relationship proof report](../../../docs/reports/e2-3-tenant-relationships.md).

## Deferred direction

Automatic trusted-owner assignment is not implemented; the current mechanism rejects absent,
foreign or changed ownership rather than filling it. Raw/bulk writes and database-level RLS
remain outside tracked-save validation. Table metadata could support an optional PostgreSQL
adapter for USING/WITH CHECK policies, but role/context handling, privilege bypass, pooled
connections, rollback and competing writers need actual database proofs. No provider package
or universal SQL abstraction has been selected. Additional providers/mapping shapes require
their own identity, native-predicate and transaction evidence.
