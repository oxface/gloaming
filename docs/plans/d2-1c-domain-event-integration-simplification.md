# D2.1c: simplify domain-event consumer integration

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

Date: 2026-10-15. Owner review of the promoted mechanism identified leftover Sales-specific
save bookkeeping and requested an abstraction registered through the EF package. The owner selected
a registered adapter with one explicit call from SaveChanges overrides, rather than automatic EF
dispatch. The owner authorized cleanup, now implemented for review with
[fresh results](../reports/d2-1c-domain-event-integration-simplification.md). The scope below records
the replacement and its deliberate reduction of default safeguards.

## Current code and agreed direction

RequiredSalesReactions is the earlier D2.1 sample coordinator, not a decider/provider abstraction.
It duplicates queue, dispatch and save lifecycle work now available in Gloaming, and additionally
tracks expected row values and fact/row matches. StageRequiredDomainReactionsAsync was added to
compare explicit staging with SaveChanges dispatch. Neither should remain the default Sales
integration after adopting the library.

Replace that coordinator with ordinary typed registered handlers and a session sink. Remove
EnsureCanDecide calls from commands and allowance services. Remove DraftOrderSaveGuard as a default
sample safeguard. Domain methods still enforce domain invariants; native EF mappings, version
predicates, constraints and the existing Gloaming tenant validator retain their roles. EF does not
enforce every domain invariant for arbitrary row writes, and removing these guards deliberately
ends that additional guarantee. Direct SQL/bulk/tracker manipulation does not gain an automatic
domain-event guarantee.

DraftOrderAggregate.PendingFacts currently contains accepted submission facts from an immutable
decision. It is not an EF changeset or an event-sourcing history. Separate domain/row mapping needs
to forward accepted facts, but does not need another record of expected tracked row values. Prefer
an explicit decision result, for example (State, Event), mapped and raised by the application adapter:

```csharp
var (candidate, submitted) = aggregate.Submit();
Map(candidate, row);
events.Raise(submitted);
```

OwnershipGuard in the standalone sample is a simpler duplicate of a tenant-ownership concern,
used to exercise validation after cascades without adding another library dependency. It is not
the Gloaming.Persistence validator and lacks its complete original-owner/metadata checks. Move
proof-only validation policy into test fixtures; use existing Gloaming ownership integration when
demonstrating tenant enforcement, rather than establishing a new default ownership guard.

## Registered interface

The EF package owns registrations and persistence coordination. The consumer-facing shape:

```csharp
services.AddDomainEvents<SalesDbContext, SalesModule>(events =>
    events.AddHandler<DraftSubmitted, ReserveCustomerAllowance>());
```

IDomainEventSave<TContext> in Gloaming.Domain.EntityFrameworkCore, with TContext constrained
to DbContext. It accepts the context at invocation rather than resolving/capturing it in the adapter's
constructor. The application's override contains one explicit call, with native validation/save
in its callback:

```csharp
public override int SaveChanges(bool acceptAllChangesOnSuccess) =>
    domainEvents.Save(this, acceptAllChangesOnSuccess,
        () => SaveNative(acceptAllChangesOnSuccess));

public override Task<int> SaveChangesAsync(
    bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default) =>
    domainEvents.SaveAsync(this, acceptAllChangesOnSuccess,
        () => SaveNativeAsync(acceptAllChangesOnSuccess, cancellationToken), cancellationToken);
```

The registered abstraction also exposes explicit Stage/StageAsync and existing false-save
acknowledgment at the persistence boundary. Ordinary commands and domain entities do not call
readiness checks or depend on this EF-specific abstraction. The concrete EfDomainEventSave becomes
an implementation detail of registration. Existing tenant/audit/inbox/outbox checks stay in the
native callback, after event handling, rather than acquiring another generic validation framework.

Registration needs explicit typed handler bindings, scoped instances and deferred handler resolution
so handlers can depend on their DbContext without causing a constructor-resolution cycle. The core
remains independent of DI and EF. Add the DI dependency only to the optional EF integration where
it is used; no PostgreSQL-specific package is needed for this provider-neutral mechanism. PostgreSQL
remains the real provider covered by database proofs.

Session-level raising should be injectable through a module-qualified sink, for example
IDomainEventsSink<TModule> extending the existing write-only interface. Registrations must bind it
to the same operation/session used by the adapter and isolate different module bindings. Handler
resolution and sink lifetime must be proven through a real service-provider composition, not only
manual construction in a test.

