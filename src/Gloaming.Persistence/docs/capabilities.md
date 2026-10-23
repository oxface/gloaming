# Persistence composition and capabilities

Foundational scope, 2026-10-07; save wiring and A2 research reconciled 2026-10-16.
This family guide and the package-local contracts travel with
the source. Root plans/reports retain cross-cutting review and dated execution evidence.

## Supported composition

HasTenantOwnership configures a consumer-populated ownership property and context-bound equality
filter. ValidateTenantChanges detects missing/foreign ownership and changes to ownership before
protected saves. Native ownership concurrency predicates also reject detached targeting of a foreign
stored row. The supported configuration covers ordinary unshared single-table entities with native
string/GUID ownership keys; PostgreSQL is the verified provider.

Select only the packages needed by the consumer:

- [Gloaming.Persistence.EntityFrameworkCore](../Gloaming.Persistence.EntityFrameworkCore/README.md)
- [Gloaming.Persistence.EntityFrameworkCore.Postgres](../Gloaming.Persistence.EntityFrameworkCore.Postgres/README.md):
  explicit `.WithRls(settingName)` selection, reviewed read/write policy migration helpers and
  automatic admitted-tenant transaction context using native EF interceptors.

## Consumer obligations

Consumers populate ownership, establish a fixed operation key, map entities and relationships, own
migrations and cover both sync/async saves through the documented shared capability binding
or explicit native save overrides. They choose tenant admission,
same-tenant foreign keys, separate edit-version tokens and final transactions/save/commit. The provider-independent
ownership package requires no Tenancy or ActorIdentity dependency. The optional PostgreSQL
adapter reads Tenancy core through the host-provided immutable accessor; admission and
within-tenant permissions remain consumer policy.

The package READMEs specify public types, explicit integration steps, errors and unsupported
configuration. Copying or referencing one package does not automatically install another
family's context or policy. Consumers keep DI lifetimes, host wiring and native dependency
selection visible.

An optional [Sales HTTP composition](../../../samples/Wholesale/Api/README.md#optional-sales-http-row-security)
now exercises the PostgreSQL capability against six finite tables, including accepted profile
audit. Unconverted PostgreSQL text/varchar ownership is supported; blank-padded character
keys remain rejected. Default HTTP/AppHost/transport rollout remains separate.

## Deferred context

Automatic stamping, automatic RLS policy scaffolding, tenant transfer, additional providers
and complex/shared/inherited mappings remain unsupported. The optional PostgreSQL adapter
supplies explicit policy operations and bounded read/write isolation; it requires restricted
runtime credentials and deployed policies. An interceptor alone does not provide database enforcement.

Deferred features need executable contract proofs before support can be claimed. No future
package or interface is implemented by this repository relocation.

## Consumer research

[Authorization and tenant-isolation research](../../../docs/research/authorization-and-isolation.md)
explains the authority, credential and query-completeness boundaries behind this composition.
The supported optional RLS contract above supersedes the old assessment's unimplemented
proposals. Actual [Sales isolation proofs](../../../samples/Wholesale/IntegrationTests/Scenarios/Cqrs/TenantIsolation/README.md)
and [HTTP setup](../../../samples/Wholesale/Api/README.md#optional-sales-http-row-security)
retain their finite deployment/setting-forgery limits. Tenant equality is not within-tenant
permission; no default application rollout or authorization-engine dependency is selected.
