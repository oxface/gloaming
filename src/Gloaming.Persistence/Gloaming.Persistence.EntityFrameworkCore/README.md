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
including native EF 10 scalar, nested and optional complex values stored as ordinary columns
in their owner's table, and complex values/collections mapped to JSON columns. Inline and
JSON values may coexist. Inline complex leaves are checked recursively against the owner
table; JSON members inherit their container. Ownership remains a directly mapped CLR scalar
property on the entity, outside all complex values. Complex-only edits retain ordinary change
detection, ownership validation and native concurrency predicates.
Ownership conversion, inheritance, owned entity mappings, multiple entity types sharing a table,
entity splitting and view mappings are excluded. Registration and protected-write validation
reject the exercised unsupported configurations. Named-filter replacement, disabled tokens,
wrong key types, generated ownership and ignored insert ownership are rejected before
accepting protected writes.

Both adapters use the same table/graph classification. Completed models are checked once
before save-time entry-state filtering, without resolving the operation tenant. Unsupported
owned dependents in separate tables and inheritance configured after ownership registration
therefore fail even when only an unannotated child or derived entry changes. Owned mappings
are excluded on the protected owner as well as on an explicitly selected owned entity.

| Native shape | Ownership support |
| --- | --- |
| Inline class/record values, structs and nullable record structs | Supported when their scalar leaves map to the owner table. |
| Primitive arrays/lists on the entity or inside an inline complex value | Supported as native PostgreSQL array columns; these are scalar properties, not EF complex collections. |
| Root primitive collections explicitly mapped to `jsonb` | Supported with Npgsql 10. |
| Converted non-tenant scalar leaves | Supported through native EF configuration; ownership scalar converters remain excluded. |
| Optional inline values with required members or a native discriminator marker | Supported, including present values with all consumer leaves null when a marker is configured. |
| Complex collections | Use JSON on relational EF 10; a separate collection table is an entity/relationship mapping. |
| JSON complex subtree below an inline complex parent | Rejected by native EF 10; keep JSON as a separate root value or map the containing value to JSON. |
| Owned entity graphs, inheritance, shared/split entity tables, view mappings | Deliberate unsupported ownership shapes, including native-valid variants. |

The [positive value-shape journey](../tests/Fixtures/RelationalOwnership/ValueShapeJourney.cs)
compares native and ownership-enabled DDL and exercises replacement, in-place collections,
optional transitions and foreign-write/concurrency refusal on PostgreSQL. The
[graph boundary regressions](../tests/EntityFrameworkCoreTests/MappingBoundaryTests.cs)
distinguish native-valid excluded mappings from supported payload shapes. Native model
validity alone does not establish a tenant predicate for every separately tracked row.

### Native relational complex value setup

```csharp
row.ComplexProperty(x => x.Text, text =>
{
    text.Property(x => x.Name).HasColumnName("Name").HasMaxLength(200);
    text.Property(x => x.TechnologyContext).HasColumnName("TechnologyContext").IsRequired(false);
});
row.HasTenantOwnership(x => x.OrganizationKey, () => RequiredOrganizationKey, "Scope");
```

Use native `ComplexProperty` configuration before or after ownership registration. No new
ownership API, serializer or save path is required. A complex value has no separate entity
identity; EF's term "table splitting" for its inline columns does not mean multiple entity
types sharing a table, which remains unsupported. Nested required/optional values use the
same mapping. An optional complex value needs a required member or native discriminator
marker so EF 10 can distinguish absence from a present value; relational complex collections
still require JSON. Entity
navigations, keys/indexes into complex members and EF 11 features are outside this contract.
See Microsoft's [complex-type guide](https://learn.microsoft.com/en-us/ef/core/modeling/complex-types)
and [EF 10 release notes](https://learn.microsoft.com/en-us/ef/core/what-is-new/ef-core-10.0/whatsnew).

The [independent relational consumer](../tests/Fixtures/RelationalOwnership/README.md) proves
explicit column names/facets, null scalar leaves, replacement, nested-only updates, optional
transitions and mixed JSON values with automatic change detection disabled through both
native save paths. PostgreSQL proofs verify the actual columns, tenant guard failures and
stored-owner concurrency refusal for detached nested-only updates/deletes. Registration
ordering, filter/token tampering and unsupported entity shapes have separate model proofs.

Applications own the persisted CLR property, native mapping, schema and migrations. Replacing
a flattened projection requires a real persisted value; merely changing `Ignore` to
`ComplexProperty` may not be sufficient. Preserve existing column names/facets and inspect
migration SQL against retained data. This adapter neither changes schemas nor resets data.
Relationships, collection rows, publication immutability and tenant admission remain consumer
policy. This extends the existing ownership mechanism without introducing a new reusable API.

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
and shared/inherited/split entity mapping shapes need their own proofs.

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
