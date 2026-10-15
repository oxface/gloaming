# EF1.1: shared EF capability composition and simpler local dispatch

Current review entry: [EventReactions consolidation](../reports/event-reactions-consolidation.md)
supersedes the earlier package names, aggregate base and marker requirements. Descriptions and
test counts below retain their dated scope; source links lead to current replacements.

Date: 2026-10-15. Owner-authorized implementation following the
[EF1 investigation](ef1-capability-interception-research.md) and
[scoped MassTransit comparison](ef1-masstransit-applicable-patterns.md).
Prepared on feat/ddd-sample-patterns for owner review. Existing staged content is preserved;
this increment changes only the working tree. No staging, commit or publication is authorized.

Subsequent owner-selected [D2.1e policy and ordering](d2-1e-local-order-and-workflow-durability.md)
adds capture order across buffers and records the workflow durability review. Counts and guarantees
below describe the EF1.1 run rather than that later increment.

## Outcome

Gloaming.EntityFrameworkCore supplies ConfigureGloamingCapabilities<TContext>. Existing feature
model helpers declare stable requirements, and module DI supplies context-specific runtime bindings.
One native interceptor implements both save paths and executes preparation, explicit change detection,
then validation. Relative registration order remains stable within each phase. Missing required
bindings fail before any preparation; unused bindings do not execute. Installation is idempotent;
TryAdd bindings deliberately retain the first registration per context/key. No model or static entity
cache holds scoped services.

Existing tenant, audit, inbox, outbox, workflow and required-inline-state validators have small
context-specific registration helpers. Sales, Inventory and Purchasing database registrations compose
them centrally, preserving their previous relative validation order. All production compositions of
those modules use the database extensions. Their contexts no longer contain save overrides or a
Sales-specific validation interceptor. Independent earlier consumers can retain the documented explicit
validator recipes; those lower-level APIs remain supported.

DomainEventDispatcher replaces DomainEventSession. The operation sink and aggregate buffers are
collected, each batch clears before handling, and downstream waves drain serially until empty or the
invocation ceiling fails. Newly loaded roots participate. Exact-type routing, module sinks, optional
AggregateRoot and immutable-domain adapters remain. Handler construction is deferred until dispatch
so ordinary scoped handlers can depend on the completed context. Both save paths work; synchronous
saves explicitly reject async-only handlers without blocking.

Remove retained handled cursors, Stage, core save delegates, acknowledgment, transaction ownership,
transaction-end binding and completion/failure interceptor bookkeeping. The core dispatcher neither
saves nor validates. The EF domain adapter contributes preparation to shared configuration and
requires a caller transaction when facts are pending. The caller saves and commits natively.

## What is proven and what was rejected

[CapabilityCompositionTests](../../samples/EventReactionsDemo/Consumer.Tests/CapabilityCompositionTests.cs)
uses the real domain, ownership and PostgreSQL outbox packages. Validators are registered before
dispatch. Both save paths persist a submitted aggregate, required receipt and staged outgoing message
in one caller transaction. The message remains undispatched. Foreign-owner handler work and tampered
outbox envelopes fail before persistence; rollback leaves all three tables empty. This proves a new
reusable composition mechanism across existing capability families, without a shared business policy.

The same suite proves duplicate installation, first-binding semantics, missing-binding rejection
before selected preparation, omitted model capabilities and scoped service isolation across contexts
sharing a cached model. The retained 18 native EF research probes establish callback order, detection,
model-annotation/migration behavior and bypasses. They are fresh executions here rather than new
production capability claims for every native hook.

[DispatcherTests](../../src/Gloaming.EventReactions/tests/EventReactionsTests/DispatcherTests.cs) covers ordered cascades,
new sources, repeated dispatch, duplicate source/equal occurrence behavior, module isolation,
frozen registration, missing handlers, clear-before-handling failure, cycles/exact ceilings,
nested dispatch, asynchronous-only work and cancellation before collection.

The two PostgreSQL domain shapes retain native overload, deletion, required-handler rejection,
producer/reaction constraint, concurrency, deferred commit and fresh-state recovery proofs. Sales
retains allowance conflict, required reaction, tenant-check and rollback coverage. Negative proofs
make reduced guarantees visible: premature native acceptance can discard producer changes without
withdrawing their facts; SQL bypasses decisions; facts raised after preparation can remain pending
after a successful save. Late-event enforcement is deliberately removed, not silently emulated.

Native callback availability does not justify a universal command/transaction/query interceptor
framework. Native transaction hooks also observe schema work and can be bypassed through ADO.NET;
SavedChanges is not caller commit. No global activation based on package presence is selected.

## MassTransit findings within existing scope

