# D2.1b: Gloaming.Domain promotion

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

Date: 2026-10-15. The owner authorized implementing the reviewed local mechanism as Gloaming.Domain
and Gloaming.Domain.EntityFrameworkCore, retaining IDomainEventsSink and using AggregateRoot for the
optional base. Work is prepared on `feat/ddd-sample-patterns` for line-by-line review, without a commit.

## Outcome

The [D2.1a assessment](d2-1a-domain-event-extraction.md) is promoted into two independently selectable
library projects under [Gloaming.Domain](../../src/Gloaming.EventReactions/README.md):

| Project | Extracted mechanism | Dependencies |
| --- | --- | --- |
| Gloaming.Domain | IDomainEvent, typed async/sync handlers, IDomainEventsSink, PendingDomainEvents, optional AggregateRoot, module registry and bounded session | Platform only |
| Gloaming.Domain.EntityFrameworkCore | Callable native save adapter, false-save acknowledgment, caller/default versus optional owned transaction lifecycle | Core plus EF Relational |

Namespace and assembly identities now match those package/project names. The former prototype
projects are removed from the working tree; no duplicate runtime implementation or compatibility
facade remains. Core proofs move to the family's DomainTests project. The existing standalone
sample remains at its current location and now references the actual Gloaming projects for both
directly mapped aggregates and immutable domain decisions mapped to separate rows.

This extracts the reusable mechanism already proven by D2.1a. It adds no further runtime guarantee
beyond promotion and the agreed naming. No new reusable runtime mechanism was proven in this step;
fresh executions verify the promoted implementation and three new architectural proofs verify the
actual package/consumer boundaries. No sample business behavior, module Contracts, migrations or
ownership/version rules moved into a library. Sales retains its specialized D2.1 protocol, including
fact/row validation; replacing it is not implied by this independent consumer adoption.

## Preserved behavior and consumer obligations

The session drains handler-produced facts and newly discovered sources in collected waves until
empty. The default positive ceiling is 128 handler invocations per successful native save;
repeated staging cannot reset it. It validates after the full cascade, then invokes native save
once and acknowledges handled prefixes. Two submission handlers and the allocation cascade execute
through this same mechanism in both consumer shapes. Equal facts remain separate occurrences.

Registration is explicit, scoped, exact-type and frozen per session. The optional base requires no
key/version model; the sink also supports composition and immutable decisions. Native sync handling
does not block on async-only handlers. Public facts do not automatically become inter-module
contracts. Context overrides, collection, callbacks and handler implementations must be trustworthy.

The EF adapter requires an existing transaction by default. CreateIfAbsent starts before handlers
when facts are pending and no native transaction exists, commits only its own transaction and joins
caller transactions without committing them. A context is disposed after its participating
transaction ends. After every successful false-acceptance save, including an empty one, the adapter's
AcceptAllChanges must be called before further work. Native tracker acceptance does not clear that
flag. Failures require a fresh context/decision and caller rollback of joined transactions.

Consumers still own facts, subscriptions, complete-result validation, row/fact matching, owner and
authorization rules, mapping, schema, expected-version predicates and caller transaction disposal.
The library does not interpret the meaning of an allowance or automatically prove required effects.

## Verification executed after promotion

| Fresh check | Result |
| --- | --- |
| Family-owned DomainTests | 19 passed |
| Standalone PostgreSQL Consumer.Tests | 69 passed, including the finite executable journey and both persistence shapes |
| ArchitectureTests | 87 passed, including three new Domain boundary proofs |
| Full solution restore/build | Passed; build has zero warnings/errors |
| Full solution style/analyzer verification | Passed with verify-no-changes |
| CSharpier including generated source | Passed, 698 files |
| Archive integrity | All 800 original files verified |
| CI wiring | Updated YAML parses; Domain filter/output/job and inherited setup resolve |
| Documentation links | 1,772 local targets checked across 203 active documents |

All **175 tests** passed with zero failures or skips. The 19+69 cases are fresh executions of the
assessment scenarios against library assemblies; their scenarios are not newly invented tests.
ArchitectureTests contributes 84 existing cases and three new checks for dependency declarations,
actual assembly references and independent consumer adoption. Earlier D2.1/D2.1a execution totals
remain dated evidence and are not added to this count. The full repository test suite was not
rerun; existing Sales runtime code is unchanged and focused library/consumer/architecture checks
accompany full solution build, style and analyzer verification.

