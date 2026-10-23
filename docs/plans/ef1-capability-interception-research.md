# EF1: capability integration and interceptor research

Historical record: the owner subsequently authorized [EF1.1](../reports/ef1-1-capability-composition.md)
common capability composition and clear-before-handling dispatch. That implementation supersedes
retained sessions, Stage, owned transactions and completion enforcement described below. Source
links now lead to replacements; test counts below remain dated evidence.

Date: 2026-10-15 (Europe/Kiev). Owner-requested next research slice, following D2.1d review.
This authorizes assessment and a reviewable recommendation, not a migration of existing libraries
or approval of a new public interface/package. D2.1d's runtime remains unchanged during planning.

Investigation completed for review on 2026-10-15: [findings and recommendation](../reports/ef1-capability-interception-research.md),
[pinned external-pattern comparison](../reports/ef1-external-patterns.md) and
[18 retained native EF probes](https://github.com/oxface/modulith-foundry/blob/dd6eb77a4a0e3c8afd7af710d7689ca265d43a64/src/Gloaming.EventReactions/samples/EventReactionsDemo/Consumer.Tests/EfInterceptionResearchTests.cs).
Model-declared requirements can select scoped work; a common installation can own Gloaming's
relative order. Recommended production composition and domain-lifecycle simplification remain
proposals. No production package, interface or migration is implemented by this research.

## Problem and intended outcome

Most Gloaming EF consumers call tenant, audit, inbox/outbox, workflow and event-stream utilities
from SaveChanges overrides. D2.1d introduced event interception and moved Sales validation into
a local interceptor. Resolve this mixed integration style across capabilities before expanding it.
Compare consistent explicit calls with consistent opt-in interception, rather than optimizing
only domain-event dispatch. Preserve independently adoptable capabilities and module ownership.

Produce a source-backed lifecycle map, an inventory of current hooks, a small comparison/proof
where needed, and a recommended interface with concrete consumer usage. Identify which existing
guarantees remain necessary and which complexity can be removed through a simpler contract.
Research may conclude that native EF composition suffices and no new reusable mechanism is justified.

## Questions to answer

1. Which EF hooks suit each existing obligation? SaveChanges interception can run tracked-state
   validation and local event preparation. Transaction interception can observe EF transaction
   commit/rollback/savepoints. Command interception can observe EF-issued SQL, including some paths
   outside SaveChanges, but cannot infer aggregate decisions or safely reconstruct domain ownership
   from arbitrary SQL. Connection/materialization/query/identity-resolution hooks need concrete uses;
   their availability alone does not justify Gloaming features.
2. Establish precise sync/async ordering: DbContext events and overrides, SavingChanges, DetectChanges,
   event cascades, complete-result validation, native writes, tracker acceptance, SavedChanges,
   save/commit failure and cancellation. Examine ThrowingConcurrencyException hooks as well as native
   failure events. Cover explicit/implicit/external transactions, savepoints and suppression. Do not
   infer that a callback encloses later callbacks, or that SavedChanges establishes caller commit.
3. Compare an explicit utility pipeline and registered interception using the same obligations.
   Would one ordered save interceptor with capability participants remove ordering hazards, or would
   it add unnecessary abstraction compared with ordinary capability interceptors? Assess duplicate
   registration and coexistence with consumer/third-party hooks. No generic pipeline is preselected.
4. Define context-qualified opt-in capability composition. An application using outbox for one module
   must not activate it in every DbContext. Omit unused capabilities; do not silently omit required
   validation because its selected capability was incompletely configured. Compare explicit typed
   capability registration and existing model declarations; avoid package/assembly-presence guessing.
5. Decide whether a shared Gloaming.EntityFrameworkCore package earns its dependency/setup cost, or
   whether existing Gloaming.Persistence.EntityFrameworkCore/native EF registration is sufficient.
   A shared package must not reference every feature package. Capability packages may contribute to
   a small shared seam while keeping tenancy, audit, messaging, workflows and event sourcing optional.
   Keep relational/provider-specific behavior out of a provider-independent core where practical.
6. Resolve lifetime rules: scoped mutable event processing versus EF's ISingletonInterceptor types,
   cached model/options, context factories, design-time construction, pooling and parallel contexts.
   The documentation table's 'No' means not mandated to be singleton, not automatically scope-safe.
7. Reassess DomainEventSession and the verbose save adapter separately from general EF composition.
   Compare the current lifecycle with an AlCopilot-style before-save drain and clear protocol. Do
   explicit Stage, handled cursors, success acknowledgment, late-fact checks, failed-session state,
   transaction binding and optional transaction ownership each justify their interface/implementation?
   Prefer removing optional guarantees to layering helpers over them when that fits consumer needs.

## Candidate consumer shape, not an approved interface

A module extension selects capabilities for its own context and the host calls that extension.
Investigate a single native options-builder installation call, for example:

```csharp
services.AddDbContext<OrdersContext>((provider, options) =>
    options.UseNpgsql(connection).AddGloamingInterceptors<OrdersContext>(provider));
```

The name, generic parameters, provider argument and package are illustrative. 'AddInterceptors'
already names EF's native operation; assess whether another short name obscures that distinction.
Capability registrations should supply only their own context's integration. Installing the common
composition must not register workers, perform I/O, add tables, publish events, commit caller
transactions or activate absent capabilities. Selected guarantees must be clear and testable.

Retain explicit lower-level utility usage if it supports real standalone adoption; do not require
applications to select both integration styles or accidentally run each validator/dispatcher twice.

## AlCopilot comparison and simplification candidate

Use the [pinned reference comparison](../reports/al-copilot-domain-event-design-comparison.md),
including its evidence limits. Its core shape is attractive: collect tracked aggregate events,
snapshot/clear a wave, dispatch sequentially, repeat, then let EF save. Clearing before handling is
compatible with mandatory scope disposal on every error. Retaining buffers is not durable delivery
and cannot undo an external side effect. A simpler contract can avoid save-completion bookkeeping.

Adopting the reference exactly also adopts async-only interception, mandatory serialized event rows
and GUID/time metadata, aggregate-only sources, unqualified service-provider handler resolution,
zero-handler success and its current depth-limit edge case. These are choices or defects to assess,
not prerequisites for simple interception. Its cached expression-based dispatcher also conflicts
with the current design exclusion of runtime code generation unless separately reconsidered.

A narrower candidate retains the simple before-save loop with: sync support or explicit sync
rejection; module-qualified handlers; an operation sink for immutable mapped-domain decisions;
a corrected configurable cascade limit; caller-owned transaction policy where required; and optional
recording outside the mandatory local-dispatch protocol. Decide unhandled-event policy explicitly.
Keep durable deferred reactions/outbox mapping separate from local event handling.

DomainEventSession currently coordinates sources, handled cursors, routing, invocation budget and
save/failure state. Its Sink is a separate PendingDomainEvents buffer. Each AggregateRoot has its
own buffer; the session collects those directly. AlCopilot distributes these roles between aggregate
buffers, the interceptor loop and its dispatcher, without an equivalent persistent operation-level
coordinator. Compare responsibilities and contracts, not class-name or line-count equivalence.

## Research deliverables and proof gates

- Inventory current utility calls and their guarantees across Sales, Inventory, Purchasing and at
  least one independent consumer. Distinguish preparation, validation, recording and delivery.
- Map concrete obligations to native hooks and show one context with several selected capabilities
  and another without them. Include dependency direction and registration/order/lifetime rules.
- Compare at least the consistent explicit-call design, native capability interceptors and any
  justified shared-composition design. Include a reduced domain-dispatch alternative. State which
  complexity disappears and which guarantees are deliberately relinquished.
- Use bounded executable proofs only for disputed lifecycle/composition behavior: cascade before
  validation, handler-created tenant/outbox work, both save paths, concurrency/cancellation/failure,
  caller commit and callback order, module isolation and omitted capabilities. Native SQL/provider
  bypasses remain explicit counterexamples. Retain independently expected business outcomes.
- Report sources, new executions versus historical D2.1d tests, extraction candidates versus
  consumer policy, package/template/sample implications and unresolved gaps. Do not claim a new
  reusable mechanism from documentation or API availability alone.

Use official EF docs and the exact pinned EF version's source for behavior claims. Research findings
belong in a dated report; public interfaces remain proposals until owner review. Record any settled
architecture only after the choice is made. A production migration is a separate reviewable increment.

## Sources and original planning verification

- [EF interception overview](https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/interceptors):
  native hook families, registration, paired sync/async methods and singleton lifetime rules.
- [ISaveChangesInterceptor](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.diagnostics.isavechangesinterceptor?view=efcore-10.0)
  and [IDbTransactionInterceptor](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.diagnostics.idbtransactioninterceptor?view=efcore-10.0).
- [EF 10.0.12 DbContext source](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/DbContext.cs),
  plus the relevant native interceptor aggregators/transaction source during the research.
- [Current local-dispatch contract](../../src/Gloaming.EventReactions/docs/local-dispatch.md) and
  [D2.1d execution evidence](../reports/d2-1d-module-owned-event-composition.md).

This planning change inspected current utilities, the pinned AlCopilot source and official EF docs.
It changes no runtime code and runs no new runtime tests. D2.1d's 485 passing tests remain historical
implementation evidence. No new reusable mechanism was proven by this planning update. Preserve the
owner's staged review snapshot; no staging or commit is authorized by the research request.


The original planning statement above predates the completed investigation. Its new native runs,
verification and remaining proof obligations are recorded in the linked findings report; the
production implementation remains unchanged. Existing staged review content is preserved.
