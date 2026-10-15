# EF1: MassTransit patterns applicable to existing Gloaming capabilities

Research date: 2026-10-15. This extends the [external-pattern comparison](ef1-external-patterns.md)
for the owner-requested EF integration cleanup. Scope is current EF persistence guards,
local domain-event dispatch, inbox/outbox and their existing optional workers. Transport
management, saga frameworks and additional messaging features are outside this assessment.

The useful lesson is context-specific composition with explicit operation boundaries.
MassTransit does not demonstrate a universal SaveChanges interceptor or model-driven
automatic worker registration. Borrow its separation of concerns without importing its
bus abstraction, transactional consumer pipeline or delivery protocol.

## Evidence and limits

Source inspection uses MassTransit commit `d9f61f764c802785cc9a4be92547d8f5b63018d4`,
downloaded read-only under `/tmp/ef1-external-patterns`. Additional source files were
downloaded read-only to `/tmp/ef1-masstransit-*`. Current official documentation was checked
separately; it contains versioned features that must not be attributed to the pinned
implementation. External tests were read selectively and were not executed. This research
adds no runtime implementation or tests and proves no new reusable mechanism. Gloaming
test references below describe existing coverage, not fresh executions.

## Adopt in this increment

### Separate declarations, runtime composition and execution

MassTransit registers `AddEntityFrameworkOutbox<TDbContext>`, selects a database provider
and optionally enables its bus outbox in DI. Model configuration separately adds the inbox,
outbox-message and outbox-state entities. Consumer interception is another explicit
receive-endpoint selection. These are distinct configuration sites; model construction
does not register scoped services or start workers.
([Pinned registration/model extensions](https://github.com/MassTransit/MassTransit/blob/d9f61f764c802785cc9a4be92547d8f5b63018d4/src/Persistence/MassTransit.EntityFrameworkCoreIntegration/Configuration/EntityFrameworkOutboxConfigurationExtensions.cs),
[pinned configurator](https://github.com/MassTransit/MassTransit/blob/d9f61f764c802785cc9a4be92547d8f5b63018d4/src/Persistence/MassTransit.EntityFrameworkCoreIntegration/Configuration/Configuration/EntityFrameworkOutboxConfigurator.cs).)

For Gloaming, `ConfigureGloamingCapabilities` can be broader than interceptor installation
while retaining those boundaries. Existing model helpers declare the capabilities and
storage shape for that context. Module registration supplies scoped handlers, sinks and
adapters. An options extension composes the selected adapters in Gloaming's defined order.
Cached model metadata must contain declarations, not scoped providers or handler instances.
This is a Gloaming design inference, not a MassTransit API we can copy directly.

Capability integration must preserve the owning operation: existing enqueue does not save
or commit; inbox processing owns its separate processing transaction; a local domain handler
stages required local changes. Installing a save callback must not start delivery or move
ownership of an existing native transaction.

### Keep context identity explicit and use the completed model

Current MassTransit documentation explicitly configures every participating DbContext and
offers `IScopedBusOutbox<TDbContext>` when an application has multiple candidate outboxes.
Its unqualified endpoint APIs otherwise require a default selection. Gloaming already uses
`IOutbox<TDbContext>`, `IInbox<TDbContext>` and typed dispatchers/processors; preserve that
explicitness instead of adding an ambient default context.
([Official multiple-context setup](https://masstransit.massient.com/configuration/middleware/outbox#multiple-dbcontext-types),
[Gloaming contracts](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore/README.md).)

A concrete caution comes from the pinned `SqlLockStatementProvider`: its static schema/table
cache is keyed by entity CLR type. Two models mapping the same provided record to different
schemas can therefore reuse the first mapping. The source supplies a switch to disable that
cache; the current documentation explains when to do so. A Gloaming capability-plan cache
must use the actual completed model, or avoid caching until it is necessary. A shared
`OutboxMessageRecord` CLR type is insufficient identity.
([Pinned cache](https://github.com/MassTransit/MassTransit/blob/d9f61f764c802785cc9a4be92547d8f5b63018d4/src/Persistence/MassTransit.EntityFrameworkCoreIntegration/EntityFrameworkCoreIntegration/SqlLockStatementProvider.cs),
[official multiple-context warning](https://masstransit.massient.com/configuration/middleware/outbox#multiple-dbcontext-types).)

Gloaming's current PostgreSQL dispatcher derives its table from its own context model in
the constructor, without that static CLR-type cache. Existing
`TypedModuleRegistrationsAndQuotedTablesRemainIndependent` covers distinct contexts with
different, quoted mappings. The common integration needs an equivalent isolation proof
for capability selection and runtime service resolution.
([Dispatcher](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/PostgresOutboxDispatcher.cs),
[existing registration coverage](../../src/Gloaming.Messaging/tests/OutboxPostgresTests/RegistrationTests.cs).)

### Make registration composition deliberate

The pinned MassTransit configurator uses `TryAddScoped` for its context-specific outbox
factory, options configuration for provider settings, and separate hosted-service
registration. Its bus-outbox setup replaces a scoped endpoint provider. This is evidence
of explicit composition decisions, not evidence that every repeated registration is
idempotent or that conflicting configuration always fails.
([Pinned factory registration](https://github.com/MassTransit/MassTransit/blob/d9f61f764c802785cc9a4be92547d8f5b63018d4/src/Persistence/MassTransit.EntityFrameworkCoreIntegration/Configuration/Configuration/EntityFrameworkOutboxConfigurator.cs),
[pinned bus-outbox registration](https://github.com/MassTransit/MassTransit/blob/d9f61f764c802785cc9a4be92547d8f5b63018d4/src/Persistence/MassTransit.EntityFrameworkCoreIntegration/Configuration/Configuration/EntityFrameworkBusOutboxConfigurator.cs).)

For Gloaming, attaching the common save integration twice must not execute handlers or
validators twice. Conflicting bindings for one declared capability should have a deliberate
outcome. Do not infer registration from a referenced assembly or from an unrelated context.
The initial integration should test repeated attachment and missing runtime bindings while
retaining ordinary native DI validation. No generic configuration-validation framework is
needed merely to obtain these checks.

## Already satisfied: retain these choices

**Enqueue and delivery are separate.** MassTransit's scoped bus context adds durable entities
through the application context; its delivery service later opens a fresh scope, resolves
the owning context, starts its own transaction and delivers persisted work. This supports
Gloaming's existing separately callable dispatch and independently selected worker. Gloaming
already permits an API-only producer and a separate worker host, so no new mechanism is
needed here.
([Pinned enqueue](https://github.com/MassTransit/MassTransit/blob/d9f61f764c802785cc9a4be92547d8f5b63018d4/src/Persistence/MassTransit.EntityFrameworkCoreIntegration/EntityFrameworkCoreIntegration/EntityFrameworkScopedBusContext.cs),
[pinned delivery service](https://github.com/MassTransit/MassTransit/blob/d9f61f764c802785cc9a4be92547d8f5b63018d4/src/Persistence/MassTransit.EntityFrameworkCoreIntegration/EntityFrameworkCoreIntegration/BusOutboxDeliveryService.cs),
[Gloaming worker composition](../../src/Gloaming.Messaging/docs/capabilities.md#separate-worker-composition).)

**A save is not transaction commit.** The pinned consumer-outbox factory begins its native
transaction, runs processing and saves, then explicitly commits; save and commit failures
remain separate. Its bus-outbox scoped context calls a method named `WasCommitted` that
actually checks whether the tracked state entity is `Unchanged`. That is an acceptance
signal, not proof that an outer transaction committed. Gloaming should keep native caller
commit visible and avoid using `SavedChanges` or entity state as durable-delivery authority.
([Pinned processing boundary](https://github.com/MassTransit/MassTransit/blob/d9f61f764c802785cc9a4be92547d8f5b63018d4/src/Persistence/MassTransit.EntityFrameworkCoreIntegration/EntityFrameworkCoreIntegration/EntityFrameworkOutboxContextFactory.cs),
[pinned tracked-state signal](https://github.com/MassTransit/MassTransit/blob/d9f61f764c802785cc9a4be92547d8f5b63018d4/src/Persistence/MassTransit.EntityFrameworkCoreIntegration/EntityFrameworkCoreIntegration/EntityFrameworkScopedBusContext.cs).)

**Fresh scopes and explicit failure contracts matter more than automatic retry.** Gloaming's
worker already resolves each dispatch attempt in a fresh scope, validates dependencies
outside the operational retry catch, and propagates cooperative cancellation. Its inbox
retains intake independently of processing. These are current contracts to preserve; they
do not require adopting MassTransit's receive pipeline or EF execution-strategy retry loop.
([Current worker](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore/OutboxWorker.cs),
[current inbox contract](../../src/Gloaming.Messaging/docs/inbox.md).)

## Defer or reject within this slice

| Candidate | Assessment within current scope |
| --- | --- |
| Worker notification after enqueue/save | MassTransit combines a local wake signal with timed polling. This can reduce same-process latency, but a signal is neither commit proof nor cross-process delivery. Gloaming already supports separate hosts; adopt only after measuring polling latency/load and preserving the poll fallback. |
| Delivery batching, adaptive polling and compiled queries | The pinned worker uses all three. They address throughput and allocation costs; no Gloaming measurement currently justifies changing its bounded one-message dispatch contract. |
| Replacing leases with a transaction held during send | The pinned worker delivers inside a database transaction. Gloaming deliberately commits its claim before transport work and fences completion afterward. Copying the transaction shape would change contention and failure semantics; retain the existing protocol. |
| Inbox receipt cleanup/duplicate window | MassTransit exposes a cleanup window. Gloaming retains queued intake and rejects identity/content conflicts. Cleanup would change deduplication guarantees and requires the already-deferred retention policy. |
| Transparent endpoint decoration | Useful when a framework owns send/publish APIs. Gloaming deliberately receives an explicit outgoing envelope through `IOutbox<TDbContext>`; no bus facade is needed. |
| General retry framework | Local clear-before-dispatch handlers require rollback/disposal after failure. Automatic retry of the same save/context cannot reconstruct a consumed domain buffer and business decision. Keep whole-operation retry consumer-owned. |
| Automatic workers inferred from model capabilities | A model can declare outbox storage in an API-only host. Worker selection remains a separate host decision. |
| Async-only interception | MassTransit's inspected durable paths are async APIs. They do not solve Gloaming's native synchronous-save contract. Implement both paths and explicitly reject async-only work from a sync save. |

The notification, batching and transaction observations come from the
[pinned delivery service](https://github.com/MassTransit/MassTransit/blob/d9f61f764c802785cc9a4be92547d8f5b63018d4/src/Persistence/MassTransit.EntityFrameworkCoreIntegration/EntityFrameworkCoreIntegration/BusOutboxDeliveryService.cs)
and [notification implementation](https://github.com/MassTransit/MassTransit/blob/d9f61f764c802785cc9a4be92547d8f5b63018d4/src/MassTransit/Middleware/Outbox/BusOutboxNotification.cs).
Cleanup configuration appears in the [pinned configurator](https://github.com/MassTransit/MassTransit/blob/d9f61f764c802785cc9a4be92547d8f5b63018d4/src/Persistence/MassTransit.EntityFrameworkCoreIntegration/Configuration/Configuration/EntityFrameworkOutboxConfigurator.cs).
Gloaming's existing [capability record](../../src/Gloaming.Messaging/docs/capabilities.md)
documents lease behavior, fresh-operation retries and deferred retention/performance work.

## Result for the current implementation

Adopt context-specific, model-aware composition; preserve explicit scoped ownership and
separate worker registration; test duplicate installation and module/model isolation.
Simplify local domain-event handling independently of durable messaging. Gloaming's existing
inbox/outbox storage and delivery contracts need no redesign for this increment. A common
capability package can coordinate existing save preparation and validation without becoming
a bus, transaction owner, repository, or worker framework.

Verification for this report: inspected the pinned source files, current official outbox
documentation, current Gloaming messaging implementations and named test coverage. No
external tests, benchmarks, new failure probes or Gloaming runtime tests were executed by
this research task. Library adoption remains subject to the parent increment's executable
consumer proofs. No template change is established by this source review; module-specific
envelopes, handler business rules and transport acceptance remain consumer policy.
