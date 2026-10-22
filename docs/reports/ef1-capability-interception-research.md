# EF1: model-declared capabilities and ordered EF integration

Historical record: the owner subsequently authorized [EF1.1](ef1-1-capability-composition.md)
common capability composition and clear-before-handling dispatch. That implementation supersedes
retained sessions, Stage, owned transactions and completion enforcement described below. Source
links now lead to replacements; test counts below remain dated evidence.

Date: 2026-10-15 (Europe/Kiev). Investigation requested during D2.1d owner review.
This report completes the [research scope](../plans/ef1-capability-interception-research.md),
with [external implementation comparisons](ef1-external-patterns.md) and retained
[native EF probes](../../src/Gloaming.EventReactions/samples/EventReactionsDemo/Consumer.Tests/EfInterceptionResearchTests.cs).
Production libraries and contexts are unchanged. Interfaces and package names below are proposals.

## Recommendation

Proceed toward consistent opt-in interception for Gloaming's tracked-save integrations, using
model declarations to select required capabilities per context and one installation extension
to own their relative execution order. Keep native EF and existing validators underneath it.
The evidence supports this direction; it does not yet prove a production shared package.

Recommend a small Gloaming.EntityFrameworkCore composition package if the next implementation
removes repeated integration from at least two existing capability families. Keep the platform-only
libraries independent. Do not migrate every utility or add interceptors for every EF hook in one
increment. Prefer an ordered before-save coordinator for preparation and validation; introduce
transaction/command hooks only for a concrete obligation that cannot use that save stage.

For local domain events, prefer a reduced AlCopilot-style collect/clear/dispatch/rescan loop,
with module-qualified routing, mapped-domain sinks, a configurable bound, cancellation and deliberate
sync behavior. Remove optional transaction ownership, explicit staging and successful-save cursor
bookkeeping if the owner accepts a fresh-operation recovery contract. The current DomainEventSession
is not essential to that simpler contract. D2.1d remains the current implementation until replaced.

## Model declarations can select capabilities

The proposed split is feasible:

1. OnModelCreating declares static requirements for this model. Existing ConfigureOutbox,
   ConfigureInbox, ConfigureAudit, tenant ownership and required-inline-state mapping already
   record capability metadata. Domain events for immutable separately mapped models need an
   explicit model-level declaration because scanning entity base classes cannot discover them.
2. AddGloamingInterceptors installs the common integration when configuring context options.
   It does not need to build the model at registration time or eagerly construct every capability.
3. At saving, the integration reads the completed context.Model and resolves only its selected
   runtime adapters from the operation's service provider. Missing required bindings fail through
   normal DI/configuration errors rather than quietly skipping required validation.

