# AlCopilot domain-event integration comparison

Follow-up: the owner selected sync/async EF interception. [D2.1d](d2-1d-module-owned-event-composition.md)
implements module-owned registration and options-only standalone contexts. The local comparison and
initial recommendation below describe D2.1c, before that cleanup; AlCopilot findings remain pinned.

Research date: 2026-10-15. The reference is pinned to
[AlCopilot commit `f67696ee68b7ff49554d28094584c6bd178e4e44`](https://github.com/oxface/AlCopilot/commit/f67696ee68b7ff49554d28094584c6bd178e4e44),
which was `main` when read. This is source inspection plus the isolated equality check described
below, not a full project test run or a broad audit of AlCopilot. The local comparison describes the D2.1c Gloaming working tree, including changes
that the owner had not approved for commit.

## Conclusion

AlCopilot demonstrates the preferred consumer shape well: the host calls a module's
registration extension; that extension configures persistence and domain-event support;
the DbContext contains its model rather than handler composition. Adopt that shape in the
standalone Gloaming sample. Keep Gloaming's bounded cascade handling, transaction ownership,
failure lifecycle and separation of event handling from event storage. The reference does
not establish those guarantees for reactions: its integration fixture explicitly registers
no handlers. No new reusable mechanism was proven by this research.
([Host composition](https://github.com/oxface/AlCopilot/blob/f67696ee68b7ff49554d28094584c6bd178e4e44/server/src/AlCopilot.Host/Program.cs#L12-L18),
[module composition](https://github.com/oxface/AlCopilot/blob/f67696ee68b7ff49554d28094584c6bd178e4e44/server/src/Modules/AlCopilot.DrinkCatalog/DrinkCatalogModule.cs#L19-L37),
[fixture](https://github.com/oxface/AlCopilot/blob/f67696ee68b7ff49554d28094584c6bd178e4e44/server/tests/AlCopilot.DrinkCatalog.IntegrationTests/Integration/PostgresFixture.cs#L15-L29).)

## What the reference actually does

The host calls `AddDrinkCatalogModule`, `AddCustomerProfileModule` and
`AddRecommendationModule`. Each module registers event assembly metadata, a scoped
`DomainEventInterceptor` and its native DbContext. The module's DbContext options attach
the interceptor. `DrinkCatalogDbContext` takes only its native options and has no custom
save overrides. The host does not assemble its handlers or event session.
([Host](https://github.com/oxface/AlCopilot/blob/f67696ee68b7ff49554d28094584c6bd178e4e44/server/src/AlCopilot.Host/Program.cs#L12-L18),
[Drink Catalog module](https://github.com/oxface/AlCopilot/blob/f67696ee68b7ff49554d28094584c6bd178e4e44/server/src/Modules/AlCopilot.DrinkCatalog/DrinkCatalogModule.cs#L19-L37),
[Customer Profile module](https://github.com/oxface/AlCopilot/blob/f67696ee68b7ff49554d28094584c6bd178e4e44/server/src/Modules/AlCopilot.CustomerProfile/CustomerProfileModule.cs#L14-L33),
[Recommendation module](https://github.com/oxface/AlCopilot/blob/f67696ee68b7ff49554d28094584c6bd178e4e44/server/src/Modules/AlCopilot.Recommendation/RecommendationModule.cs#L16-L35),
[DbContext](https://github.com/oxface/AlCopilot/blob/f67696ee68b7ff49554d28094584c6bd178e4e44/server/src/Modules/AlCopilot.DrinkCatalog/Data/DrinkCatalogDbContext.cs#L11-L28).)

`AggregateRoot<TId>` owns an event list behind `IAggregateRoot`. Domain methods call protected
`Raise`; infrastructure can inspect or clear the list. `IDomainEvent` requires a GUID
aggregate ID and occurrence time. There is no general operation-level sink in this shared
API. This reference therefore supports scanning tracked aggregate entities; it does not
by itself demonstrate immutable domain objects mapped onto separate EF rows.
([Aggregate base](https://github.com/oxface/AlCopilot/blob/f67696ee68b7ff49554d28094584c6bd178e4e44/server/src/AlCopilot.Shared/Domain/AggregateRoot.cs),
[aggregate interface](https://github.com/oxface/AlCopilot/blob/f67696ee68b7ff49554d28094584c6bd178e4e44/server/src/AlCopilot.Shared/Domain/IAggregateRoot.cs),
[event interface](https://github.com/oxface/AlCopilot/blob/f67696ee68b7ff49554d28094584c6bd178e4e44/server/src/AlCopilot.Shared/Domain/IDomainEvent.cs).)

The interceptor's asynchronous saving callback performs this sequence:

1. Scan tracked `IAggregateRoot` instances and snapshot their pending events.
2. Clear all of those aggregate buffers.
3. Add serialized `DomainEventRecord` rows for the entire batch.
4. Dispatch every event, awaiting its handlers sequentially.
5. Rescan for events produced by those handlers and repeat, subject to a depth limit.
6. Return to native EF persistence.

The loop does handle cascades, including events on newly tracked roots. It does not save
between waves. It neither begins a transaction nor records handler completion; records
and tracked changes are left for the ensuing native save.
([Complete interceptor](https://github.com/oxface/AlCopilot/blob/f67696ee68b7ff49554d28094584c6bd178e4e44/server/src/AlCopilot.Shared/Data/DomainEventInterceptor.cs#L14-L83).)

The dispatcher uses a cached generic invocation delegate for each runtime event type,
resolves `IDomainEventHandler<TEvent>` services from its scoped provider, and invokes them
sequentially. It succeeds when that service collection is empty. The type registry is a
different mechanism: it scans attributes for stable serialized event names and combines
assemblies registered by all modules. It does not discover or register handlers.
([Dispatcher](https://github.com/oxface/AlCopilot/blob/f67696ee68b7ff49554d28094584c6bd178e4e44/server/src/AlCopilot.Shared/Data/DomainEventDispatcher.cs#L9-L53),
[assembly registration](https://github.com/oxface/AlCopilot/blob/f67696ee68b7ff49554d28094584c6bd178e4e44/server/src/AlCopilot.Shared/Domain/DomainEventRegistryServiceCollectionExtensions.cs#L10-L25),
[type registry](https://github.com/oxface/AlCopilot/blob/f67696ee68b7ff49554d28094584c6bd178e4e44/server/src/AlCopilot.Shared/Domain/DomainEventTypeRegistry.cs#L35-L72).)

## What to adopt

- **Module-owned registration.** Provide one ordinary `IServiceCollection` extension in
  each sample module. Register the DbContext, scoped handlers, event integration and
  module-specific policy there. The host calls that extension. This directly follows
  the reference's composition shape.
- **A small DbContext surface.** The previously selected explicit save call can retain
  this clarity without requiring automatic interception: inject one registered EF
  coordinator and call it in the native save overrides. The context should not create
  its registry or accept both a coordinator and its underlying session. This is a
  proposal for Gloaming, not a claim that AlCopilot uses explicit save calls.
- **Typed, sequential handlers and automatic tracked-root collection.** Those match
  both implementations' intended use. Keep the Gloaming sink for decisions whose
  domain objects are not EF entities, rather than requiring every aggregate to inherit
  a persistence-visible base.

The former standalone ConsumerScope (current replacement: [module registration](https://github.com/oxface/modulith-foundry/blob/dd6eb77a4a0e3c8afd7af710d7689ca265d43a64/src/Gloaming.EventReactions/samples/EventReactionsDemo/Consumer/DirectOrdersRegistration.cs))
creates a service provider and operation scope for each finite consumer run. That lifetime
management is legitimate demo/test setup. Its calls into `DirectOrdersContext.RegisterEvents`
and `MappedQuotesContext.RegisterEvents`, and its explicit constructor wiring of both the
session and save coordinator, make the application composition harder to recognize. A
module registration extension should own that wiring; a host/test may still create a
scope. This observation concerns the D2.1c working tree, not AlCopilot.

## What to change or keep separate

These are concrete consequences inferred from the pinned source. They were not reproduced
by executing AlCopilot tests.

| Concern | Source behavior and scenario | Gloaming implication |
| --- | --- | --- |
| Synchronous saves | The interceptor overrides only `SavingChangesAsync`. A caller using native synchronous `SaveChanges` has no synchronous event-dispatch callback in this interceptor. [Source](https://github.com/oxface/AlCopilot/blob/f67696ee68b7ff49554d28094584c6bd178e4e44/server/src/AlCopilot.Shared/Data/DomainEventInterceptor.cs#L8-L27) | Preserve explicit sync support or explicitly reject it. Do not silently bypass required reactions. |
| Failed handling and retry | Every collected aggregate buffer is cleared before the first handler. If handler A updates tracked rows and handler B throws, the facts are already absent. Retrying that same context cannot replay the whole required chain from those buffers. The record rows also describe the entire batch before completion. [Source](https://github.com/oxface/AlCopilot/blob/f67696ee68b7ff49554d28094584c6bd178e4e44/server/src/AlCopilot.Shared/Data/DomainEventInterceptor.cs#L49-L81) | Retain handled-event cursors and reject reuse after failure. A fresh operation remains the recovery boundary. Buffer retention alone cannot undo mutations from earlier handlers. |
| Cascade limit | The depth check precedes the empty-batch check. After five nonempty waves, the next iteration throws even if the fifth produced no further events. A single wave can also contain arbitrarily many handler invocations. [Source](https://github.com/oxface/AlCopilot/blob/f67696ee68b7ff49554d28094584c6bd178e4e44/server/src/AlCopilot.Shared/Data/DomainEventInterceptor.cs#L12-L41) | Keep a configurable handler-invocation ceiling, checking completion correctly. |
| Transaction boundary | Dispatch happens inside the saving callback, with no explicit transaction creation or requirement in the interceptor. Tracked changes saved together can use native EF atomic persistence; immediate SQL, another context or external I/O in a handler is not made atomic by this code. [Source](https://github.com/oxface/AlCopilot/blob/f67696ee68b7ff49554d28094584c6bd178e4e44/server/src/AlCopilot.Shared/Data/DomainEventInterceptor.cs#L14-L24) | Retain explicit transaction ownership around dispatch and save. Local dispatch does not establish external delivery durability. |
| Module boundaries | Dispatch resolves handlers by event type from the shared service provider, without a module key. A handler registered by another module for that same public event type would also run. This is a possible use of the API, not observed cross-module handling. [Source](https://github.com/oxface/AlCopilot/blob/f67696ee68b7ff49554d28094584c6bd178e4e44/server/src/AlCopilot.Shared/Data/DomainEventDispatcher.cs#L42-L51) | Preserve explicit module scoping for required local reactions. Public event visibility alone must not imply an integration subscription. |
| Missing handlers | An empty handler collection succeeds. This is intentional in the integration fixture, which tests event persistence with no handlers. [Dispatcher](https://github.com/oxface/AlCopilot/blob/f67696ee68b7ff49554d28094584c6bd178e4e44/server/src/AlCopilot.Shared/Data/DomainEventDispatcher.cs#L48-L51), [fixture](https://github.com/oxface/AlCopilot/blob/f67696ee68b7ff49554d28094584c6bd178e4e44/server/tests/AlCopilot.DrinkCatalog.IntegrationTests/Integration/PostgresFixture.cs#L15-L26) | Decide explicitly whether an event is an optional notification or requires a registered reaction. Gloaming currently fails closed for unhandled events; changing that policy is separate from simplifying DI. |
| Recording versus delivery | `DomainEventRecord` contains event metadata and payload, but no pending/processed state, retry information or delivery identity. The interceptor adds it before dispatch. It is not an outbox delivery protocol. [Record](https://github.com/oxface/AlCopilot/blob/f67696ee68b7ff49554d28094584c6bd178e4e44/server/src/AlCopilot.Shared/Data/DomainEventRecord.cs), [interceptor](https://github.com/oxface/AlCopilot/blob/f67696ee68b7ff49554d28094584c6bd178e4e44/server/src/AlCopilot.Shared/Data/DomainEventInterceptor.cs#L61-L81) | Keep optional event recording and durable delivery separate from the local dispatcher. Do not require event names, timestamps, GUID aggregate IDs or a recording table for local handling. |

A parameterless-looking `Process()` call that merely drains handlers before
`base.SaveChangesAsync()` would hide an important distinction. It can stage changes, but
it cannot by itself observe whether the subsequent save or transaction commit succeeds.
The existing coordinator wraps native persistence to manage that lifecycle. Simplify its
consumer surface and hide the session first; replacing it with separate processing and
acknowledgment calls would need an equivalent owner for the save lifecycle. This is a
design inference from the requirements, not a claim about an EF limitation.

Clearing events before dispatch is not inherently incorrect if every failure discards the entire
operation and retries rebuild fresh state. The unsafe scenario is retrying the same tracker after
partial handling. Do not infer durability from retaining buffers either: neither approach can undo
an already committed external effect. Preserve or explicitly revise the selected recovery contract.

## Another Domain helper to avoid copying unchanged

The generic ValueObject<T> compares the wrapped value without checking the concrete domain type.
An isolated package-free executable compiled the unmodified ValueObject, DrinkName and TagName
files from the pinned checkout. For DrinkName.Create("same") and TagName.Create("same"), it printed
different runtime types = true, DrinkName.Equals(TagName) = true, and matching hash codes = true.
This is an executed observation, not merely an inferred edge case.
([Equality base](https://github.com/oxface/AlCopilot/blob/f67696ee68b7ff49554d28094584c6bd178e4e44/server/src/AlCopilot.Shared/Domain/ValueObject.cs),
[DrinkName](https://github.com/oxface/AlCopilot/blob/f67696ee68b7ff49554d28094584c6bd178e4e44/server/src/Modules/AlCopilot.DrinkCatalog/Features/Drink/DrinkName.cs),
[TagName](https://github.com/oxface/AlCopilot/blob/f67696ee68b7ff49554d28094584c6bd178e4e44/server/src/Modules/AlCopilot.DrinkCatalog/Features/Tag/TagName.cs).)

If domain-specific value types are meant to distinguish those concepts, equality should preserve
that type distinction. Existing nominal records or a base that includes concrete type identity
can provide it. This is a reason to compare individual Domain helpers rather than transplant the
entire folder. The temporary harness and output are under /tmp/alcopilot-value-equality-proof;
neither repository's runtime files were changed.

## Evidence limits and next step

The reviewed AlCopilot source tree contains the generic handler interface and dispatcher,
but the search found no concrete `IDomainEventHandler<T>` implementation or registration.
Its event-record integration tests assert persisted creation, deletion and update records;
their fixture explicitly dispatches to zero handlers. Those are useful tests of the
reference's event recording, but they do not demonstrate cascading business reactions,
handler failures, cross-module isolation or transaction rollback of handler effects.
([Handler contract](https://github.com/oxface/AlCopilot/blob/f67696ee68b7ff49554d28094584c6bd178e4e44/server/src/AlCopilot.Shared/Domain/IDomainEventHandler.cs),
[record tests](https://github.com/oxface/AlCopilot/blob/f67696ee68b7ff49554d28094584c6bd178e4e44/server/tests/AlCopilot.DrinkCatalog.IntegrationTests/Integration/DomainEventRecordIntegrationTests.cs#L26-L95),
[fixture](https://github.com/oxface/AlCopilot/blob/f67696ee68b7ff49554d28094584c6bd178e4e44/server/tests/AlCopilot.DrinkCatalog.IntegrationTests/Integration/PostgresFixture.cs#L15-L26).)

Initial recommendation, superseded by the owner-selected D2.1d interceptor: move standalone event/context registration into sample
module extensions; reduce the directly mapped DbContext to options plus one registered
save coordinator; leave operation sinks with the mapped-domain application adapter; keep
provider/scope construction in the executable host or test harness. Review that consumer
shape before introducing interception, new base contexts, repository abstractions or
serialized event storage. These are proposed changes; this research changed no runtime code.

The [concrete composition follow-up](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/d2-1d-module-owned-event-composition.md) shows the
implemented module extension, native scope and options-only DbContext shape. No Gloaming or AlCopilot
project test suite was executed for this comparison; the isolated equality check is the only new
runtime execution and establishes no dispatch/transaction guarantee.

Source retrieval used a read-only GitHub API download into `/tmp/alcopilot-reference`.
The public web reader returned a cache miss; shell network access required an approved
read-only escalation. No credential values were read or printed, and AlCopilot was not
modified.
