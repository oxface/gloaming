# D2.1a: local domain-event extraction assessment

Current review entry: [EventReactions consolidation](../reports/event-reactions-consolidation.md)
supersedes the earlier package names, aggregate base and marker requirements. Descriptions and
test counts below retain their dated scope; source links lead to current replacements.

Historical record: the owner subsequently authorized [EF1.1](../reports/ef1-1-capability-composition.md)
common capability composition and clear-before-handling dispatch. That implementation supersedes
retained sessions, Stage, owned transactions and completion enforcement described below. Source
links now lead to replacements; test counts below remain dated evidence.

Historical interface/evidence: [D2.1d](../reports/d2-1d-module-owned-event-composition.md) supersedes the explicit save-service integration
and custom false-save acceptance described below. Source links for removed adapters/composition
now point to their current interceptor/module-registration replacements; earlier results remain dated.

Date: 2026-10-14. Owner-authorized follow-up to the proposed extraction of an event marker,
optional aggregate base, typed handlers, module registry, sink and callable SaveChanges utility.
This assessment preserves the staged D2.1 review snapshot and its Sales working files.

Current status, 2026-10-15: the owner authorized promoting this reviewed mechanism and naming into
Gloaming.Domain and its optional EF adapter. The [promotion report](../reports/d2-1b-gloaming-domain-promotion.md)
records the implementation and fresh checks; the [package-local contract](../../src/Gloaming.EventReactions/README.md)
is the current consumer entry point. Assessment-only language below records the earlier scope;
source links now point to the promoted implementation. No duplicate sample prototypes remain.

Further owner review on 2026-10-15 selected a registered EF adapter with one explicit native-save
call and removal of the older default Sales defensive protocol. The
[D2.1c integration follow-up](d2-1c-domain-event-integration-simplification.md) records that direction,
the proposed consumer shape and required proofs separately from the promoted implementation.

Owner review follow-up, 2026-10-15: recommend the family name `Gloaming.Domain` and optional
`Gloaming.Domain.EntityFrameworkCore`, replacing this assessment's initial DomainEvents package
names. The optional base is now `AggregateRoot` in the prototype and proposed interface.
Keep `IDomainEventsSink`: it accepts raised facts for later handling; `PendingDomainEvents` is
the concrete buffer. These names do not introduce other DDD abstractions or require inheritance.

## Review status and concrete scope

