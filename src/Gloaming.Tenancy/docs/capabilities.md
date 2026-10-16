# Tenancy composition and capabilities

Foundational scope, 2026-10-07; A2 composition research reconciled 2026-10-16.
This family guide and the package-local contracts travel with
the source. Root plans/reports retain cross-cutting review and dated execution evidence.

## Supported composition

The core distinguishes deliberate tenantless execution from missing initialization and keeps the
selected opaque tenant key fixed for an operation. The HTTP adapter resolves and admits a candidate
before publication; denied or unknown candidates cannot silently become tenantless. Endpoint/group
tenancy requirements remain independent of anonymous or named authorization policies.

Select only the packages needed by the consumer:

- [Gloaming.Tenancy](../Gloaming.Tenancy/README.md)
- [Gloaming.Tenancy.AspNetCore](../Gloaming.Tenancy.AspNetCore/README.md)

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

Worker/message context propagation, tenant rebinding, cross-process formats and database row
isolation are not supplied here. Persistence ownership is a separate opt-in family. Admission and
future transport trust need consumer-specific proofs; tenantless execution never grants unrestricted
access.

Deferred features need executable contract proofs before support can be claimed. No future
package or interface is implemented by this repository relocation.

## Consumer research

Tenant-aware resource authorization and native authorization ordering are recorded in
[A2](../../../docs/plans/a2-tenant-resource-authorization-rls-research.md), alongside
[A1 Access/resource authorization](../../../docs/plans/a1-access-resource-authorization-research.md).
The [A2 assessment](../../../docs/reports/tenant-authorization-assessment.md) now traces the
current composition: the optional actor evaluator establishes actor before native handlers;
tenant admission still follows native authorization. Late consumer resource checks are the
recommended first composition. Database RLS is a separate optional proof, not tenant admission
or within-tenant permission. No middleware reordering, Access/FGA dependency, new supported
capability or approved interface follows from this assessment.
