# EF1: external domain-event and EF integration patterns

Research date: 2026-10-15. This is the external-source part of
[EF1](../plans/ef1-capability-interception-research.md). It compares public implementation
sources, rather than counting tutorials or claiming a representative survey of all .NET
applications. No external project's tests were executed. No new reusable mechanism was
proven by this source review. The companion [Gloaming/native EF investigation](ef1-capability-interception-research.md)
records its separately executed probes and integration recommendation.

## Finding

AlCopilot's aggregate buffer, deferred dispatch and before-save interceptor form a
recognizable implementation of an established pattern. Microsoft documents deferred
domain-event dispatch before persistence for related changes made through one scoped
context. Jason Taylor's template implements before-save interception. Other projects make
different lifecycle choices: Ardalis dispatches after a successful save, ABP completes an
event loop within its unit of work, and MassTransit persists outgoing messages for later
delivery. These are useful comparisons, not interchangeable guarantees.
([Microsoft's domain-event guidance](https://learn.microsoft.com/en-us/dotnet/architecture/microservices/microservice-ddd-cqrs-patterns/domain-events-design-implementation),
sources and qualifications below.)

There is support for simplifying Gloaming toward AlCopilot's lifecycle. There is no reason
to copy its async-only path, global handler routing, mandatory event records or exact
depth check. Its bounded rescan loop is more complete for cascades than a single snapshot.
The earlier [AlCopilot comparison](al-copilot-domain-event-design-comparison.md) contains
the pinned source evidence and recovery limitations.

## Before-save interception: Jason Taylor's CleanArchitecture

At commit `12acea3cc12c06b65f1109f5f02475abb9984698`,
`DispatchDomainEventsInterceptor` handles both `SavingChanges` and
`SavingChangesAsync`. It snapshots events from tracked `BaseEntity` objects, clears all
matching buffers and publishes the snapshot through MediatR before native persistence.
There is no rescan loop or invocation ceiling in this interceptor. Newly raised events
therefore are not drained by another wave of this callback. The sync callback blocks on
the async dispatcher with `GetAwaiter().GetResult()`, rather than providing synchronous
handler execution. The asynchronous callback does not pass its cancellation token to
`Publish`.
([Pinned interceptor](https://github.com/jasontaylordev/CleanArchitecture/blob/12acea3cc12c06b65f1109f5f02475abb9984698/src/Infrastructure/Data/Interceptors/DispatchDomainEventsInterceptor.cs#L17-L48).)

Infrastructure registers auditing and event interceptors as scoped
`ISaveChangesInterceptor` services, then attaches that service collection in DbContext
options. This demonstrates central composition and ordinary options-only persistence;
the shown registration does not select capabilities from model annotations or qualify
handlers by module. It registers auditing before event dispatch, so its order should not
be copied for a Gloaming requirement that validation and stamping observe handler-created
changes.
([Pinned registration](https://github.com/jasontaylordev/CleanArchitecture/blob/12acea3cc12c06b65f1109f5f02475abb9984698/src/Infrastructure/DependencyInjection.cs#L20-L25).)

Inference: clearing before handlers can be a valid operation contract when failure always
discards the tracker. This source does not establish recovery by retrying the same
context, transaction ownership for immediate handler SQL, or durable external delivery.

## After-save interception: Ardalis CleanArchitecture

At commit `e7c79be54ba0b001a5b332c1efe935f520b9db4b`, the main template's
`EventDispatchInterceptor` implements `SavedChangesAsync`. It collects tracked objects
with events and delegates to `IDomainEventDispatcher.DispatchAndClearEvents`. There is
no cascade loop in this interceptor. Exact clearing order belongs to that external
dispatcher and was not verified here. Scoped interceptor and dispatcher registration
live in infrastructure composition and are attached to `AppDbContext` options.
([Pinned interceptor](https://github.com/ardalis/CleanArchitecture/blob/e7c79be54ba0b001a5b332c1efe935f520b9db4b/src/Clean.Architecture.Infrastructure/Data/EventDispatcherInterceptor.cs#L5-L28),
[registration](https://github.com/ardalis/CleanArchitecture/blob/e7c79be54ba0b001a5b332c1efe935f520b9db4b/src/Clean.Architecture.Infrastructure/InfrastructureServiceExtensions.cs#L27-L46).)

The context redirects parameterless synchronous `SaveChanges()` to its asynchronous path
by blocking. It does not override the synchronous boolean overload in the inspected file.
This is not complete synchronous coverage for Gloaming's supported native overloads.
([Pinned context](https://github.com/ardalis/CleanArchitecture/blob/e7c79be54ba0b001a5b332c1efe935f520b9db4b/src/Clean.Architecture.Infrastructure/Data/AppDbContext.cs#L14-L15).)

Inference: a successful native save is distinct from a surrounding caller transaction
committing. An after-save handler's new tracked mutations also need another persistence
operation. This template's lifecycle is unsuitable as a direct replacement for required
reactions that must be staged before Gloaming validates and saves their combined changes.

## A cascading operation boundary: ABP

At commit `6d1ed040293493525033dfdede6df7c2b5526f58`, `AbpDbContext.SaveChangesAsync`
applies framework concerns, creates an event report, calls native persistence, then
queues its reported events onto the current unit of work. `CreateEventReport` snapshots
tracked objects' local and distributed events and clears their buffers before native
save. Thus the report exists in memory before saving, while its transfer to the unit of
work follows successful saving.
([Pinned save implementation](https://github.com/abpframework/abp/blob/6d1ed040293493525033dfdede6df7c2b5526f58/framework/src/Volo.Abp.EntityFrameworkCore/Volo/Abp/EntityFrameworkCore/AbpDbContext.cs#L203-L227),
[queue transfer](https://github.com/abpframework/abp/blob/6d1ed040293493525033dfdede6df7c2b5526f58/framework/src/Volo.Abp.EntityFrameworkCore/Volo/Abp/EntityFrameworkCore/AbpDbContext.cs#L325-L341),
[buffer collection](https://github.com/abpframework/abp/blob/6d1ed040293493525033dfdede6df7c2b5526f58/framework/src/Volo.Abp.EntityFrameworkCore/Volo/Abp/EntityFrameworkCore/AbpDbContext.cs#L556-L601).)

`UnitOfWork.CompleteAsync` first saves, then repeatedly snapshots and clears queued
local/distributed events, publishes them in event order, and saves again. Newly queued
events feed the next iteration. It commits transactions only after the queues drain.
This loop has no explicit iteration ceiling in the inspected method. The inspected
completion API is asynchronous; it is not an example of native sync interception.
([Pinned operation loop](https://github.com/abpframework/abp/blob/6d1ed040293493525033dfdede6df7c2b5526f58/framework/src/Volo.Abp.Uow/Volo/Abp/Uow/UnitOfWork.cs#L128-L183).)

ABP therefore supplies a useful precedent for an operation-level coordinator beyond
aggregate buffers, including cascading events and explicit transaction completion. Its
ambient unit of work and repeated persistence differ materially from AlCopilot's single
before-save drain. Adopting that framework boundary would add scope Gloaming has not
requested. Local and distributed queues are separate; their names alone do not establish
outbox use for a particular deployment. ABP's local bus guidance places entity-event
handling before transaction completion.
([Local event bus](https://abp.io/docs/latest/framework/infrastructure/event-bus/local),
[unit-of-work contract](https://abp.io/docs/latest/framework/architecture/domain-driven-design/unit-of-work).)

## Durable delivery and context selection: MassTransit

MassTransit's documented EF outbox setup registers `AddEntityFrameworkOutbox<TDbContext>`
and `UseBusOutbox`, and separately configures inbox/outbox entities in the context model.
The documentation explicitly configures each participating context. This is a useful
precedent for context-specific capability registration plus model setup; it does not
demonstrate model annotations automatically installing interceptors.
([Official outbox configuration](https://masstransit.massient.com/configuration/middleware/outbox).)

At commit `d9f61f764c802785cc9a4be92547d8f5b63018d4`, bus-outbox configuration replaces
the scoped bus-context provider with an EF-context-specific implementation and registers
a delivery service. Scoped publish/send endpoints enqueue message/state entities in that
context. The background delivery service separately reads persisted messages and sends
them, with its own database transaction and save/commit logic. This path uses endpoint
decoration and delivery infrastructure, rather than draining aggregate events from a
SaveChanges interceptor.
([Pinned configuration](https://github.com/MassTransit/MassTransit/blob/d9f61f764c802785cc9a4be92547d8f5b63018d4/src/Persistence/MassTransit.EntityFrameworkCoreIntegration/Configuration/Configuration/EntityFrameworkBusOutboxConfigurator.cs#L46-L67),
[scoped message capture](https://github.com/MassTransit/MassTransit/blob/d9f61f764c802785cc9a4be92547d8f5b63018d4/src/Persistence/MassTransit.EntityFrameworkCoreIntegration/EntityFrameworkCoreIntegration/EntityFrameworkScopedBusContext.cs#L61-L97),
[delivery](https://github.com/MassTransit/MassTransit/blob/d9f61f764c802785cc9a4be92547d8f5b63018d4/src/Persistence/MassTransit.EntityFrameworkCoreIntegration/EntityFrameworkCoreIntegration/BusOutboxDeliveryService.cs#L101-L165).)

The lesson is to keep local required reactions, optional event recording and durable
external delivery as separate capabilities. An interceptor can prepare an outbox row;
it is not the delivery protocol. MassTransit does not imply that every Gloaming concern
should become an interceptor.

## Implications for Gloaming

These are research recommendations, not reviewed interface commitments:

- Prefer a small before-save drain for local events, with a bounded rescan, sequential
  handling and a documented fresh-operation recovery contract. Keep module-qualified
  routing and the operation sink needed by immutable, separately mapped domain objects.
- Define preparation, event dispatch, change detection, validation and completion as
  deliberate phases. A context-specific registration extension can own Gloaming's order;
  it cannot dictate all callbacks installed independently by consumers or other packages.
- Model declarations can describe context capabilities. Keep runtime service setup
  explicit enough to supply handler services, scoped state and delivery workers. None of
  the selected sources proves that `OnModelCreating` should mutate service registration.
- Solve sync explicitly: implement real synchronous handlers where available, or reject
  sync use for configurations requiring async work. The templates' blocking adaptations
  demonstrate a common compromise, not a reason to adopt it as Gloaming's default.
- Research a shared ordered composition mechanism before migrating every existing
  validation utility. Avoid adding a broad interceptor framework merely because EF
  exposes many interception interfaces.

Verification: inspected the pinned files above and their public repository trees;
searched official documentation for comparable patterns. Sources were downloaded
read-only into `/tmp/ef1-external-patterns` for line-level inspection. No runtime files,
external repositories, staged snapshots or Git metadata were changed by this research.
Existing Gloaming test results do not verify any external project's behavior.