The first sandboxed restore ended before producing output; restoring through the permitted local
MSBuild IPC environment succeeded. Tests use that same permitted environment and existing local
Podman/Testcontainers setup. No checks were disabled to obtain these results.

## Review map and integration

- [Contracts](../../src/Gloaming.EventReactions/Gloaming.EventReactions/Contracts.cs),
  [buffer/base](../../src/Gloaming.EventReactions/Gloaming.EventReactions/PendingEvents.cs),
  [registry](../../src/Gloaming.EventReactions/Gloaming.EventReactions/EventReactionRegistry.cs),
  [session](../../src/Gloaming.EventReactions/Gloaming.EventReactions/EventReactionDispatcher.cs) and
  [EF adapter](../../src/Gloaming.EventReactions/Gloaming.EventReactions.EntityFrameworkCore/EventReactionServiceCollectionExtensions.cs)
  are the public mechanism and implementation for owner review.
- [Core setup](../../src/Gloaming.EventReactions/Gloaming.EventReactions/README.md),
  [EF setup](../../src/Gloaming.EventReactions/Gloaming.EventReactions.EntityFrameworkCore/README.md) and the
  [execution contract](../../src/Gloaming.EventReactions/docs/local-dispatch.md) document current consumer
  guarantees, options and obligations beside the packages.
- [DirectOrders](../../src/Gloaming.EventReactions/samples/EventReactionsDemo/Consumer/DirectOrders.cs) and
  [MappedQuotes](../../src/Gloaming.EventReactions/samples/EventReactionsDemo/Consumer/MappedQuotes.cs) consume the actual
  library namespaces. Their native migrations and finite business outcomes are unchanged.
- [Core proofs](../../src/Gloaming.EventReactions/tests/EventReactionsTests/DispatcherTests.cs),
  [database proofs](../../src/Gloaming.EventReactions/samples/EventReactionsDemo/Consumer.Tests/DispatchTests.cs),
  [fault proofs](../../src/Gloaming.EventReactions/samples/EventReactionsDemo/Consumer.Tests/DispatchTests.Faults.cs) and
  [boundary proofs](../../tests/ArchitectureTests/EventReactionBoundaryTests.cs) establish the
  current evidence and supported limits.

The solution groups the packages/core tests under Libraries/Domain and retains only consumer
projects under the sample. CI has a Domain lane running core and PostgreSQL consumer suites, with
its own changed-path selection and the existing repository integrity checks. Library/source
documentation, development commands, roadmap and current status point to the promoted implementation.
[ADR 0016](../adr/0016-local-domain-event-dispatch.md) records the agreed boundary and trade-offs.
No template preset, source snapshot or generated application graph changed.

The owner's staged review snapshot is preserved exactly. The relocations, references, new files,
CI and documentation changes are working-tree changes only. Staged former prototype/core-test
paths therefore appear as working-tree deletions with their replacements under the new family;
the index still contains the owner's earlier review versions. No staging, unstaging, commit,
package-registry publication or push was performed. Package projects exist in the repository;
release packaging/distribution is not claimed by this slice.

A pre-change manifest of 1,740 working files confirms that all 30 changed/relocated existing paths
belong to the intended integration, documentation or standalone consumer scope. All nine project,
source and test relocations have their expected destinations. Existing Wholesale, archive and
unrelated library working files remain byte-for-byte unchanged, and the staged index hash matches
the snapshot taken before promotion.

## Remaining gaps

The libraries support local handling before native save. Native-save callback facts are rejected,
not dispatched after persistence. Direct SQL/bulk writes and alternate contexts can bypass the
protocol; the negative database proof demonstrates that a subsequent empty save cannot repair
missing effects. The invocation bound cannot interrupt a handler that never returns.

PostgreSQL is the real database proof provider. Unknown commit results, process death, external
committed effects, automatic retries and independently deferred delivery remain unproven. Native
successful save may precede a later caller rollback; queues/trackers do not repair themselves.
No serializer, event store, mediator, repository abstraction or mandatory module graph is supplied.
D2.2 still needs its own durable reaction/integration policy, explicit inbox/outbox mapping,
receiver recovery and semantic idempotency proofs.
