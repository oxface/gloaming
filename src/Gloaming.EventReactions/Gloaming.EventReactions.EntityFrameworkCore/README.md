# Gloaming.EventReactions.EntityFrameworkCore

Preparation for shared [Gloaming EF capability integration](../../Gloaming.EntityFrameworkCore/README.md).
Dependencies are Gloaming.EventReactions, Gloaming.EntityFrameworkCore, EF Relational and DI
Abstractions. PostgreSQL is the verified provider.

## Registration and sources

```csharp
services.AddScoped<ImportRequest>();
services.AddEventReactions<OrdersContext, OrdersScope>(reactions => reactions
    .CollectFrom<Order>(order => order.Events)
    .CollectSources((provider, _) =>
        [provider.GetRequiredService<ImportRequest>().Events])
    .AddReaction<OrderSubmitted, ReserveAllowance>());
services.AddDbContext<OrdersContext>((provider, options) =>
    options.UseNpgsql(connection).ConfigureGloamingCapabilities<OrdersContext>(provider));

// In OrdersContext.OnModelCreating:
modelBuilder.ConfigureEventReactions();
modelBuilder.Entity<Order>().Ignore(order => order.Events);
```

These are ordinary consumer classes and event records. CollectFrom<TEntity>(selector) selects
buffers from tracked entries of that registered type, including Deleted entries. It does not infer
sources through inheritance or scan unrelated properties. CollectSources(provider, context) supplies
other composed buffers, including scoped non-entity objects. It resolves against the current operation
provider on each wave. Source selectors are explicit integration obligations: omitted buffers are not
discovered, drained or validated. Capture deletion facts before detaching their source.

An application adapter can instead inject IEventSink<OrdersScope> to raise facts into the operation
buffer, which is always collected. A pure model may return facts to that adapter without referencing
this library. The tag selects routing and DI bindings; it does not prescribe module architecture.

Keep native context options, source selectors and typed reactions in the module/application registration
extension called by the host. Models contain stable requirements; scoped services remain outside them.
AddReaction<TEvent,TReaction> registers a scoped class, and a factory overload supports explicit
construction. Resolution is deferred until handling so reactions may inject the completed context.

Use one AddEventReactions registration per context, with a distinct tag for each independently bound
context, and one context instance per operation. A second registration for the same context throws
during service setup, including when it uses a different tag. Put all that context's reactions and
source selectors in its single registration. Context pooling, singleton/shared options and concurrent
operation work are unsupported. No event service is injected into the context, and no save override
is needed.

## Saving and recovery

Begin the caller transaction, execute operations, save through any native SaveChanges overload, then
commit after saving returns. Pending events require an active context transaction. The adapter never
creates or commits one. Shared preparation drains every downstream wave, then detects changes and
runs selected feature validators before one native save. Register the relevant tenant, audit,
messaging, workflow and event-stream validation capabilities separately.

Capture order is preserved across selected sources and the operation sink; handler order is stable
within an event. It does not infer business dependencies. Use explicit application orchestration for
complex sequences. The default bound is 128 reaction invocations per dispatch. Sync saving rejects
async-only reactions without blocking. A reaction stages local work without saving or committing
independent effects.

There is no Stage API, save acknowledgment, transaction-end binding or completion interceptor.
SaveChanges(false) retains native EF state: use ChangeTracker.AcceptAllChanges after success before
continuing work. Incorrectly accepting unsaved row changes does not withdraw their raised events.

On any error, roll back/dispose the transaction and discard the scope. Retry with fresh state and
a new business decision. Observed preparation/dispatch errors reject reuse; native write, concurrency,
cancellation and commit errors have no universal reuse guard. Successful saving does not establish
later caller commit or repair consumed buffers after rollback.

Do not raise events in persistence/completion callbacks: they can remain pending after saving succeeds.
Suppression, SQL/bulk writes, arbitrary tracker changes, options omitting shared configuration and
automatic same-context retry are outside this contract. No tracker mirror, repository, durable queue,
event log, serializer, integration-message mapping or aggregate base is provided.

[Compiled registration](../src/Gloaming.EventReactions/samples/EventReactionsDemo/Consumer/DirectOrdersRegistration.cs),
[scoped request proof](../src/Gloaming.EventReactions/samples/EventReactionsDemo/Consumer.Tests/AdHocSourceTests.cs),
[execution contract](../docs/local-dispatch.md) and
[current report](../../../docs/reports/event-reactions-consolidation.md).
