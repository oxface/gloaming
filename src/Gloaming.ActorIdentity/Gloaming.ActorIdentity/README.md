# Actor identity

A package-free, independently adoptable library for the identity performing an operation
and optional initiator attribution. It has no dependency on tenancy, DI, ASP.NET Core, EF,
transports, Aspire or sample Access types. Actor denotes attribution, not the actor
concurrency model. Consumers authenticate identities and authorize work.

## Public interface

- `ActorId`: a validated opaque string, preserved and compared exactly.
- `Actor`: explicitly anonymous, human or system. Human/system kind distinguishes identical
  key text. `ActorKind` has explicit values Anonymous=1, Human=2, System=3; 0 is invalid.
- `ActorContext`: immutable actor and optional initiator. Construct with
  `new ActorContext(actor, initiator)`; `new ActorContext(Actor.Anonymous)` deliberately
  establishes anonymity. No tenant choice is required.
- `IActorContextAccessor.Current`: read-only access to the established context.
- `IActorContextInitializer.Initialize`: host-facing, single-assignment establishment.
- `ActorContextAccessor`: scope-owned holder implementing both interfaces and `IDisposable`.
- `RequireIdentifiedActor()`: explicitly checks presence, throwing
  `IdentifiedActorRequiredException` for anonymity. It does not check trust or permission.

The host owns the scope and finishes establishment before business work begins. Early reads,
repeat initialization and access after disposal fail. Concurrent reads after initialization
are supported; disposal is idempotent. Scope disposal does not revoke captured values,
cancel work, save or commit. Different operations establish different scopes.

## Consumer setup

DI registration stays visible in consumer code:

```csharp
services.AddScoped<ActorContextAccessor>();
services.AddScoped<IActorContextAccessor>(provider =>
    provider.GetRequiredService<ActorContextAccessor>());
services.AddScoped<IActorContextInitializer>(provider =>
    provider.GetRequiredService<ActorContextAccessor>());

await using var scope = serviceProvider.CreateAsyncScope();
scope.ServiceProvider.GetRequiredService<IActorContextInitializer>()
    .Initialize(new ActorContext(
        Actor.System(new ActorId("sample.maintenance")),
        initiator: knownInitiator));
```

Business code injects the read accessor. The consumer supplies trusted canonical keys,
optional attribution and authorization; there is no implicit initiator population or
propagation. For OIDC, the sample will map validated issuer/subject to a globally unique
application user in consumer-owned Access code. The core performs no provider lookup.

See [actor-only and combined sample composition](../../../samples/Wholesale/ContextDemo/README.md),
[the split interfaces for review](../../../docs/plans/e1-tenant-actor.md) and
[the split report](../../../docs/reports/e1-identity-split.md). Optional
[ASP.NET Core integration](../Gloaming.ActorIdentity.AspNetCore/README.md) and its native
request proofs are implemented for review in E3.1. The core acquires no new dependencies.
No stable wire or persistence format is established here.

## Deferred direction

Current capabilities and consumer obligations are defined above. HTTP establishment is an
optional separate package; identity-provider lookup and authorization remain consumer policy.
No cross-process propagation or stable serialized context format is supported. A future
message/worker integration would need explicit trust, attribution and compatibility proofs
rather than automatically transferring a captured operation scope. No new core interface
is selected for that work.
