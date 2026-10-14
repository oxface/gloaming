# Tenancy composition and capabilities

Current scope, 2026-10-07. This family guide and the package-local contracts travel with
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

## Queued consumer research

Tenant-aware resource authorization and native authorization ordering are recorded in
[A2](../../../docs/plans/a2-tenant-resource-authorization-rls-research.md), alongside
[A1 Access/resource authorization](../../../docs/plans/a1-access-resource-authorization-research.md).
These are assessment-only backlog items for Umbrine, not supported capabilities or approved
interfaces. The current guarantees and dependencies above remain the baseline.