Adopt its context-specific composition, separate model/runtime configuration and explicit worker
activation. Retain Gloaming's typed outbox and separate enqueue/delivery/worker roles. Avoid a static
cache keyed solely by a shared entity CLR type. The pinned implementation's tracked-state method
named WasCommitted is not proof of an outer transaction commit.

No new reusable messaging mechanism was proven by source research alone. Worker wake signals,
adaptive polling, batching, retention windows and alternate transaction/transport protocols remain
deferred pending actual requirements and measurement. The comparison cites pinned primary source;
no external framework tests or benchmarks were executed. Gloaming gains no bus facade or saga engine.

## Template and dependency repair

Persistence now references the common EF package. The source-snapshot template therefore includes
that fourth library and its two native package versions. The creator relocates only copied native
project references into the existing flat libraries directory, rejecting unselected dependencies.
C# source remains byte-identical; manifest hashes describe the materialized project/source files.
The proof verifies local references, deterministic output and both generated consumers. Catalog
retains explicit validation. This is a dependency repair, not a new interceptor/template preset.

CI selection includes shared EF source in dependent feature lanes and the template lane. Domain
selection also accounts for the real composition test's Persistence/Messaging dependencies.
Architecture checks verify the shared package has no feature/provider dependency and Domain remains
platform-only; the executable standalone consumer still has no Wholesale/other-feature dependency.

## Fresh verification

All active solution projects build with zero warnings/errors. The following suites passed with no
skips; counts describe this implementation and are distinct from historical D2.1d's 485-test result.

| Suite | Passing tests |
| --- | ---: |
| Domain dispatcher | 12 |
| Standalone consumer, common integration and native EF probes | 98 |
| Sales required reactions | 30 |
| Architecture | 88 |
| Audit PostgreSQL | 39 |
| Tenant ownership | 23 |
| Outbox / inbox PostgreSQL | 36 / 34 |
| Workflow / event-store PostgreSQL | 38 / 67 |
| Wholesale rich aggregate / DDD | 40 / 22 |
| Wholesale workflow / HTTP / event persistence / baseline persistence | 62 / 105 / 190 / 36 |
| Two external template adoption suites | 5 + 5 |
| **Total** | **930** |

Template TypeScript type/format checks and complete creation proof passed, including conflicting
parent SDK pin, namespace Task, rejection/failure atomicity, deterministic source snapshots, both
external builds/style/analyzers and PostgreSQL journeys. Final solution style and analyzer verification
passed; CSharpier checked 715 files. Archive verification retained all 800 original files. Changed
Markdown links resolve and git diff --check passed. The staged manifest stayed unchanged at
dbacccc25bd2b8ee6207cc60caefac79dc4c2b8218464b1f5a56362639faf40a.
Local logs use /tmp/ef1-1-*.log.

## Review-worthy files and remaining boundaries

- [Common capability implementation](../../src/Gloaming.EntityFrameworkCore/Gloaming.EntityFrameworkCore/CapabilityConfiguration.cs)
  and [consumer contract](../../src/Gloaming.EntityFrameworkCore/README.md): model selection, phase order,
  first-binding and scoped-options requirements.
- [Dispatcher](../../src/Gloaming.EventReactions/Gloaming.EventReactions/EventReactionDispatcher.cs),
  [buffer](../../src/Gloaming.EventReactions/Gloaming.EventReactions/PendingEvents.cs) and
  [EF binding](../../src/Gloaming.EventReactions/Gloaming.EventReactions.EntityFrameworkCore/EventReactionServiceCollectionExtensions.cs):
  reduced lifecycle and explicit caller transaction.
- [Sales database composition](../../samples/Wholesale/modules/Sales/Sales/SalesDatabase.cs),
  [standalone module](../../samples/EventReactionsDemo/Consumer/DirectOrdersRegistration.cs),
  [real composition proofs](../../samples/EventReactionsDemo/Consumer.Tests/CapabilityCompositionTests.cs)
  and [template relocation](../../tools/template/create.ts).

Any failure requires rollback/disposal and a fresh operation with reloaded state and a new decision.
Clear-before-handling is not durability. Native write/commit failures have no universal same-object
reuse guard. The coordinator does not own transactions or observe every later native callback;
external suppression and late facts are unsupported. Context pooling, singleton/shared options,
parallel scope use, automatic execution-strategy retry and unknown-commit recovery are outside scope.
Direct constructors/options omitting common configuration and SQL/bulk writes bypass integration.
Trusted migration/seed fixtures can use raw native options and must not claim coordinated validation.

Fact/row correspondence, schemas, root-version policy, domain rules, handlers, tenancy admission,
authorization and integration-message mapping remain consumer-owned. Other interceptor families,
durable D2.2 reactions and template capability presets remain future work. Current library-local
contracts and ADR 0016 supersede prior implementation descriptions; earlier reports retain dated
evidence and point to replacements for removed source files.
