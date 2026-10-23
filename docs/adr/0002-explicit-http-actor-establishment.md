# 0002: Establish HTTP actors after effective native authentication

Status: accepted design direction in the owner-approved E3.1 plan, 2026-10-04.
Implementation and registration helpers were owner-reviewed and checkpointed as `faefc0b`
on 2026-10-05.

## Context

The immutable actor core is independent of ASP.NET Core, tenant selection and consumer
Access. HTTP consumers need its context before business work and any authorization handler
that reads the application actor. Endpoint policies can authenticate different schemes from
the default scheme, while a request without a policy may never invoke a policy evaluator.
Native authorization and challenge behavior must remain consumer-controlled.

## Decision

Provide an optional ActorIdentity ASP.NET Core adapter with an explicit consumer resolver.
Consumers wrap a selected native `IPolicyEvaluator` to establish actor context after its
policy authentication and before authorization. Separate downstream middleware completes
establishment for policy-free requests. A request feature coordinates successful publication
between those paths without rebinding the core or introducing ambient context.

Keep registration, native authentication/authorization and both integration points visible
in consumer code. The adapter does not select schemes, add actor authorization policies,
discover identities, map claims by convention or choose HTTP failure responses. The resolver
maps the effective authenticated principal; unauthenticated execution establishes explicit
anonymity. An authenticated principal cannot silently become anonymous after failed mapping.

Explicit registration/pipeline helpers may group the holder aliases, resolver and evaluator
wrapper. Their effects remain documented; a custom inner evaluator is supplied by a factory
rather than discovered from existing service descriptors. Consumers retain native
authentication/authorization setup, middleware order and direct composition when needed.

The adapter depends only on ActorIdentity and the native ASP.NET Core framework. Both
ActorIdentity and Tenancy remain independently adoptable package-free cores. Provider-specific
cookie/OIDC setup and external-identity/account policy stay in the sample/template consumer.

## Consequences and limits

Consumer authorization handlers can read established attribution without replacing native
policy enforcement. Correct composition requires the wrapper and completion middleware;
there is no automatic registration or hidden policy evaluation pass. Consumer-selected inner
evaluators remain explicit, and the host chooses mapping-failure presentation.

One request has one immutable actor context. Principal replacement after publication is
rejected; mutation in place and identity-changing re-execution are outside this composition.
Full remote OIDC/session topology and tenant admission need separate evidence.

See [the approved interface plan](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/e3-1-http-actor-identity.md),
[the adapter recipe](../../src/Gloaming.ActorIdentity/Gloaming.ActorIdentity.AspNetCore/README.md) and
[the execution report](../reports/e3-1-http-actor-identity.md).
