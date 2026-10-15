# D2.1d: module-owned domain-event composition

Current review entry: [EventReactions consolidation](../reports/event-reactions-consolidation.md)
supersedes the earlier package names, aggregate base and marker requirements. Descriptions and
test counts below retain their dated scope; source links lead to current replacements.

Historical record: the owner subsequently authorized [EF1.1](../reports/ef1-1-capability-composition.md)
common capability composition and clear-before-handling dispatch. That implementation supersedes
retained sessions, Stage, owned transactions and completion enforcement described below. Source
links now lead to replacements; test counts below remain dated evidence.

Date: 2026-10-15. Owner-authorized cleanup, implemented for review. The owner selected an EF
interceptor with both synchronous and asynchronous paths, superseding D2.1c's explicit save service
and the initial proposal in this document. [Implementation evidence](../reports/d2-1d-module-owned-event-composition.md)
records verification separately from earlier results.

## Consumer shape

A module's IServiceCollection extension registers its native DbContext, typed handlers,
application adapters and Gloaming AddDomainEvents. The host calls module extensions, builds one
provider and uses ordinary native scopes. DirectOrdersContext and MappedQuotesContext take only
native options; they contain no event session properties, handler composition or save overrides.
MappedQuoteCommands receives the mapped context and IDomainEventsSink<MappedQuotesModule>, applies
accepted immutable decisions and raises their facts. ConsumerScope and generic sample handler
closures are removed. Test-specific async-only handlers and fault probes stay in test composition.

```csharp
var services = new ServiceCollection();
services.AddDirectOrdersModule(connection);
services.AddMappedQuotesModule(connection);
await using var provider = services.BuildServiceProvider();
await using var operation = provider.CreateAsyncScope();
var database = operation.ServiceProvider.GetRequiredService<DirectOrdersContext>();
```

AddDomainEvents installs a scoped DomainEventSaveChangesInterceptor<TContext,TModule> using native
ConfigureDbContext. Deferred handler construction avoids context-construction cycles. The core
has no EF or DI dependency. SaveChanges/SaveChangesAsync drain bounded downstream facts before
native persistence; completion/failure/cancellation hooks manage the event lifecycle. Native
concurrency failures are also observed through DbContext.SaveChangesFailed.

## Deliberate lifecycle changes

The old IDomainEventSave, RegisteredDomainEventSave and EfDomainEventSave implementation are removed.
The core exposes split BeginSave/BeginSaveAsync, CheckSave and CompleteSave hooks for persistence
callbacks, sharing the same lifecycle used by its direct Save/SaveAsync methods. Fail invalidates
the operation and retains pending buffers. These hooks are adapter-facing, not domain policy.

Use native ChangeTracker.AcceptAllChanges after SaveChanges(false). No parallel acceptance flag or
custom acknowledgment wrapper remains. Native misuse can discard unsaved writes or repeat SQL;
this is a documented boundary, not another safeguard around EF.

Sales' existing tenant/audit/inbox/outbox/workflow checks move to a module-local interceptor appended
in OnConfiguring. They therefore inspect handler-created writes as well as original command writes.
Other Sales compositions retain those checks without opting into domain events. No new generic
validation interface, repository, unit of work, event log or base context is introduced.

Caller transactions remain the default and are never committed by Gloaming. Optional CreateIfAbsent
commits only its own transaction at its completion hook. Interception cannot surround callbacks
that run later: use a caller transaction when commit must wait for the entire callback pipeline.
After any save error, discard the operation and roll back/dispose its transaction. A later saving
callback can throw outside EF's failure hooks; scope disposal releases remaining owned resources.
The [package contract](../../src/Gloaming.EventReactions/docs/local-dispatch.md) states these limits.

## Scope and proof obligations

Preserve staged owner work unchanged. Reuse real PostgreSQL proofs for both persistence shapes,
all save overloads, cascades, repeated staging, deletion/detachment, native version conflicts,
required-write failures, deferred commit rejection, cancellation and fresh-state recovery.
Add targeted proofs for split lifecycle, native sync failure, cancellation during native saving,
concurrency failure invalidation and caller rollback after later completion callbacks reject.
Module/handler policy, domain facts, mappings, keys and concurrency remain consumer-owned.

The [AlCopilot comparison](../reports/al-copilot-domain-event-design-comparison.md) supports module-owned
composition and options-only contexts; its async recording interceptor is not copied as a guarantee.
D2.2 durable deferred reactions remain separate. No template preset is added.
