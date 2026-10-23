# D2.1c: registered domain-event integration and consumer cleanup

Current review entry: [EventReactions consolidation](event-reactions-consolidation.md)
supersedes the earlier package names, aggregate base and marker requirements. Descriptions and
test counts below retain their dated scope; source links lead to current replacements.

Historical record: the owner subsequently authorized [EF1.1](ef1-1-capability-composition.md)
common capability composition and clear-before-handling dispatch. That implementation supersedes
retained sessions, Stage, owned transactions and completion enforcement described below. Source
links now lead to replacements; test counts below remain dated evidence.

Historical interface/evidence: [D2.1d](d2-1d-module-owned-event-composition.md) supersedes the explicit save-service integration
and custom false-save acceptance described below. Source links for removed adapters/composition
now point to their current interceptor/module-registration replacements; earlier results remain dated.

Date: 2026-10-15. Owner-authorized implementation of the
[registered integration/cleanup scope](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/d2-1c-domain-event-integration-simplification.md),
prepared on `feat/ddd-sample-patterns` for review. No commit or staging was performed.

## Outcome

Sales and both standalone persistence shapes now reuse the registered Gloaming implementation.
Applications inject IDomainEventSave<TContext> and make one explicit call from each bool
SaveChanges override; native save, validators and transaction ownership remain visible. The
concrete EfDomainEventSave is internal to the EF package. No automatic interceptor, base DbContext,
background dispatcher or PostgreSQL-specific wrapper was introduced.

The optional EF package provides AddDomainEvents<TContext,TModule>, explicit typed handler
registrations/factories, scoped DomainEventSession<TModule> and IDomainEventsSink<TModule>.
Handlers resolve on first dispatch, allowing context-dependent constructors without a DI cycle.
Factory bindings create one deferred handler per operation. Module-qualified sinks route to the
same operation/session used by the adapter, with real DI proofs for module and scope isolation.
The adapter binds to one context per scope when first used and collects tracked AggregateRoot
buffers automatically, with optional additional source collectors.

The core remains package-free. The EF package now depends on core, EF Relational and DI
Abstractions, which is used for the requested registration mechanism. PostgreSQL remains the
database proof provider. No additional DI implementation/framework dependency enters core.

## Removed code and simpler sample behavior

- Deleted RequiredSalesReactions and DraftOrderSaveGuard, including mirrored expected row values,
  typed pending-root bookkeeping and bespoke false-save/transaction lifecycle handling in Sales.
- Removed EnsureCanDecide calls from commands and allowance services, and removed the Sales-specific
  StageRequiredDomainReactionsAsync convenience method. Explicit staging uses the library abstraction
  in tests; ordinary executable usage calls SaveChanges.
- Replaced DraftOrderAggregate.PendingFacts with an explicit immutable (State, Event) submission
  result. The command maps the accepted candidate and raises its fact into the module sink.
- Added an ordinary ReserveCustomerAllowance handler, whose domain decision and row mapping stay
  in Sales. It uses the scoped SalesDbContext and never saves independently.
- Removed the standalone OwnershipGuard. The incorrect-related-row proof now exercises native EF/
  PostgreSQL foreign-key failure, without a custom sample ownership policy.
- Both standalone consumers now use real registered operation scopes. Their options-only internal
  constructors serve EF design-time model creation, not runtime save coordination.

Native ownership validation in Sales is retained and runs after event handling, along with existing
audit, inbox/outbox and workflow validation. Domain methods still enforce complete draft/allowance
rules, and native root/allowance version predicates still arbitrate legitimate competing commands.
The existing schemas, migrations, relationships and persisted numeric values are unchanged.

## Deliberately changed guarantees

Arbitrary tracked row changes are not accepted domain decisions. The removed default guards no
longer check full-graph loading, root-version participation, fixed terms, fact/row correspondence
or tampering with a staged reaction. Native EF/SQL constraints may reject invalid writes; they do
not enforce every aggregate invariant. Current tests explicitly demonstrate a direct tracked
submission without a fact and a child-only write without root-version advancement. SQL/bulk
negative proofs remain. The earlier D1.1/D2.1 guard-specific results are dated historical evidence.

The former Sales limit of 32 distinct submissions and its rejection of downstream facts are gone.
Gloaming's configured invocation ceiling (default 128) bounds the actual handler cascade. A Sales
proof now produces another accepted fact during handling and verifies both decisions are drained
and persisted, replacing the previous unsupported-cascade assertion.

Stage handles pending events and their cascades without native save or acknowledgment. Save stages
remaining events and performs native save once, then acknowledges handled occurrences. Successful
false saves still require the library's explicit AcceptAllChanges(context); no original-version
snapshots or command-level readiness checks are added. A context is disposed after its participating
transaction ends; failures require fresh state and rollback of caller-owned transactions.

## Fresh verification

