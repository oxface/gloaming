# Gloaming.EventReactions

Platform-only event buffers and ordered reaction dispatch. No EF, messaging, DI, identity or tenancy
dependency. Consumers define ordinary event types; they require no marker or inheritance.

```csharp
public sealed record ItemAccepted(Guid Id);

var pending = new PendingEvents();
var registry = new EventReactionRegistry<ImportScope>();
registry.Add<ItemAccepted>(new RecordAcceptedItem());
var dispatcher = new EventReactionDispatcher<ImportScope>(registry, pending);

pending.Raise(new ItemAccepted(id));
await dispatcher.DispatchAsync(() => otherBuffers, cancellationToken);
// The caller owns validation, persistence and commit.
```

IEventReaction<TEvent> supplies HandleAsync; ISynchronousEventReaction<TEvent> optionally supplies
Handle for synchronous dispatch. Register explicit instances before constructing the dispatcher.
Registrations freeze at construction. Bindings match the exact CLR type and execute serially in
registration order. Missing handlers fail. The configurable positive ceiling defaults to 128
reaction invocations per dispatch. Sync rejects an async-only binding without blocking.

PendingEvents implements IEventSink. Any object can own one; there is no supplied AggregateRoot or
required source interface. The constructor-supplied operation buffer is included automatically.
Dispatcher.Sink and PendingCount expose that buffer only. Additional buffers come from the supplied
collector on every wave. The optional EF adapter exposes IEventSink<TModule> in the operation scope.

Each Raise captures a distinct occurrence with an internal process-local sequence. Collected waves
preserve capture order across sources and the operation sink. Equal payloads and the same object
raised twice remain separate occurrences. Duplicate buffers in a wave are collected once. Each
batch clears before handling; downstream facts and newly exposed sources join subsequent waves.
Expose every current participating buffer before dispatch; later discovery cannot reorder completed
work. The sequence is neither persisted nor a distributed order or domain version.

Repeated dispatch handles newly raised facts only. Handler/collection failure, cancellation, exceeding
the ceiling and nested dispatch fault the dispatcher. Roll back and discard the operation; reconstruct
state and re-decide. Clear-before-handling is not acknowledgment or durability, and a later persistence
failure does not restore consumed facts. Cancellation is cooperative and cannot interrupt a hung
reaction. Buffers, dispatchers and handlers are not thread safe.

The core does not save, validate, own transactions, expose Stage or retain handled history. Use
explicit application orchestration for complex business dependencies. See the
[lifecycle contract](../docs/local-dispatch.md) and
[optional EF integration](../Gloaming.EventReactions.EntityFrameworkCore/README.md).
