# EventReactions consolidation — 2026-10-15

## Outcome and review boundary

The owner authorized one cohesive review of the local-reaction work rather than separate reviews
of its intermediate slices. The current contract is Gloaming.EventReactions with an optional
Gloaming.EventReactions.EntityFrameworkCore adapter. The capability captures ordinary events and
executes bounded, ordered reactions before native saving. It does not supply a DDD object model.

This report supersedes the current-interface guidance in D2.1a–e reports and the earlier
Gloaming.Domain positioning. Those documents retain their dated experiments and test counts.
The [ADR](../adr/0016-local-domain-event-dispatch.md) and
[execution contract](../../src/Gloaming.EventReactions/docs/local-dispatch.md) describe the final
design. Implementation remains subject to owner review; authorization to implement is not commit
approval. Existing staged files were preserved, with all consolidation edits in the working tree.

The earlier D1 and D1.1 commits, `041c64a` and `f55d803`, remain intact. Their sample-owned aggregate
rules, mappings and concurrency findings still apply. The rich-aggregate README now describes native
EF acceptance instead of the acknowledgment service removed during the subsequent experiments.

## Final mechanism

- PendingEvents composes into any consumer object. IEventSink provides ad-hoc capture, and
  IEventSink<TModule> supplies a scoped operation sink. No library AggregateRoot, mandatory event
  marker or event-source interface remains. Ordinary reference and value records are supported.
- IEventReaction<TEvent> and optional ISynchronousEventReaction<TEvent> provide typed reactions.
  EventReactionRegistry uses exact CLR types and freezes bindings before dispatch. Consumers own
  subscriptions and business meaning; registration is not a dependency graph.
- EventReactionDispatcher deduplicates selected buffers, clears each collected wave and preserves
  Raise capture order across buffers and the operation sink. It then collects downstream waves until
  empty or the reaction-invocation ceiling is reached. Handler registration order applies within an
  occurrence. The default ceiling is 128 invocations per dispatch.
- EF registration explicitly selects tracked buffers with CollectFrom<TEntity>(selector), and other
  scoped sources with CollectSources(provider, context). The operation sink participates automatically.
  No inheritance discovery, tracker mirror or row-diff inference replaces source registration.
- The separate shared Gloaming.EntityFrameworkCore package selects capabilities from model metadata,
  executes preparation, detects changes, then executes validation before native saving. Module DI
  extensions register reactions and sources; contexts need only their native options. Both sync and
  async saves work; sync saving rejects async-only reactions without blocking on them.

The current review also includes the previously prepared capability bindings for tenancy, audit,
inbox/outbox, workflows and required inline event state. These remain independent feature packages.
Feature model extensions supply their capability keys rather than requiring application magic strings.
Audit's internal capability key and entity annotation are now centralized constants; their values and
validation behavior are unchanged. Model metadata never captures scoped handlers or providers.

This proves a reusable composition mechanism: consumers can supply events without adopting the
library's aggregate model, including sources that are not EF entities. It does not prove a reusable
DDD foundation, generic decider, event-history store or new durable-delivery mechanism.

## Executable consumer evidence

The [standalone consumer](../../src/Gloaming.EventReactions/samples/EventReactionsDemo/README.md) has no Wholesale dependency:

- DirectOrders inherits a consumer-owned OwnedEntity and composes buffers in its root and allowance.
  Its module registration selects those buffers explicitly, and EF mappings ignore them.
- MappedQuotes keeps immutable state, decisions and plain facts in a separate
  [model project](../../src/Gloaming.EventReactions/samples/EventReactionsDemo/Model/EventReactionsDemo.Model.csproj) with no
  package, framework or project references. Application adapters map rows and forward accepted facts
  to the operation sink. Domain code has no EventReactions or EF dependency.
- Scoped-request proofs select a non-entity ImportRun buffer and combine it with the operation sink.
  Plain value-record events create persisted rows in sync and async saves, starting with no tracked
  event sources. This exercises ad-hoc capture rather than a replacement aggregate interface.

Existing direct/mapped proofs continue to cover deletion, cascades, cross-source order, repeated
payloads, root conflicts, required-reaction failures, transaction rollback, cancellation, invocation
ceilings, false acceptance, SQL bypass and late callback behavior. Composition proofs combine real
tenant and outbox validation with reactions. Architecture checks verify actual assembly boundaries,
the dependency-free model and consumer-owned inheritance.

Wholesale's [Sales example](../../samples/Wholesale/DomainEventsDemo/README.md) still uses domain
facts because that is its business meaning. Draft submission reserves customer allowance and records
the required work in the same module transaction. Its immutable aggregate does not depend on a marker
or base class. Business constraints, concurrency tokens, handler choices and row mapping stay sample-owned.

The former standalone sample and library folders, project identities, namespaces, solution entries,
architecture declarations and CI paths now use EventReactions. Existing standalone database schemas,
table names and migration identities are retained; the folder/namespace rename introduces no database
migration. Shared EF/template wiring prepared earlier remains in the review; this consolidation adds
no template preset and does not add EventReactions to the event-free template.

## Initial consolidation verification

Verification before the subsequent pre-review corrections below:

| Suite | Passed |
| --- | ---: |
| EventReactions core | 17 |
| Standalone EventReactions consumer, PostgreSQL and capability composition | 102 |
| Architecture boundaries | 89 |
| Wholesale Sales domain reactions | 30 |
| Auditing PostgreSQL | 39 |
| Rich aggregate consumer | 40 |
| DDD comparison consumer | 22 |
| Workflow consumer | 62 |
| HTTP identity consumer | 105 |
| **Total** | **506** |