## Stage, save and buffers

Stage means execute all pending handlers and their cascades, changing domain/tracked state without
native save or acknowledgment. Calling it again does not repeat already staged occurrences. Save
stages any remaining work, invokes native save and acknowledges successfully handled occurrences.
This is a flush of event handling, not EF's usual meaning of flushing changes to the database.

PendingDomainEvents is a buffer type, not a global shared queue. AggregateRoot owns one buffer and
forwards protected Raise calls into it; it does not itself implement IDomainEventsSink. The session
tracks aggregate buffers and its separately supplied session-level sink. Aggregate facts need not
be copied into that shared sink. Non-aggregate facts and events returned by separate immutable
domain objects can be raised into the operation sink directly.

Keep execution serial. DbContext, aggregate mutation, buffers and handled cursors are not thread
safe; replacing the list with a concurrent queue would not make the operation safe. Independent
parallel operations use independent contexts and sessions. Async handlers can await work, but
finish before the next handler is invoked.

DirectOrder.Draft currently emits no creation fact because creation has no required reaction in
this example. A domain creation event is optional in a state-stored aggregate. If a use case needs
one, raise a named accepted fact with the initial values its handlers require; event sourcing and
complete reconstruction payloads do not follow from the existence of IDomainEvent.

## Verification and documentation obligations

Preserve the staged owner snapshot and edit only the working tree. Prove DI resolution with a
context-dependent handler, module sink isolation, one explicit save call, both native save families,
cascades, native version conflicts, cancellation/rollback and immutable decision adaptation.
Retain the existing direct and immutable consumer shapes as executable library usage, while removing
default fixture-only validation from production sample wiring.

Replace obsolete tests for the removed defensive tracker/guards with current consumer and native
constraint expectations. Keep concurrency, domain invariants and required local outcomes tested;
do not claim EF will reject arbitrary writes merely because the old custom guards did. Update
package-local setup and the current design/report separately from dated D1.1/D2.1 proof history.
The report records actual executions separately from this scope.

## Implemented public integration surface

The existing non-generic sink remains; the core adds its module-qualified DI binding type.
EfDomainEventSave is now internal. The new public registration/save surface is:

```csharp
public interface IDomainEventsSink<TModule> : IDomainEventsSink { }

public interface IDomainEventSave<TContext> where TContext : DbContext
{
    int Save(TContext context, bool acceptAllChangesOnSuccess, Func<int> nativeSave);
    Task<int> SaveAsync(TContext context, bool acceptAllChangesOnSuccess,
        Func<Task<int>> nativeSave, CancellationToken cancellationToken = default);
    void Stage(TContext context);
    Task StageAsync(TContext context, CancellationToken cancellationToken = default);
    void AcceptAllChanges(TContext context);
}

public sealed class DomainEventRegistration<TContext, TModule> where TContext : DbContext
{
    // Constructed by AddDomainEvents, not by consumers.
    public int MaxHandlerInvocations { get; set; } // default 128
    public DomainEventTransactionMode TransactionMode { get; set; } // default RequireExisting
    public DomainEventRegistration<TContext, TModule> AddHandler<TEvent, THandler>()
        where TEvent : IDomainEvent where THandler : class, IDomainEventHandler<TEvent>;
    public DomainEventRegistration<TContext, TModule> AddHandler<TEvent, THandler>(
        Func<IServiceProvider, THandler> factory)
        where TEvent : IDomainEvent where THandler : class, IDomainEventHandler<TEvent>;
    public DomainEventRegistration<TContext, TModule> CollectSources(
        Action<TContext, DomainEventSession<TModule>> collect);
}

public static class DomainEventServiceCollectionExtensions
{
    public static IServiceCollection AddDomainEvents<TContext, TModule>(
        this IServiceCollection services, Action<DomainEventRegistration<TContext, TModule>> configure)
        where TContext : DbContext;
}
```

The [registered adapter](../../src/Gloaming.EventReactions/Gloaming.EventReactions.EntityFrameworkCore/EventReactionServiceCollectionExtensions.cs)
binds to one context in the scope at first use. The
[registration implementation](../../src/Gloaming.EventReactions/Gloaming.EventReactions.EntityFrameworkCore/EventReactionRegistration.cs)
defers scoped handler resolution, preserves the core's native sync/async distinction and adds
optional collectors after automatically collecting tracked AggregateRoot buffers. Configure only
during service registration. No cross-context operation or thread-safe scope reuse is supplied.
