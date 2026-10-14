# Persistence composition and capabilities

Current scope, 2026-10-07. This family guide and the package-local contracts travel with
the source. Root plans/reports retain cross-cutting review and dated execution evidence.

## Supported composition

HasTenantOwnership configures a consumer-populated ownership property and context-bound equality
filter. ValidateTenantChanges detects missing/foreign ownership and changes to ownership before
protected saves. Native ownership concurrency predicates also reject detached targeting of a foreign
stored row. The supported configuration covers ordinary unshared single-table entities with native
string/GUID ownership keys; PostgreSQL is the verified provider.

Select only the packages needed by the consumer:

- [Gloaming.Persistence.EntityFrameworkCore](../Gloaming.Persistence.EntityFrameworkCore/README.md)

## Consumer obligations

Consumers populate ownership, establish a fixed operation key, map entities and relationships, own
migrations and invoke validation in both native save overrides. They choose tenant admission,
same-tenant foreign keys, separate edit-version tokens and final transactions/save/commit. No
Tenancy or ActorIdentity dependency is required.

The package READMEs specify public types, explicit integration steps, errors and unsupported
configuration. Copying or referencing one package does not automatically install another
family's context or policy. Consumers keep DI lifetimes, host wiring and native dependency
selection visible.

## Deferred context

Automatic stamping, RLS migration policies, bulk/raw SQL enforcement, tenant transfer, additional
providers and complex/shared/inherited mappings remain unsupported. An interceptor alone would not
protect bypass paths. Provider-specific policies need actual role/context and write/read isolation
proofs.

Deferred features need executable contract proofs before support can be claimed. No future
package or interface is implemented by this repository relocation.

## Queued consumer research

Optional PostgreSQL RLS and tenant/resource authorization composition are recorded in
[A2](../../../docs/plans/a2-tenant-resource-authorization-rls-research.md), alongside
[A1 Access/resource authorization](../../../docs/plans/a1-access-resource-authorization-research.md).
These are assessment-only backlog items for Umbrine, not supported capabilities or approved
interfaces. The current guarantees and dependencies above remain the baseline.