The [extraction plan](library-extraction.md#dependency-and-interface-review) requires concrete
interfaces, consumer usage and guarantees before a library increment. Repository guidance keeps
proposed interfaces here until owner review. Implement the candidate in standalone sample
prototype projects, not Gloaming packages; execute proofs before presenting promotion for review.
No archived code is imported. The owner's described prior usage is evidence of repeated need;
current D2.1 supplies fresh one-handler lifecycle evidence, not cascade/registry proofs.

The candidate removes module-specific event routing, repeated collection/draining, nested-save
protection, successful-save acknowledgment, and transaction/failed-context bookkeeping. Domain
events, subscriptions, required business effects, row mapping and validators remain consumer-owned.

Two independently composed order consumers use the same mechanism: directly EF-mapped mutable
aggregates with an optional base, and immutable domain decisions adapted to separate rows through
the operation sink. Required allowance reservation and submission receipts exercise two handlers
for a producer fact; reservation raises a further allocation fact. Deletion releases the accepted
allocation, demonstrating facts retained after a root leaves tracking. These are assessment
consumer policies, not extensions to Wholesale Sales.

## Proposed core interface

The public surface below was authorized for implementation with Gloaming namespaces/assembly identities.
The core has no packages, EF, DI, tenancy, actor, serialization or transport dependency.

```csharp
public interface IDomainEvent { }
public interface IDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    Task HandleAsync(TEvent fact, CancellationToken cancellationToken);
}
public interface ISynchronousDomainEventHandler<in TEvent> where TEvent : IDomainEvent
{
    void Handle(TEvent fact);
}
public interface IDomainEventsSink
{
    void Raise(IDomainEvent fact);
}
```

`PendingDomainEvents` is a package-free buffer with Raise and Count. `AggregateRoot` optionally
owns one buffer and exposes protected Raise; it imposes no keys, versions, mapping or domain rules.
Composition and immutable decision results work without that base.

`DomainEventRegistry<TModule>.Add<TEvent>(handler)` records exact-type bindings in explicit order.
Handlers are constructed in the consumer's operation scope; the registry captures no global DI
provider or assembly scan. A session freezes registration. Missing handlers and duplicate instance
registration fail. The module tag separates subscriptions, not database credentials/security.

`DomainEventSession<TModule>` exposes Sink, Track(buffer), Stage/StageAsync and Save/SaveAsync.
Save takes consumer collection/validation callbacks and a native-save delegate. It acknowledges
handled buffer prefixes only after that delegate succeeds. A collected FIFO wave runs all handlers
in registration order before the next wave; new buffers/facts are collected again. A positive
invocation ceiling bounds cascades. Failed handling, cancellation, nested execution or post-dispatch
facts during native saving fault the session; reuse requires a fresh session and operation.
Sync execution rejects async-only handlers, without sync-over-async. Ordinary core use need not
involve a DbContext or the template's module layout.

The cascade loop is in the core session's Drain/DrainAsync, called by both explicit staging and
the EF save adapter. It collects pending facts, invokes their handlers, then collects again,
including new facts on newly tracked roots and the sink. For example, submission -> allowance
reservation -> allocation recording completes before validation and one native EF save. Repeating
native SaveChanges is unnecessary for those staged changes. A handler cycle exceeds the configured
invocation ceiling and fails the operation. Facts raised inside native persistence callbacks are
rejected rather than starting another post-save dispatch cycle; handlers must raise domain facts
while making their domain decisions before persistence.

## Proposed EF adapter and consumer obligations

`EfDomainEventSave<TModule>` binds a native context, session, collect callback and validation
callback. Context overrides delegate both bool save families to its Save/SaveAsync methods and
pass base.SaveChanges delegates. Explicit Stage/StageAsync uses the same dispatch session.

Transaction modes: RequireExisting = 1 (default), CreateIfAbsent = 2. For pending facts, begin/join
the transaction before handling. The adapter never commits a caller transaction; an adapter-owned
transaction covers handling, validation and save, then commits and disposes. Explicit staging
with pending facts requires a caller transaction. It cannot retroactively include earlier writes
in a newly opened transaction. A participating context is bound to that transaction and discarded after it ends.
Failures require caller rollback/disposal for joined transactions or native disposal of an owned
transaction, then a fresh context/decision. No automatic retry or ambiguous-commit guarantee.

After SaveChanges(false), use the adapter's explicit AcceptAllChanges method before further work.
This deliberately avoids assuming a consumer's version-property name or inferring acceptance from
arbitrary tracked mutations. Validate after the complete handler cascade. Handlers stage changes;
nested native saves and external committed effects are outside the supported protocol. Raw SQL and
bulk operations bypass the hook. Callback mutation after validation is rejected where pending-fact
collection can detect it; the adapter is not a universal guard against arbitrary consumer code.

Example wiring (review sketch):

```csharp
var pending = new PendingDomainEvents();
var registry = new DomainEventRegistry<OrdersModule>();
registry.Add<OrderSubmitted>(new ReserveAllowance(context, pending));
registry.Add<OrderSubmitted>(new RecordSubmission(context));
registry.Add<AllowanceReserved>(new RecordAllocation(context));
var events = new DomainEventSession<OrdersModule>(registry, pending, maxHandlerInvocations: 128);
var save = new EfDomainEventSave<OrdersModule>(context, events, CollectSources, Validate);
// SaveChangesAsync(bool accept, CancellationToken ct) delegates to:
await save.SaveAsync(accept, () => base.SaveChangesAsync(accept, ct), ct);
```

Actual executable consumer wiring must compile. Buffers belong to one operation session; event
delivery is serial, and concurrent use of a session/context is unsupported.
Event visibility stays a consumer choice. Public types used across a module's assemblies do not
automatically become integration contracts; cross-module publication remains explicit mapping.

## Proof obligations and promotion gate

The [assessment report](../reports/d2-1a-domain-event-extraction.md) records new executions and
the extraction recommendation. The [consumer README](../../samples/EventReactionsDemo/README.md)
contains actual runnable usage. The owner-authorized promotion uses two projects in the
`src/Gloaming.Domain/` family: package-free `Gloaming.Domain` and
`Gloaming.Domain.EntityFrameworkCore` (core plus EF Relational only). No DI/transport,
event-store, serialization or application dependency is required. These are now repository library
projects; this does not imply publication to a package registry.

Prove multiple handlers, deterministic order, bounded cascades/cycles, newly tracked sources,
explicit sink input, deletion/detachment, module isolation, missing bindings, optional inheritance,
sync/async paths, false acceptance, repeated staging/saves, cancellation, native concurrency,
handler/save/commit faults, and caller versus adapter-owned transaction behavior on PostgreSQL.
Include standalone core usage without EF and independently expected business outcomes.

Keep prototype dependencies and consumers separate, wire build/test/CI, and present an exact public
interface plus evidence. Report what is proven versus proposed and what correctness work consumers
still own. Promotion to Gloaming and replacement of D2.1's reviewed consumer require owner review;
this assessment does not deliver D2.2 durable deferred reactions or a new template preset.

## Complete proposed public mechanism surface

The four interfaces above plus the following declarations describe the prototype's entire public
mechanism surface. Consumer business types and test harness interfaces are not library proposals.
Namespaces changed to the owner-selected Gloaming.Domain family. The actual files linked here provide line-by-line
implementation review: [core](../../src/Gloaming.EventReactions/Gloaming.EventReactions/EventReactionDispatcher.cs),
[registry](../../src/Gloaming.EventReactions/Gloaming.EventReactions/EventReactionRegistry.cs),
[buffer/base](../../src/Gloaming.EventReactions/Gloaming.EventReactions/PendingEvents.cs),
and [EF adapter](../../src/Gloaming.EventReactions/Gloaming.EventReactions.EntityFrameworkCore/EventReactionServiceCollectionExtensions.cs).

```csharp
public sealed class PendingDomainEvents : IDomainEventsSink
{
    public PendingDomainEvents();
    public int Count { get; }
    public void Raise(IDomainEvent fact);
}
public abstract class AggregateRoot
{
    protected AggregateRoot();
    public PendingDomainEvents DomainEvents { get; }
    protected void Raise(IDomainEvent fact);
}
public sealed class DomainEventRegistry<TModule>
{
    public DomainEventRegistry();
    public void Add<TEvent>(IDomainEventHandler<TEvent> handler) where TEvent : IDomainEvent;
}
public sealed class DomainEventSession<TModule>
{
    public DomainEventSession(DomainEventRegistry<TModule> registry, PendingDomainEvents sink,
        int maxHandlerInvocations = 128);
    public IDomainEventsSink Sink { get; }
    public int PendingCount { get; }
    public bool IsFaulted { get; }
    public void Track(PendingDomainEvents source);
    public void EnsureReady();
    public void Fail();
    public void Stage(Action collectSources);
    public Task StageAsync(Action collectSources, CancellationToken cancellationToken = default);
    public int Save(Action collectSources, Action validate, Func<int> nativeSave);
    public Task<int> SaveAsync(Action collectSources, Action validate, Func<Task<int>> nativeSave,
        CancellationToken cancellationToken = default);
}
public enum DomainEventTransactionMode
{
    RequireExisting = 1,
    CreateIfAbsent = 2,
}
public sealed class EfDomainEventSave<TModule>
{
    public EfDomainEventSave(DbContext context, DomainEventSession<TModule> events,
        Action collectSources, Action validate,
        DomainEventTransactionMode mode = DomainEventTransactionMode.RequireExisting);
    public void EnsureReady();
    public void AcceptAllChanges();
    public int Save(bool acceptAllChangesOnSuccess, Func<int> nativeSave);
    public Task<int> SaveAsync(bool acceptAllChangesOnSuccess, Func<Task<int>> nativeSave,
        CancellationToken cancellationToken = default);
    public void Stage();
    public Task StageAsync(CancellationToken cancellationToken = default);
}
```

Exact-type dispatch, missing-handler rejection, invocation ceiling, one-session buffer ownership,
optional inheritance, serialized handling, explicit false-acceptance acknowledgment and transaction
ownership are all interface requirements, not hidden implementation details. Async-only handlers
can be staged asynchronously before sync native saving. AcceptAllChanges is valid only after a
successful false-acceptance save, including an empty one; it cannot discard an unsaved producer.
The naming analyzer's CLR EventHandler-suffix rule has narrowly scoped suppressions on the two
domain-handler interfaces, retaining the owner's established domain vocabulary.
