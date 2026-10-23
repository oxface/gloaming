# D2.1d: module-owned composition and EF interception

Current review entry: [EventReactions consolidation](event-reactions-consolidation.md)
supersedes the earlier package names, aggregate base and marker requirements. Descriptions and
test counts below retain their dated scope; source links lead to current replacements.

Historical record: the owner subsequently authorized [EF1.1](ef1-1-capability-composition.md)
common capability composition and clear-before-handling dispatch. That implementation supersedes
retained sessions, Stage, owned transactions and completion enforcement described below. Source
links now lead to replacements; test counts below remain dated evidence.

Date: 2026-10-15 (Europe/Kiev). Owner-directed cleanup is implemented for review on
feat/ddd-sample-patterns. This supersedes D2.1c's explicit save service and the original D2.1d
proposal. The existing staged snapshot is preserved; this increment is unstaged and uncommitted.

## Outcome

AddDomainEvents now installs a scoped DomainEventSaveChangesInterceptor<TContext,TModule> through
native ConfigureDbContext. Both sync and async save paths dispatch cascades, observe completion,
failure and cancellation, and retain the existing module-qualified registry/sink. Native context
failure events also invalidate concurrency failures. The core remains package-free and independent
of EF/DI; its split save lifecycle supports callback-based persistence without duplicated dispatch.

The host calls AddDirectOrdersModule and AddMappedQuotesModule, builds one native provider, then
uses ordinary operation scopes. Both standalone contexts take only native options. Module extensions
own typed handler registration; handler classes own reactions. MappedQuoteCommands owns row/domain
adaptation and injects its module sink. ConsumerScope, generic production handler closures, context
session/dispatcher properties and explicit save overrides are removed. Test knobs stay in fixtures.

The old IDomainEventSave, RegisteredDomainEventSave and EfDomainEventSave files are removed.
SaveChanges(false) uses native ChangeTracker.AcceptAllChanges; no parallel acceptance flag remains.
The native-misuse counterexamples deliberately show that early acceptance can discard writes and
repeat saving can fail through native EF constraints/concurrency. No default row/fact mirror,
aggregate write guard or invented unit of work is introduced.

Sales uses the same registered interceptor. Its existing tenant/audit/inbox/outbox/workflow checks
move into a local validation interceptor appended in OnConfiguring, after domain dispatch. This
preserves validation of handler-created writes and of other Sales compositions that do not select
domain events. No migrations, business rules or persistence identities change in this cleanup.

## Mechanism and ownership findings

The reusable integration proven here is opt-in EF interception around the existing bounded local
dispatcher, including split completion/failure handling and deferred scoped handler construction.
Module extensions and native scopes are consumer setup patterns, rather than a new library provider
or scope abstraction. Domain events, handlers, rules, mappings, keys, ownership, native concurrency
and required-reaction policy remain consumer-owned. No new reusable domain-mapping mechanism was
proven. No template preset, event log, message publication or durable deferred lifecycle is added.

The AlCopilot reference informed the module-registration/options-only-context shape. Its event
recording, GUID/time metadata, shared handler resolution and async-only interceptor were not copied.
[The pinned comparison](al-copilot-domain-event-design-comparison.md) retains the source findings and
evidence limits separately from these new Gloaming runtime proofs.

## Save boundary and explicit limits

Caller transactions remain the default and are never committed by the adapter. Optional
CreateIfAbsent begins before dispatch and commits only its transaction at the interceptor's
completion position. After an observed deferred server rejection, buffers remain pending and the
session is faulted. A participating context is discarded when its transaction ends.

An interceptor cannot wrap callbacks that run after it. A later completion interceptor or
DbContext.SavedChanges event can throw after Gloaming's successful completion and owned commit.
Consumers that need commit after every save callback returns use a caller transaction. New
PostgreSQL proofs demonstrate rollback after a later callback rejects both sync and async saves.

