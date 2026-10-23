# Event-sourced aggregate bookkeeping

This package-free core supports one bounded event-append capability. It has no EF, JSON,
codec, tenancy, hosting or module dependency. The write contract is
`IEventSourcedAggregate<TEvent>`: Id, captured ExpectedVersion, proposed Version and ordered
PendingEvents. Implementing it is required for the EF appender; inheriting the supplied
`EventSourcedAggregate<TState, TEvent>` is optional.

The base initializes already-loaded state at its observed version. Version zero requires
null state; positive versions require state. Initialization creates no pending facts and
never applies today's command eligibility to historical state. The optional EF store validates its captured header/inline state; consumers using other
readers remain responsible for state/version consistency and historical decoding/integrity.

Consumer aggregate methods decide eligibility, produce immutable facts, then call protected
`ApplyChanges(batch)`. The base snapshots the batch, checks nulls and version arithmetic,
invokes pure `Evolve(state, batch)` and validates its final candidate exactly once through
`ValidateCandidate`. Only a successful complete candidate changes State, Version and the
read-only pending collection. An empty decision changes nothing. ExpectedVersion stays fixed
across accepted decisions; PendingEvents retains their order.

```csharp
internal bool TryIssue(decimal[] quantities, out decimal requested)
{
    var decision = StockPositionDecisions.Issues(State!, quantities);
    requested = decision.Requested;
    if (decision.Events is null)
        return false;
    ApplyChanges(decision.Events);
    return true;
}

protected override StockPositionState Evolve(
    StockPositionState? state, IReadOnlyList<IStockPositionEvent> events) =>
    StockPositionEvolution.Evolve(state, events);
```

ApplyChanges mutates accepted aggregate bookkeeping; Evolve calculates a candidate. A module
can share its internal pure reducer between aggregate evolution and historical reconstruction,
without exposing aggregate mutation or sharing business policy with the library. Different
projection state shapes retain their own reducers. The whole-batch hook avoids repeatedly
copying a growing collection for each fact.

States/facts and consumer evolution/policy must be immutable and free of side effects. The
base preserves its own accepted state/version/pending after ordinary failures; it cannot
undo arbitrary consumer mutation, external effects or concurrently used aggregates.
Pending events are proposals until caller commit. There is no pending reset, replay engine,
generic loader, projector registry, retry or transaction abstraction. Discard the aggregate
and context after persistence failure/rollback and load committed state for a new decision.

[EF appender](../Gloaming.EventSourcing.EntityFrameworkCore/README.md),
[consumer aggregate](../../../samples/Wholesale/modules/Inventory/Inventory/StockPositions/StockPositionAggregate.cs),
[core proofs](../tests/EventSourcingTests/AggregateTests.cs),
[reviewed scope](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/es1-bounded-event-append.md),
[findings](https://github.com/oxface/modulith-foundry/blob/bd2adb7dea58de304aa2eb3df3c587c6fe348320/docs/reports/es1-bounded-event-append.md).

## Local capability context

[Capabilities, limitations and deferred directions](docs/capabilities.md) are maintained
beside this package. They explain the optional EF adapter, independent Events utilities,
projection lifecycle gaps and future provider-specific integration. Repository plans/reports
are supplementary review/evidence, rather than required consumer setup documentation.
