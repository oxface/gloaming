# Gloaming.EventReactions

Composable local event capture and bounded reactions. Consumers own the event meaning, object model,
subscriptions and persistence. Events can be ordinary records or value records; no event marker,
aggregate base or source interface is required.

| Package | Capability | Dependencies |
| --- | --- | --- |
| [Gloaming.EventReactions](Gloaming.EventReactions/README.md) | Buffer/sink, typed reactions, registry and ordered dispatcher | Platform only |
| [Gloaming.EventReactions.EntityFrameworkCore](Gloaming.EventReactions.EntityFrameworkCore/README.md) | Explicit source selectors and preparation before native saving | Core, shared EF integration, EF Relational and DI Abstractions |

Use this capability for bounded local reactions. Required intra-module changes finish in the same
owning transaction. Prefer explicit application orchestration for complex dependencies or decisions
that need another reaction's result. The registry routes events; it does not infer a business graph.

A consumer can compose PendingEvents into an existing class hierarchy, select a buffer from a scoped
request, or raise plain facts through IEventSink<TModule>. The generic tag selects subscriptions and
the operation sink; it need not represent a module or impose a repository structure.

The [focused consumer](samples/EventReactionsDemo/README.md) demonstrates a package-free
immutable model. [Adoption fixtures](tests/AdoptionTests/Consumer.Tests.csproj) retain consumer-owned
inheritance and scoped non-entity source variants. The
[Sales consumer](../../samples/Wholesale/IntegrationTests/Scenarios/DomainEvents/README.md) supplies private domain facts
without making them framework types.

```sh
dotnet test --project src/Gloaming.EventReactions/tests/EventReactionsTests/EventReactionsTests.csproj
dotnet test --project src/Gloaming.EventReactions/tests/AdoptionTests/Consumer.Tests.csproj
```

Read the [execution contract](docs/local-dispatch.md). Event sourcing, durable delivery,
business history and application workflow policy are separately selected capabilities.