EF 10.0.12 invokes SavingChanges interceptors before entering its save try/catch, and its concurrency
catch invokes DbContext.SaveChangesFailed directly rather than the failure interceptor. The adapter
observes that native event for concurrency; exceptions in another saving callback still require
scope disposal. These are verified against the [exact native sync source](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/DbContext.cs#L634-L680)
and [async source](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/DbContext.cs#L775-L825),
and exercised through the new/retained failure proofs. This is why all save exceptions require
rollback/disposal and fresh state rather than retrying the old session.

Callbacks must not suppress native persistence or raise domain facts during saving/commit. Incoming
suppression is rejected; later suppression is unsupported. Late facts fail completion but cannot
undo committed work. SQL/bulk writes, premature native acceptance, arbitrary tracker edits and
independently committed effects bypass the protocol. Unknown commit outcomes and automatic
execution-strategy retry remain unsupported. D2.2 durable deferred reactions remain unimplemented.

## Verification

Fresh results for this interceptor implementation, rather than inherited D2.1c evidence:

| Suite | Passed | Relevant evidence |
| --- | ---: | --- |
| DomainTests | 23 | Existing bounded waves plus split lifecycle, busy session, late facts and retained failed buffers. |
| Standalone Consumer.Tests | 78 | Both shapes/all save overloads, native module composition, cascades, staging, deletion/detachment, sync write failures, native cancellation, concurrency invalidation, later-callback rollback, owned deferred rejection and native misuse boundaries. |
| DomainEventsDemo.Tests | 32 | Sales adoption, native tenant validation after handling, cascades, transactions, rejection, migration and bypass limits. |
| RichAggregateDemo.Tests | 40 | Existing rich aggregate/mapping behavior with native tracker acceptance. |
| ArchitectureTests | 87 | Declared/actual dependency boundaries and sample/library separation. |

These 260 focused tests passed. The finite standalone and Sales journeys execute inside the
consumer suites. Moving the shared Sales validators also justified these fresh regression runs:
DddDemo.Tests 22, PersistenceDemo.Tests 36, HttpIdentityDemo.Tests 105 and WorkflowDemo.Tests 62.
All **485 tests passed**, with no failures or skips.

The complete solution build passed with zero warnings/errors. Required dotnet format style and
analyzer verification passed; CSharpier checked 706 files; archive verification preserved all 800
original files from 15d1ec6ce85f872e4547b37c33658dea76b25d03. Staged/unstaged whitespace checks
passed, and changed Markdown source links resolve. Logs are /tmp/d2-1d-{build,core,consumer,sales,
rich,architecture,DddDemo,PersistenceDemo,HttpIdentityDemo,WorkflowDemo,style,analyzers}.log.
The staged-content manifest SHA-256 remained
2f4ee970853b889fa34d56641d89d181f6af65d7c801619458d4514ce47e1779.
No staging, unstaging, commit or push was performed.

## Files for review

- [Interceptor](../../src/Gloaming.EventReactions/Gloaming.EventReactions.EntityFrameworkCore/EventReactionServiceCollectionExtensions.cs),
  [registration](../../src/Gloaming.EventReactions/Gloaming.EventReactions.EntityFrameworkCore/EventReactionServiceCollectionExtensions.cs)
  and [session lifecycle](../../src/Gloaming.EventReactions/Gloaming.EventReactions/EventReactionDispatcher.cs): public surface,
  transaction ownership, busy/failure state and event acknowledgment.
- [Direct module registration](https://github.com/oxface/modulith-foundry/blob/dd6eb77a4a0e3c8afd7af710d7689ca265d43a64/src/Gloaming.EventReactions/samples/EventReactionsDemo/Consumer/DirectOrdersRegistration.cs),
  [mapped module registration](../../src/Gloaming.EventReactions/samples/EventReactionsDemo/Consumer/MappedQuotesRegistration.cs),
  [typed reactions](https://github.com/oxface/modulith-foundry/blob/dd6eb77a4a0e3c8afd7af710d7689ca265d43a64/src/Gloaming.EventReactions/samples/EventReactionsDemo/Consumer/DirectOrderReactions.cs) and
  [mapped commands](../../src/Gloaming.EventReactions/samples/EventReactionsDemo/Consumer/MappedQuoteCommands.cs): consumer ownership.
- [Sales context](../../samples/Wholesale/modules/Sales/Sales/SalesDbContext.cs): existing validation order.
- [Native failure proofs](https://github.com/oxface/modulith-foundry/blob/dd6eb77a4a0e3c8afd7af710d7689ca265d43a64/src/Gloaming.EventReactions/samples/EventReactionsDemo/Consumer.Tests/DispatchTests.Faults.cs),
  [core lifecycle proofs](../../src/Gloaming.EventReactions/tests/EventReactionsTests/DispatcherTests.cs) and
  [current package contract](../../src/Gloaming.EventReactions/docs/local-dispatch.md).

Earlier reports retain dated results and superseded setup with replacement source links. The
[plan](../plans/d2-1d-module-owned-event-composition.md), [design](../design.md),
[ADR 0016](../adr/0016-local-domain-event-dispatch.md) and package/sample READMEs now describe this
interceptor composition. Library implementation/public types still require owner line-by-line review.