| Check | Result |
| --- | --- |
| DomainTests | 19 passed |
| Standalone Consumer.Tests | 70 passed: 69 PostgreSQL cases plus one new real-DI scope/module isolation proof |
| Sales DomainEventsDemo.Tests | 32 passed |
| RichAggregateDemo.Tests | 40 passed |
| ArchitectureTests | 87 passed, including actual registration dependency checks |
| Full solution restore/build | Passed, zero build warnings/errors |
| Full solution style/analyzers | Passed with verify-no-changes |
| CSharpier including generated source | Passed, 704 files |
| Archive integrity | All 800 original files verified |
| Documentation links | 1,805 local targets resolve across 205 active documents |

All **248 tests passed**, with no failures or skips. These are fresh executions after cleanup, not
the earlier assessment/promotion totals. One new DI test establishes deferred factory resolution,
same-session sink binding, module isolation and fresh-scope separation. Existing consumer tests
now execute through DI and the public save abstraction, including both save families, async-only
handlers, staging, deletion/detachment, cascades, native concurrency, required-write/commit failures,
cancellation and recovery. The Sales suite also proves a typed handler whose constructor depends on
the same scoped context, and retains post-handler Gloaming tenant validation.

Four obsolete Sales guard tests and two rich-aggregate guard cases were removed. Direct-write and
cascade assertions were rewritten to the current supported boundary rather than retaining the old
expectations. The standalone custom-ownership rejection became a native foreign-key failure proof.
No tests were disabled. Unrelated test families were not rerun after the focused affected suites
and full solution build/style/analyzer checks passed.

## Review-worthy files

- [Public save abstraction](../../src/Gloaming.EventReactions/Gloaming.EventReactions.EntityFrameworkCore/EventReactionServiceCollectionExtensions.cs),
  [registration](../../src/Gloaming.EventReactions/Gloaming.EventReactions.EntityFrameworkCore/EventReactionServiceCollectionExtensions.cs),
  [handler bindings](../../src/Gloaming.EventReactions/Gloaming.EventReactions.EntityFrameworkCore/EventReactionRegistration.cs)
  and [scoped adapter](../../src/Gloaming.EventReactions/Gloaming.EventReactions.EntityFrameworkCore/EventReactionServiceCollectionExtensions.cs).
- [Sales registration](../../samples/Wholesale/modules/Sales/Sales/SalesRegistration.cs),
  [context](../../samples/Wholesale/modules/Sales/Sales/SalesDbContext.cs),
  [immutable submission](../../samples/Wholesale/modules/Sales/Sales/DraftOrders/DraftOrderAggregate.cs),
  [command mapping](../../samples/Wholesale/modules/Sales/Sales/DraftOrders/DraftOrderCommands.cs) and
  [required handler](../../samples/Wholesale/modules/Sales/Sales/OrderAllowances/ReserveCustomerAllowance.cs).
- [Standalone composition](https://github.com/oxface/modulith-foundry/blob/dd6eb77a4a0e3c8afd7af710d7689ca265d43a64/src/Gloaming.EventReactions/samples/EventReactionsDemo/Consumer/DirectOrdersRegistration.cs),
  [direct consumer](https://github.com/oxface/modulith-foundry/blob/dd6eb77a4a0e3c8afd7af710d7689ca265d43a64/src/Gloaming.EventReactions/samples/EventReactionsDemo/Consumer/DirectOrders.cs),
  [mapped consumer](../../src/Gloaming.EventReactions/samples/EventReactionsDemo/Consumer/MappedQuotes.cs),
  [DI isolation proof](https://github.com/oxface/modulith-foundry/blob/dd6eb77a4a0e3c8afd7af710d7689ca265d43a64/src/Gloaming.EventReactions/samples/EventReactionsDemo/Consumer.Tests/RegistrationTests.cs) and
  [Sales cascade/tenant/concurrency proofs](../../samples/Wholesale/IntegrationTests/Reactions/ReactionTests.Faults.cs).

The new reusable capability proven here is scoped registration and context-independent adapter
resolution around the existing lifecycle, with actual Sales reuse. The bounded dispatch algorithm
was already proven in D2.1a; it is not claimed as a new mechanism again. Domain rules, event types,
handlers, mapping and native validators remain consumer-owned. No template preset was added.

The owner's staged review snapshot is preserved exactly. Edits and deletions remain working-tree
changes, including staged former implementation files whose review versions stay in the index.
Frozen archive files, existing migrations and unrelated library/runtime files are preserved.
The pre-change manifest covers 1,748 existing files; its 43 changed/deleted existing paths are
confined to the intended Domain, Sales, consumer, proof and documentation scope. The staged index
hash matches the pre-change snapshot exactly.
No staging, unstaging, commit, package publication or push was performed.

## Remaining limits

Sessions, buffers, aggregates and DbContexts execute serially; a concurrent queue would not make
the operation thread safe. Configure each context/module pair once and use a separate scope for
independent operations. A handler that never returns cannot be interrupted by an invocation bound.

No universal write guard, automatic event inference, event store, external-effect atomicity,
automatic retry, process-death recovery or known outcome after ambiguous commit is supplied.
Native callback facts are rejected rather than dispatched after persistence. D2.2 durable deferred
handling and explicit inbox/outbox publication still require separate consumer policy and proofs.
