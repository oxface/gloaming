# E1 independent actor identity and tenancy

Status: split implemented, owner-reviewed and checkpointed as `c8cbf64`, 2026-10-03. The original combined
library was owner-reviewed and checkpointed as `a8e45c9`. The owner subsequently authorized
independent actor and tenancy libraries. The owner approved the complete reviewed change set. This document describes the revised interfaces;
[the split report](../reports/e1-identity-split.md) records current verification and limits.
[The original report](../reports/e1-tenant-actor.md) remains checkpoint evidence.

## Outcome and scope

Give ordinary .NET consumers independently adoptable actor-identity and tenancy contexts.
Each replaces repeated scoped-holder lifecycle and explicit requirement checks without
requiring the other library or the sample Access model. Keep executing actor and optional
initiator together; tenant choice belongs to its own context.

- `src/Gloaming.ActorIdentity/Gloaming.ActorIdentity/`: actor values, attribution, lifecycle and actor guard.
- `src/Gloaming.Tenancy/Gloaming.Tenancy/`: tenant choice, lifecycle and tenant guard.
- `src/Gloaming.ActorIdentity/tests/ActorIdentityTests/` and `src/Gloaming.Tenancy/tests/TenantTests/`: separate executable test consumers,
  each referencing only its selected foundation library and the test framework.
- `src/Gloaming.Tenancy/samples/ContextDemo/`: actor-only, tenancy-only and combined standard-DI usage.
- `src/Gloaming.Tenancy/samples/ContextDemo.Tests/`: consumer composition and capability proofs.

Both libraries have no package, project or extra framework dependencies. Their small holder
implementations are local to each library; there is no common runtime base package or generic
context framework. The active solution excludes the archive. The exercised sample is the
template recipe; no second template implementation is added.

## Public interfaces for review

| Actor identity | Purpose |
| --- | --- |
| `ActorId` | Validated opaque string with exact value equality. |
| `ActorKind`, `Actor` | Anonymous=1, Human=2, System=3; named factories enforce key presence/absence. Kind participates in actor equality. |
| `ActorContext(Actor actor, Actor? initiator = null)` | Immutable executing actor and optional explicitly supplied origin attribution. Null actor is rejected. Anonymous origin and absent origin are distinct. |
| `IActorContextAccessor.Current` | Read-only established actor context. |
| `IActorContextInitializer.Initialize(ActorContext)` | Host-facing establishment once within the owning scope. |
| `ActorContextAccessor` | Concrete reader/initializer holder and idempotent disposal. |
| `ActorContextRequirements.RequireIdentifiedActor()` | Presence check, returning the actor or throwing `IdentifiedActorRequiredException`. |

| Tenancy | Purpose |
| --- | --- |
| `TenantId` | Validated opaque string with exact value equality. |
| `TenantContext.ForTenant(TenantId)` | Immutable selected tenant, rejecting a null key. |
| `TenantContext.Tenantless()` | Deliberate execution outside a tenant boundary. |
| `ITenantContextAccessor.Current` | Read-only established tenancy context. |
| `ITenantContextInitializer.Initialize(TenantContext)` | Host-facing establishment once within the owning scope. |
| `TenantContextAccessor` | Concrete reader/initializer holder and idempotent disposal. |
| `TenantContextRequirements.RequireTenant()` | Selection check, returning the tenant or throwing `TenantRequiredException`. |

Each guard has its own typed exception. The old shared requirement discriminator and
`RequireTenantAndIdentifiedActor()` are removed. Consumer capabilities requiring both invoke
both checks explicitly; this does not justify another combined runtime library. Invalid
construction uses argument exceptions; lifecycle misuse uses invalid-operation/disposed
exceptions. These are errors to present through consumer policy, not HTTP result mappings.

Keys reject null/blank values and preserve accepted text without trimming, parsing or
normalization. Domain-ID mapping/canonicalization remains consumer code. Context carries
identity only: no roles, principal, trace IDs, cancellation or arbitrary metadata bags.
Actor denotes attribution, not an actor-model component. Neither established context nor
attribution grants authentication, tenant admission or permission.

## Lifecycle and independent composition

