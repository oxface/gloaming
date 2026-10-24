# ActorIdentity composition and capabilities

This family guide and package-local contracts describe current capabilities, setup and supported limits.

## Supported composition

The core distinguishes anonymous, human and system actors, validates opaque keys and publishes one
immutable context per operation. Early reads, reinitialization and use after disposal fail. The HTTP
adapter combines the native policy evaluator with completion middleware so policy-free requests also
establish context. Unmapped authenticated identities fail; native authentication and authorization
retain their meaning.

Select only the packages needed by the consumer:

- [Gloaming.ActorIdentity](../Gloaming.ActorIdentity/README.md)
- [Gloaming.ActorIdentity.AspNetCore](../Gloaming.ActorIdentity.AspNetCore/README.md)

## Consumer obligations

Consumers authenticate external identities, map canonical application keys, supply initiator
attribution, authorize work and select error responses. They own operation scopes and middleware
order. ActorIdentity does not select a tenant, resolve an Access directory or register an
authentication scheme.

The package READMEs specify public types, explicit integration steps, errors and unsupported
configuration. Copying or referencing one package does not automatically install another
family's context or policy. Consumers keep DI lifetimes, host wiring and native dependency
selection visible.

## Deferred context

Cross-process context formats, automatic message propagation, principal rebinding and universal
provider/claims mapping are unsupported. Any later transport adapter needs explicit trust,
attribution, lifetime and compatibility proofs.

Deferred features need executable contract proofs before support can be claimed. No future
package or interface is implemented by this repository relocation.