Record required capabilities in metadata rather than scoped interceptor instances or concrete callback chains.
This is declaration-driven selection, not service registration from OnModelCreating. EF caches
models, and context options/internal services are initialized before model construction. Put
stable capability keys, booleans or other static descriptors in metadata, not a scoped provider,
handler, sink or operation state. Keep tenant/actor accessors and consumer key getters as runtime
bindings. Changes to tenant identity must not change model capability selection.
([Model caching guidance](https://learn.microsoft.com/en-us/ef/core/modeling/dynamic-model),
[EF 10.0.12 context initialization](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/DbContext.cs#L425-L463),
[model source](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/Infrastructure/ModelSource.cs#L55-L91).)

New probes use two context types with identical tables but different model requirements. They
prove the flag survives in runtime/design models, scopes share the cached model without sharing
scoped work, the unselected context does not even resolve the capability, and missing selected
services fail before SQL. The literal string requirement causes no relational migration difference
in this Npgsql example. This does not verify arbitrary descriptor objects, generated snapshot code,
compiled models, AOT or every provider. Native custom-annotation propagation is visible in
[RuntimeModelConvention](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/Metadata/Conventions/RuntimeModelConvention.cs#L195-L240).

A declared outbox requires its save validation even if this operation never resolves IOutbox.
A worker/delivery service remains separately opted in. Merely installing an outbox package or using
outbox in another module does not select this model. Registration cannot infer all consumer policy:
HasTenantOwnership supports arbitrary key types and its validator still needs the operation key
binding; domain dispatch needs the module's handler registry and operation sink.

## Inventory of current obligations

| Capability | Current declaration | What the save utility actually does | Proposed placement |
| --- | --- | --- | --- |
| Tenant ownership | Entity annotations plus named filter/concurrency mapping | Detects tracked writes and validates current/original owner against the operation key; does not assign ownership | Validation after preparation, with its existing runtime key binding |
| Audit | Gloaming:Audit entity marker | Checks append-only tracked records, unchanged staged envelope and originating active transaction | Validation; audit content remains explicitly staged |
| Outbox | Gloaming:Outbox entity marker | Checks enqueue registration, immutable envelope/lifecycle fields and the same native transaction | Validation; event-to-message mapping stays consumer-owned |
| Inbox | Gloaming:Inbox entity marker | Rejects tracked lifecycle writes, which belong to native intake/processing | Validation; no interception-based replacement for callable processing |
| Workflows | Explicit WorkflowInstanceRecord mapping; currently no dedicated capability marker | Checks runner proposals, unchanged process/request state, original tokens and transaction | Add an explicit requirement or supported mapping recognition; validate, do not auto-run workflows |
| Event sourcing / required state | Family-specific main-state annotations | Checks advancing headers, event range, required state, tokens and maintenance writes | Validation of explicitly prepared appends/repair; no implicit replay or append |
| Local domain events | Runtime context/module registration; currently no model requirement | Collects sources, routes handlers and drains downstream facts | Preparation before final validation; add an explicit requirement for mapped-domain sources |

Source inventory: [tenant mapping/validation](../../src/Gloaming.Persistence/Gloaming.Persistence.EntityFrameworkCore/TenantOwnershipExtensions.cs),
[audit](../../src/Gloaming.Auditing/Gloaming.Auditing.EntityFrameworkCore/AuditModelExtensions.cs),
[outbox](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore/OutboxModelExtensions.cs),
[inbox](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore/InboxModelExtensions.cs),
[workflow model](../../src/Gloaming.Messaging/Gloaming.Messaging.Workflows.EntityFrameworkCore/WorkflowModelBuilderExtensions.cs)
and [validation](../../src/Gloaming.Messaging/Gloaming.Messaging.Workflows.EntityFrameworkCore/WorkflowSaveChangesExtensions.cs),
[required event state](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/RequiredInlineStateExtensions.cs),
[domain integration](../../src/Gloaming.EventReactions/Gloaming.EventReactions.EntityFrameworkCore/EventReactionServiceCollectionExtensions.cs).

Sales currently appends its validators after event dispatch through OnConfiguring. Inventory calls
tenant/audit/event-stream/outbox/inbox validation in both save overrides; Purchasing calls tenant
and event-stream validation. Independent [ExportDbContext](https://github.com/oxface/modulith-foundry/blob/56a8b12857d1cffb842d839238784c607b66e474/src/Gloaming.Messaging/samples/OutboxDemo/ExportDbContext.cs)
and [RenderDbContext](https://github.com/oxface/modulith-foundry/blob/56a8b12857d1cffb842d839238784c607b66e474/src/Gloaming.Messaging/samples/InboxDemo/RenderDbContext.cs) call their owning capability guards.
Those small consumers are necessary adoption targets for any common integration, not just Wholesale.
Existing utility methods remain useful as the lower-level implementation and explicit adoption seam.

## Order: what can actually be enforced

EF appends AddInterceptors registrations; another call does not replace earlier entries. Internally
injected interceptors precede application interceptors. Application interceptors run in addition
order. A new probe confirms that registering the same instance twice executes it twice, so the
common installation must be idempotent or reject duplication. Avoid registering capability
interceptors independently and then installing them again through the common extension.
([Official registration contract](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.dbcontextoptionsbuilder.addinterceptors?view=efcore-10.0),
[EF aggregation source](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/Diagnostics/Internal/Interceptors.cs#L36-L52).)

A common extension can order Gloaming's selected interceptors in one batch. A single save coordinator
also makes missing-capability checks and shared preparation/validation phases explicit, without
exposing a numeric-priority system to consumers. Prefer that small coordinator if it removes
repeated selection and ordering logic. Both approaches retain native EF interception.

Recommended before-save order:

1. Verify the model's required runtime bindings exist; apply any capability-specific preconditions.
2. Drain local domain facts and their handlers, including newly loaded sources and downstream facts.
3. Perform final change detection, then run the selected tracked-write validators.
4. Return to native EF persistence; the caller retains save/acceptance/transaction ownership.

The strict dependency is complete preparation before complete-result validation, and all validation
before persistence. Most current validators only inspect prepared state; their relative order
primarily determines which error is reported first. Preserve a deterministic order, but do not infer
a need for a general dependency graph. Existing guards each call DetectChanges; retaining them may
repeat detection initially. A single shared change snapshot is a separate optimization, not a reason
to rewrite every guard during adoption.

The new phase probe deliberately supplies validation before preparation, then composes them by
phase. Both native save paths reject a generated invalid row before it reaches PostgreSQL. This
proves native ordering can support that rule; it does not migrate or prove all Gloaming guards.

New probes also confirm that both SavingChanges and SavedChanges run in forward order. These
callbacks are not nested middleware with reversed completion. The extension owns Gloaming's order,
not arbitrary consumer callbacks installed before/after it. Consumers must preserve the contract
when adding their own mutations or suppression.
([Exact native loop](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/Diagnostics/Internal/SaveChangesInterceptorAggregator.cs#L24-L49).)

## Native lifecycle and useful hooks

| Stage | Relevant evidence / implication |
| --- | --- |
| Context SavingChanges event, then SavingChanges interceptors | Preparation and validation run here, before native detection |
| Native DetectChanges, then persistence | Validators inspecting original/modified state must detect for themselves or use a preceding detection stage |
| State-manager acceptance when requested | Occurs before SavedChanges; false acceptance keeps EF's normal tracker behavior |
| SavedChanges interceptors, then context SavedChanges event | Successful save is not caller commit |
| Caller transaction commit | EF transaction hooks can inspect/reject this operation separately |

The exact save sequence is visible in [DbContext](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/DbContext.cs#L634-L680)
and [StateManager](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/ChangeTracking/Internal/StateManager.cs#L1354-L1440).
The change-detection probe observes an attached CLR edit as Unchanged on entering saving, detects
it as Modified, and confirms the updated value through a fresh PostgreSQL context.

A throwing SavingChanges interceptor is outside EF's native save try/catch: new probes receive no
matching failure interceptor or context failure event. Do not build an unconditional start/end
pairing protocol around these notifications. Concurrency has ThrowingConcurrencyException hooks
and a native context failure event; D2.1d's concurrency/cancellation proofs remain historical rather
than being reimplemented here. Every failure still requires discarding this operation when using
the proposed clear-before-dispatch contract.

TransactionCommitting/Async runs before the native commit. New probes reject caller commit after
a successful save and confirm no committed row. They also establish that transaction hooks run
during EnsureCreated schema setup, so domain-specific commit checks cannot indiscriminately apply
to every transaction. Committing GetDbTransaction directly bypasses EF's transaction hook; the
negative proof persists its row without invoking the armed EF rejection.
([Exact transaction implementation](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore.Relational/Storage/RelationalTransaction.cs#L74-L113),
[async commit](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore.Relational/Storage/RelationalTransaction.cs#L160-L205).)

Command/connection interception remains useful for identified native-operation requirements; it
does not restore domain facts for SQL/bulk writes. Materialization, query-expression and identity
resolution hooks need an actual consumer before extraction. EF singleton interception types require
shared thread-safe implementations; a table entry saying 'No' does not mean automatic scoped lifetime.
Use ordinary diagnostics for observation that does not need to alter operations.
([EF hook families and lifetime rules](https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/interceptors).)

## Sync and async recommendation

Keep both native save paths. Current validators are synchronous in-memory operations and can serve
both callbacks directly. Domain handlers performing async I/O implement the async path with token
propagation; native sync handlers can additionally serve sync saving. A synchronous save requiring
an async-only handler must throw a clear unsupported-operation error, not silently omit it and not
block on its Task. No common integration can make arbitrary asynchronous work synchronously safe.

The native async-only probe demonstrates the problem directly: synchronous SaveChanges succeeds
without executing the async-only interceptor; SaveChangesAsync executes it. Existing Gloaming
handler interfaces already provide the native dual-path/rejection distinction. Retain that useful
part while simplifying the surrounding lifecycle. The external template comparisons show blocking
adaptations, rather than evidence that they are required or desirable for Gloaming.

Preserve the current missing-required-handler failure unless the owner explicitly chooses optional
notifications. Do not inherit zero-handler success accidentally from GetServices. Explicit generic
registrations also avoid copying the reference's runtime-compiled expression dispatcher.

## What to remove from the domain adapter

| Current responsibility | Recommendation for the reduced contract |
| --- | --- |
| Separate root buffers and operation sink | Keep: mapped immutable domains need the sink; roots keep their own buffers |
| Explicit module routing and exact typed handlers | Keep; event visibility must not imply cross-module subscriptions |
| Sequential cascading drain and configurable invocation ceiling | Keep, including newly tracked sources; fix the reference's termination-check edge case |
| Reentrancy control and cancellation | Keep minimal dispatch protection and propagate cancellation |
| Explicit Stage API and handled cursors until successful save | Remove from the default design; clear batches before dispatch and use ordinary native save |
| Adapter-owned transaction creation/commit and transaction-reuse binding | Remove; consumers own required transaction scope and disposal |
| Begin/Check/Complete save acknowledgment protocol | Remove from the simple before-save path; no claim that it wraps every callback |
| Late native-callback domain-event enforcement | Do not promise it through the reduced dispatcher; domain decisions belong in commands/handlers |
| Mandatory DomainEventRecord, GUID identity/time metadata | Keep out of the required local mechanism; recording is a separate capability |

This would replace, rather than hide, optional lifecycle complexity. DomainEventSession currently
coordinates queues, handled offsets, routing, budget and save/failure state; it is not the common
sink itself. In the reduced design, a dispatcher and scoped sink may suffice without a public
session concept. Failure means rollback/disposal and re-deciding in a fresh scope. Clearing early
is compatible with that contract; retaining a buffer is not durable delivery or rollback repair.

Handlers may stage audit/outbox work explicitly through the owning capability. The common integration
must not invent audit content, translate private facts into messages, publish externally, replay
streams or execute workflows because a model declares those tables. Pure tracked work can use native
save atomicity; protocols requiring an encompassing transaction keep their caller-owned requirement.
Earlier command SQL, other contexts and external effects are not included retroactively. D2.2 remains
separate from these local save integrations.

## Package and consumer shape to take into interface review

The existing Gloaming.Persistence.EntityFrameworkCore package currently owns generic-key tenant
mapping/validation. Adding all-capability composition there would make unrelated capabilities depend
on that specialized package. A small Gloaming.EntityFrameworkCore package is the clearer candidate
if implementation proves shared selection/order value. It would reference EF Core and DI abstractions;
relational/provider behavior and actual validators stay with their feature packages. Feature EF
packages depend on the common composition seam; the common package does not import them all.
No new package is created by this research. Keep the distinctive AddGloamingInterceptors name for
review: AddInterceptors already denotes EF's native registration and accumulation behavior.

Illustrative usage, with existing model mapping and a proposed domain-event requirement:

```csharp
// Module registration: normal typed handlers, operation sink and capability runtime bindings.
services.AddDomainEvents<OrdersContext, OrdersModule>(events =>
    events.AddHandler<OrderSubmitted, ReserveAllowance>());
services.AddDbContext<OrdersContext>((provider, options) =>
    options.UseNpgsql(connection).AddGloamingInterceptors(provider)); // proposed

// In OnModelCreating:
modelBuilder.ConfigurePostgresOutbox("orders", "outbox"); // existing mapping marks its capability
modelBuilder.RequireDomainEvents();                       // proposed; also supports mapped domains
```

The current AddDomainEvents installs its own interceptor. In a future common integration it would
contribute its runtime binding instead; both paths must not be installed simultaneously. The example
omits the module's other ordinary registrations and mappings. Names/signatures, required binding
registration and initial two-capability adoption still need interface review; no public participant
hierarchy or ordering configuration is approved here.

Initially retain the current non-pooled operation scope requirement. Do not capture scoped services
in singleton interceptor types, shared model metadata or pooled context options. Context factories,
compiled models/AOT, generated snapshots, duplicate-installation semantics and third-party suppression
need explicit proof before support is claimed. [EF pooling guidance](https://learn.microsoft.com/en-us/ef/core/performance/advanced-performance-topics#managing-state-in-pooled-contexts)
explains why a reused context cannot simply inherit the current scoped-state setup.

Compare implementation choices explicitly:

| Choice | Assessment |
| --- | --- |
| Consistent explicit utility calls | Clear local order, minimal package setup; duplicates integration and requires every consumer to remember both paths |
| Common extension ordering native capability interceptors | Small first implementation; must also validate requirements/duplicates and keep selected service resolution scoped |
| One common save coordinator invoking capability adapters | Preferred if it concentrates selection, phases and errors behind one call; avoid a general middleware/DAG framework |
| A broad transaction-owning unit of work | ABP demonstrates the model, but it exceeds this research's desired scope and is not selected |

## New verification and remaining proof obligations

The retained native probe fixture adds 18 cases. The complete standalone Consumer.Tests suite
contains 78 existing domain-event cases plus those probes: **96 tests passed** on .NET 10.0.401,
EF 10.0.12, Npgsql 10.0.3 and PostgreSQL 18.6. No external project's suite was run. The two contexts,
scoped selection, missing binding, metadata/schema check, forward ordering, duplicate registration,
async-only bypass, unpaired failure, change detection, preparation/validation and transaction/bypass
results above are new executions. Existing cascades, tenant/outbox enforcement and cross-family
production composition remain the earlier D2.1d evidence; the probes do not establish their migration.

No new reusable Gloaming mechanism is claimed as proven: native primitives were verified, a common
composition mechanism is an extraction candidate, and a production library/interface was not added.
Model declarations, scoped resolution and ordered native interception are feasible. Consumer-owned
rules, mappings, required participants, audit/message content and transaction admission remain policy.
Template materialization is deferred until the integration has real library/consumer adoption.

The next reviewable implementation should prove the common call using at least two existing
capability families and one omitted-capability context, then adopt local events with the agreed
reduced contract. Repeat actual handler-created tenant/outbox validation, cascade, both save paths,
cancellation/native failure, transaction and fresh-state recovery proofs through that public usage.
Only then claim a shared package and migrate further contexts. Update ADR 0016 after the owner
settles the changed contract, rather than treating this research recommendation as approval.

Final verification: full ModulithFoundry.slnx build passed with zero warnings/errors; required
style/analyzer checks passed; CSharpier checked 707 files; archive verification preserved all 800
original files from 15d1ec6ce85f872e4547b37c33658dea76b25d03. Markdown source links and staged/unstaged
whitespace checks passed. Logs: /tmp/ef1-{build,consumer,style,analyzers}.log. No runtime source,
package declarations, production registrations or migration files changed. Added research consists
of the two reports, the native test fixture and planning/design status updates.

The existing staged-content manifest remained
`dbacccc25bd2b8ee6207cc60caefac79dc4c2b8218464b1f5a56362639faf40a`.
All investigation changes remain unstaged. No staging, unstaging, commit or push was performed.