All suites completed with zero failures or skips. The full solution builds with zero warnings/errors.
Solution style and analyzer verification pass. CSharpier checks 725 files including generated source.
Archive verification confirms all 800 original files remain intact. Changed-document links and both
staged/unstaged whitespace checks pass. These are current results, separate from earlier report counts;
unaffected historical suites were not rerun to inflate coverage.

Local verification logs are `/tmp/reactions-build.log`, `/tmp/reactions-core.log`,
`/tmp/reactions-consumer.log`, `/tmp/reactions-architecture.log`, `/tmp/reactions-sales.log`,
`/tmp/reactions-audit.log`, `/tmp/reactions-rich.log`, `/tmp/reactions-ddd.log`,
`/tmp/reactions-workflow.log`, `/tmp/reactions-http.log`, `/tmp/reactions-style.log`,
`/tmp/reactions-analyzers.log` and `/tmp/reactions-csharpier.log`.

The original consolidation preserved staged index manifest
`5acc5c5cd1abf56121b1bafdba92e355f724f90c49abd1cf35714ec8d7e74f32`.
That earlier snapshot contained the old names. The owner subsequently staged the consolidation;
the pre-review correction below preserves that newer snapshot. No agent staging, unstaging, commit
or history rewrite was performed.

## Pre-review corrections — 2026-10-15

The supplied independent review identified two P2 defects. Both were reproduced by regression tests
before applying fixes:

- A second event tag for the same context created its sink and dispatcher while shared preparation
  retained only the first binding. AddEventReactions now rejects a second registration for that
  context, including the same tag, before invoking configuration or mutating the service collection.
  All that context's reactions and sources belong in its single registration. Independent contexts
  retain distinct tags and scoped bindings. Multi-tag dispatch ordering is not added.
- A collector could catch a nested-dispatch rejection and return an empty batch, allowing outer
  success, or return a pending batch that invoked a handler before failure. Both dispatch paths now
  check health immediately after collection, before handling or returning. Four new cases exercise
  sync/async dispatch with empty and nonempty batches, assert no handlers execute and reject reuse.

The duplicate-registration cases cover both repeated and different tags, unchanged service descriptors,
an uninvoked rejected configuration callback and the surviving original sink/dispatcher. The existing
independent-context test remains in the same suite. These corrections tighten the documented lifecycle
and configuration boundary; no new reusable mechanism or template behavior was introduced.

Fresh verification after these corrections: core 21, standalone/composition 104, architecture 89 and
Sales reactions 30 tests, totaling 244 with no failures or skips. The full solution build, style,
analyzers, CSharpier, changed-document links and whitespace checks pass. The initial 506-test run above
remains separate evidence; unrelated suites were not repeated for these bounded fixes. Logs are
`/tmp/reactions-review-*-red.log` for the reproduced failures and `/tmp/reactions-review-*.log`
for corrected verification.

The index manifest at the start and end of the corrections is
`122e4a8285a8893f92924eea060964d41b65e9b7c08dc0d46c128fa7904ca203`.
All correction edits remain unstaged for owner review.

## Limits and deferred work

Source selection is an explicit integration obligation. An omitted buffer is neither discovered nor
drained. All current buffers must be visible before dispatch; events hidden until a later wave cannot
be retroactively inserted into work already completed. Ordering is process-local capture order,
not a persisted sequence or inferred business dependency. Buffers and dispatchers support one serialized
operation; Interlocked sequence allocation does not make them safe for concurrent mutation.

The caller must open the owning transaction before handling pending events and commit after saving.
On failure it must roll back, discard the scope and rebuild the decision from committed state. Cleared
events and mutated objects are not restored. Native SaveChanges(false)/AcceptAllChanges semantics
remain native, including the danger of premature acceptance. Direct SQL/bulk writes, detached or
unregistered sources, late callbacks, independent commits and automatic reuse after failure bypass
the documented protocol. External effects require a separate durable mechanism.

Use explicit application orchestration for complex dependencies or decisions requiring another step's
result. Local reactions remain an available bounded atomic pattern; no eventual-consistency default
is introduced inside a module. Durable workflow state is already module-owned, but existing workflow
durability does not make arbitrary direct module calls recoverable or persist a call stack.
[D2.3](../plans/d2-3-module-consistency-and-coordination-research.md) retains that delivery/compensation
and shared-transaction assessment. No new saga adapter, deferred-local inbox, decider abstraction or
business-history capability was implemented here.

## Review entry points

Start with the [core contract](../../src/Gloaming.EventReactions/Gloaming.EventReactions/README.md),
[core implementation](../../src/Gloaming.EventReactions/Gloaming.EventReactions),
[EF registration](../../src/Gloaming.EventReactions/Gloaming.EventReactions.EntityFrameworkCore/EventReactionRegistration.cs)
and [shared save integration](../../src/Gloaming.EntityFrameworkCore/Gloaming.EntityFrameworkCore/CapabilityConfiguration.cs).
Then compare [DirectOrders registration](../../src/Gloaming.EventReactions/samples/EventReactionsDemo/Consumer/DirectOrdersRegistration.cs),
[pure model](../../src/Gloaming.EventReactions/samples/EventReactionsDemo/Model),
[ad-hoc source proofs](../../src/Gloaming.EventReactions/samples/EventReactionsDemo/Consumer.Tests/AdHocSourceTests.cs),
[ordering proofs](../../src/Gloaming.EventReactions/samples/EventReactionsDemo/Consumer.Tests/EventOrderingTests.cs) and
[architecture boundaries](../../tests/ArchitectureTests/EventReactionBoundaryTests.cs).