Each holder has independent single assignment. Early reads fail rather than inventing
anonymous or tenantless execution; the host deliberately supplies those values. Identical
or different second initialization fails. Reads after establishment support concurrency;
competing initializers publish one complete value per holder. Disposal is idempotent and
blocks later reads/initialization, while already returned immutable values remain values.

The split intentionally provides no atomic publication across both holders. A host may
establish actor identity before using it for consumer-owned tenant admission. It must finish
the segments required by a capability before invoking that capability. If later establishment
fails, the host aborts that operation and disposes its scope. Sales independently checks both
requirements and rejects missing context; the libraries cannot detect an arbitrary invalid
actor/tenant membership pairing. The consumer verifies that relationship.

Each operation owns its lifetime; await all branches before disposing. Child or subsequent
operations explicitly establish new scopes. There is no AsyncLocal, reset, nested restore,
implicit propagation, save, commit, rollback or retry protocol.

## Executable consumer usage

`DemoComposition.CreateActorServices()` registers only the actor holder and aliases; the
console uses it for anonymous host information and identified maintenance attribution.
`CreateTenantServices()` registers only the tenancy holder, aliases and Inventory fixtures;
availability reads require a selected tenant without resolving actor identity.
`CreateServices()` composes both and adds Sales. Sales calls Inventory through Contracts,
requires tenancy and an identified actor explicitly, and reports optional initiator.

The host registers each concrete holder as scoped and aliases its reader/initializer to
that same instance. See the two library READMEs for standalone recipes. Combined setup:

```csharp
await using var scope = serviceProvider.CreateAsyncScope();
scope.ServiceProvider.GetRequiredService<IActorContextInitializer>()
    .Initialize(new ActorContext(
        Actor.System(new ActorId("sales.order-fulfilment")), knownInitiator));
scope.ServiceProvider.GetRequiredService<ITenantContextInitializer>()
    .Initialize(TenantContext.ForTenant(new TenantId(consumerTenantKey)));

var operation = scope.ServiceProvider.GetRequiredService<ConsumerOperation>();
await operation.RunAsync(cancellationToken);
```

The sample's tenant/SKU fixtures have independently expected quantities 42 and 7. Sales
assesses a positive draft quantity through Inventory's contract. Fixtures do not constitute
persistence isolation, and co-located internal capabilities do not prove physical module
isolation. Demonstration identities are explicit; no fake-authenticated HTTP endpoints exist.

## Proof obligations

- Key validation/equality, actor-kind distinction, explicit anonymous/tenantless values and
  optional initiator with no identity or permission inferred from attribution.
- Per-holder early reads, null initialization, rejected reassignment, competing initializers,
  16 concurrent readers with 100 reads each, disposal before/after initialization.
- Separate executable test-project dependency graphs; both runtime libraries package-free
  and unrelated frameworks excluded.
- Actual DI aliases and disposal for each segment; actor-only and tenancy-only registration;
  Sales rejection when either required segment remains uninitialized.
- All six tenant-choice/actor-kind combinations through consumer capability calls.
- Concurrent scopes for the same actor in different tenants, child-scope independence and
  cleanup of both contexts on exceptions/cancellation followed by fresh establishment.
- Active restore/build, tests, console, formatting, style/analyzers, dependency verification,
  archive integrity, CI and hook updates.

## Later slices and limits

E2 designs explicit EF isolation utilities; it need not depend on either context library.
The sample maps its tenancy accessor into the persistence utility. E3 handles native HTTP
actor establishment, independently adoptable tenancy middleware, configurable tenant
selection utilities and separate consumer-owned membership/admission behavior.

Route selection should accept a configurable route-value name; host selection should allow
consumer conventions, including custom domains. One-tenant-per-user mapping is consumer
resolution. Candidate selection, canonical resolution and admission remain distinct. These
HTTP interfaces are not implemented or proven here. Native authorization keeps its meaning;
there is no parallel actor-specific HTTP policy. Organization is the sample domain/UI term,
Tenant the technical boundary. No automatic membership, population or fallback is introduced.

Library interfaces remain subject to owner line-by-line review. Leave changes unstaged;
implementation authorization does not authorize a commit.
