# Modular monolith patterns

Apply these documents to a consuming application's repository. They describe a preferred
structure and implementation process, not a generated template or required Gloaming runtime.
The documents can be read in place or copied as one folder; implementation instructions
use sibling documents and public library contracts.

A request can be as small as:

> Explore the documentation in /modular-monolith-patterns and apply it to this repository.
> Start with modules X (AggregateA, AggregateB) and Y (AggregateE with event sourcing).

## Start here

1. Inspect the target repository's instructions, solution, tooling, existing architecture and
   working changes. Establish the application name, requested modules/aggregate roots, persistence
   choice per aggregate and the first behavior to prove. Preserve unrelated work.
2. Read [structure](structure.md). Adapt an existing application toward the boundaries; keep
   working public behavior and current project conventions unless the request changes them.
3. For initialization or missing host infrastructure, follow [bootstrap](bootstrap.md).
   For requested modules/aggregates, follow [modules](modules.md) and the relevant persistence
   branch. Follow [messaging](messaging.md) when durable handoff or command receipts are selected.
4. Resolve [library integration](gloaming.md) against the package versions actually being used.
   Essential contracts remain with each library. Implement one runnable path through the selected
   modules, native persistence and host.
5. Complete [verification](verification.md). Record the resulting module map, infrastructure and
   transaction ownership in the target application's architecture documentation.

The target repository's instructions and explicit user decisions take precedence over these
defaults. Ask for missing decisions that change business meaning or require credentials; continue
independent setup while those answers are pending. State ordinary technical assumptions clearly.

## Interpret the request

The example selects two modules, not three projects for three aggregates:

| Module | Aggregate roots | Persistence |
| --- | --- | --- |
| X | AggregateA, AggregateB | Native state-stored EF by default |
| Y | AggregateE | Event-sourced aggregate with required inline state |

Each module owns a context, schema, migrations and named connection. Aggregate boundaries stay
inside their module; another module calls its Contracts. A module can mix persistence styles
when explicitly required. API style is an application choice; match the existing style, using
minimal APIs for a new HTTP host unless requested otherwise.

When only aggregate names are supplied, establish the named boundaries and a minimal labelled
create/load example. Keep business invariants and additional process stages tied to actual
requirements. A timer, saga, broker or cross-module write is not implied by having two modules.

## Completion

The requested modules are registered and reachable through real application operations.
Chosen persistence and migrations work; required identity/tenant context is established; tests
prove accepted/rejected work, isolation and rollback. The report names commands actually run,
remaining configuration and unsupported behavior. An empty project graph alone is not completion.
