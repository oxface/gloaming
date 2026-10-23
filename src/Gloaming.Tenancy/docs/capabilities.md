# Tenancy composition and capabilities

Foundational scope, 2026-10-07; A2 composition research reconciled 2026-10-16.
This family guide and the package-local contracts travel with
the source. Root plans/reports retain cross-cutting review and dated execution evidence.

## Supported composition

The core distinguishes deliberate tenantless execution from missing initialization and keeps the
selected opaque tenant key fixed for an operation. The HTTP adapter resolves and admits a candidate
before publication; denied or unknown candidates cannot silently become tenantless. Endpoint/group
tenancy requirements remain independent of anonymous or named authorization policies.

The PostgreSQL adapter explicitly sets a consumer-selected transaction-local setting from
the operation tenant, with native connection/transaction checks. It adds no automatic
transaction, policy or scope establishment. Consumers must call it before protected reads
and writes in every transaction/attempt; it does not run through SaveChanges capabilities.

Select only the packages needed by the consumer:

- [Gloaming.Tenancy](../Gloaming.Tenancy/README.md)
- [Gloaming.Tenancy.AspNetCore](../Gloaming.Tenancy.AspNetCore/README.md)
- [Gloaming.Tenancy.EntityFrameworkCore.Postgres](../Gloaming.Tenancy.EntityFrameworkCore.Postgres/README.md)

## Consumer obligations

Consumers select canonical tenant identities, perform membership/admission checks, authorize
capabilities and present failures. HTTP establishment runs after native authorization and any
selected actor completion; tenant context is not available to native authorization handlers in this
composition. The family requires neither ActorIdentity nor a particular Organization model.

The package READMEs specify public types, explicit integration steps, errors and unsupported
configuration. Copying or referencing one package does not automatically install another
family's context or policy. Consumers keep DI lifetimes, host wiring and native dependency
selection visible.

## Deferred context

Worker/message context propagation, tenant rebinding, cross-process formats and automatic database
row-isolation provisioning are not supplied here. Persistence ownership is a separate opt-in
family. Admission and future transport trust need consumer-specific proofs; tenantless
execution never grants unrestricted access.

Deferred features need executable contract proofs before support can be claimed. No future
package or interface is implemented by this repository relocation.

## Consumer research

Tenant-aware resource authorization and native authorization ordering are recorded in
[A2](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/a2-tenant-resource-authorization-rls-research.md), alongside
[A1 Access/resource authorization](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/a1-access-resource-authorization-research.md).
The [A2 assessment](../../../docs/reports/tenant-authorization-assessment.md) now traces the
current composition: the optional actor evaluator establishes actor before native handlers;
tenant admission still follows native authorization. Late consumer resource checks are the
recommended first composition. Database RLS is a separate optional proof, not tenant admission
or within-tenant permission. No middleware reordering, Access/FGA dependency, new supported
capability or approved interface follows from this assessment.

The later [A2.2 sample proof](../../../samples/Wholesale/IntegrationTests/Scenarios/Cqrs/TenantIsolation/README.md)
uses native PostgreSQL policies and explicit transaction-local setup with separate runtime
credentials. Its [matrix](../../../docs/reports/a2-2-sales-postgres-rls.md) bounds database
tenant isolation, setting forgery and paths not adopted. That original proof kept setup local.
The owner subsequently selected
[A2.3 extraction](../../../docs/reports/a2-3-postgres-tenant-context.md): the explicit setup
utility is now an optional adapter, independently proven alongside the Sales adoption.
RLS policies, runtime role checks and within-tenant permissions remain consumer-owned.

The separately authorized [A2.4 ownership/RLS adapter](../../Gloaming.Persistence/Gloaming.Persistence.EntityFrameworkCore.Postgres/README.md)
now connects this immutable accessor to `.HasTenantOwnership(...).WithRls(settingName)` and
native transaction setup for ordinary scoped EF requests. It is optional Persistence/provider
integration; core Tenancy and this family's explicit transaction-local utility gain no dependency
on it. Its [report](../../../docs/reports/a2-4-ownership-rls-capability.md) records fresh read/write
proofs, explicit policy migration obligations and unsupported connection modes.
