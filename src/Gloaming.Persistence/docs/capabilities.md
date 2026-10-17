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

An optional [Sales HTTP composition](../../../samples/Wholesale/HttpIdentityDemo/README.md#optional-sales-http-row-security)
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

Optional PostgreSQL RLS and tenant/resource authorization composition are recorded in
[A2](../../../docs/plans/a2-tenant-resource-authorization-rls-research.md), alongside
[A1 Access/resource authorization](../../../docs/plans/a1-access-resource-authorization-research.md).
The [A2 assessment](../../../docs/reports/tenant-authorization-assessment.md) and
[primary-source matrix](../../../docs/reports/tenant-authorization-primary-sources.md) are now
prepared for review. Optional RLS needs a restricted runtime role and transaction-local setup
for every protected read/write/retry; current local credentials and raw transport paths do
not provide that guarantee. Tenant equality supplies no within-tenant resource permissions.
These findings add no supported library capability or approved interface. The later
[A2.2 consumer proof](../../../samples/Wholesale/CqrsDemo/TenantIsolation/README.md) now exercises
five Sales tables with separate credentials, raw/bulk paths and physical-pool/transaction
lifecycle controls. Its [report](../../../docs/reports/a2-2-sales-postgres-rls.md) records the
setting-forgery limit and paths not adopted. The current library guarantees and dependencies
above describe the provider-independent baseline; the later optional adapter below adds
automatic transaction setup for its separately selected composition.

[A2.3](../../../docs/reports/a2-3-postgres-tenant-context.md) subsequently extracts explicit
setup into the optional Tenancy/PostgreSQL family adapter. Persistence acquires no dependency
on it; ownership mapping/save validation and database policy/context setup remain separate.

[A2.4](../../../docs/reports/a2-4-ownership-rls-capability.md) now supplies the optional ownership/RLS
adapter with an independent PostgreSQL consumer. It keeps HasQueryFilter and tracked ownership
checks, adds USING/WITH CHECK policy helpers and binds the operation tenant before EF queries
and writes. Automatic setup requires an existing caller-owned transaction for reads and writes; the
explicit transaction-local utility remains independently usable. Refer to the adapter's contract for required integration, role/migration
obligations and rejected connection modes. This adds no default HTTP/worker or template rollout.

The owner-selected [transaction refinement](../../../docs/reports/a2-5-transaction-rls-simplification.md)
replaces the initial session/registry experiment: `.UseRls<TContext>(provider)` installs one
native transaction/command interceptor. The existing owner begins the transaction and all
its protected work receives local tenant context automatically. No per-transaction setup call
or additional tenancy scope is required. Selected reads also require that transaction.
