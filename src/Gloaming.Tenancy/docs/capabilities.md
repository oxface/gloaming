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

[Authorization and tenant-isolation research](../../../docs/research/authorization-and-isolation.md)
explains native policy ordering, late resource checks and the separate database-enforcement
obligation. The [optional Persistence RLS adapter](../../Gloaming.Persistence/Gloaming.Persistence.EntityFrameworkCore.Postgres/README.md)
can establish this immutable tenant automatically when the caller begins a protected native
transaction. This family's transaction-local utility remains an independent explicit option.
Roles/policies, membership admission, resource permission and transport trust remain consumer-owned;
core Tenancy gains no Access/FGA or Persistence dependency.
