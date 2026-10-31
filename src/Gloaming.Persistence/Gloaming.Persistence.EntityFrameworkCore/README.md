# Explicit EF tenant ownership

An opt-in EF Core Relational utility with no actor-identity, tenancy, Access, provider,
web or hosting dependency. PostgreSQL is the first proven provider; the consumer supplies
native storage keys, entities, schema, mappings, registration and saves.

## Shared save composition

This package also references [Gloaming.EntityFrameworkCore](../../Gloaming.EntityFrameworkCore/README.md).
HasTenantOwnership declares the tenant-ownership model requirement. Register
AddTenantOwnershipValidation<TContext,TKey>(context => context.RequiredTenant) and install
ConfigureGloamingCapabilities<TContext>(provider) in native scoped context options to run the existing
validator after all preparation. The required-key property must be accessible to the module's
registration code. The explicit override recipe below remains an alternative; choose one integration.
Neither recipe changes filtering, stamping or transaction ownership.

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

The proofs cover native string and GUID keys and ordinary unshared single-table entities,
including native EF 10 complex values/collections mapped to JSON columns in their owner's
table. Every top-level complex value must use `ToJson`; nested objects, optional nested values
and collections inherit their JSON container. Ownership remains a direct CLR scalar property
on the entity, outside JSON. JSON-only edits retain ordinary change detection, ownership
validation and native concurrency predicates.
Ownership conversion, inheritance, owned entity mappings, non-JSON complex values, shared tables,
entity splitting and view mappings are excluded. Registration and protected-write validation
reject the exercised unsupported configurations. Named-filter replacement, disabled tokens,
wrong key types, generated ownership and ignored insert ownership are rejected before
accepting protected writes.

### Native JSON value setup and compatibility

```csharp
row.ComplexProperty(x => x.Content, content =>
{
    content.ToJson("ContentJson");
    content.ComplexCollection(x => x.Levels);
});
row.HasTenantOwnership(x => x.OrganizationKey, () => RequiredOrganizationKey, "Scope");
```

No new guard registration, serializer or database schema is introduced by this compatibility
repair. Configure member names, enum conversion and column types through native EF/Npgsql.
The [independent consumer](../tests/Fixtures/JsonOwnership/README.md) proves multiple JSON columns,
init-only record values, collection replacement/in-place edits after reload, optional selection
transitions, root collections and explicit nullable string-enum conversion with EF 10.0.13,
Npgsql 10.0.3 and PostgreSQL 18.6. Both native save paths detect foreign JSON-only changes even
with automatic change detection disabled. The optional RLS adapter accepts the same JSON shape
while retaining its text-key restrictions and deployed policy requirements.

Native EF 10 limitations still apply. Complex collection declarations must implement `IList<T>`
(the proof uses `List<T>`); `IReadOnlyList<T>` declarations are rejected. Constructors cannot
bind complex-member/collection parameters: positional records containing them need an
EF-compatible constructor, such as a private parameterless constructor, or separate persistence
value types. Scalar-only leaf records are exercised without that extra constructor. Initialize
collection properties empty for materialization and seed defaults explicitly when creating a
new entity; EF can populate an existing collection during reload. Passing the ownership model
check does not certify arbitrary CLR shapes, custom serialization, nullability or EF features.

Applications own adoption of typed JSON into existing columns. Preserve JSON member/enum
representation intentionally and review native migration SQL against retained data; this library
does not convert payloads or run migrations. Immutable revisions, snapshots, source locks and
within-tenant rules remain module policy. Native MAF/Gloaming checkpoint and history JSON is
opaque library-owned text and is outside this typed business-value mapping capability.

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
neither opens transactions, saves, commits nor retries. Registration is explicit: select the
save-coordinator extension above or invoke validation through the native overrides. Native
saves/transactions remain visible; no ambient scope, base DbContext or automatic stamping is introduced.

See the [ownership consumer](../samples/PersistenceDemo/README.md) and [family capabilities](../docs/capabilities.md) for executable usage and supported mappings.

## Deferred direction

Automatic trusted-owner assignment is not implemented; the current mechanism rejects absent,
foreign or changed ownership rather than filling it. Raw/bulk writes remain outside this
provider-independent adapter's tracked-save validation. Tenant transfer, additional providers
and non-JSON complex/shared/inherited mapping shapes need their own proofs.

## Optional PostgreSQL row security

The separate [PostgreSQL adapter](../Gloaming.Persistence.EntityFrameworkCore.Postgres/README.md)
adds `.HasTenantOwnership(...).WithRls(settingName)`, explicit read/write policy migration
helpers and transaction-local context setup through `UseRls` and a native interceptor. Transactions
remain caller-owned. This base ownership package stays independent of Tenancy and Npgsql;
`FindTenantOwnershipProperty` exposes its declared property for optional integrations.

The [Sales isolation fixture](../../../samples/Wholesale/IntegrationTests/Scenarios/Cqrs/TenantIsolation/README.md)
demonstrates restricted policies and native setup. RLS does not enforce within-tenant permissions
or contain a credential holder who can forge the custom tenant setting.

The explicit transaction-setup recipe uses the independent
[Tenancy/PostgreSQL context utility](../../Gloaming.Tenancy/Gloaming.Tenancy.EntityFrameworkCore.Postgres/README.md)
instead of automatic interceptor setup. Choose one integration lifetime mode. The base ownership
package generates no policies and binds no tenant context; provider-specific setup belongs to
the selected optional package. The shared save coordinator retains its own dependencies and
does not perform transaction setup.
