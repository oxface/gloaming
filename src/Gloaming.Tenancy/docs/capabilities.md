# Tenancy composition and capabilities

This family guide and package-local contracts describe supported context establishment and selected adapters.

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

## Consumer composition

The [Persistence RLS adapter](../../Gloaming.Persistence/Gloaming.Persistence.EntityFrameworkCore.Postgres/README.md)
can establish immutable tenant context when a protected native transaction begins.
This family's utility remains an independent explicit option. Roles, policies, admission,
resource permission and transport trust remain consumer-owned; core Tenancy has no
Access, authorization-engine or Persistence dependency.
