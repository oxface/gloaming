# D2.1a: local domain-event extraction assessment

Current review entry: [EventReactions consolidation](../reports/event-reactions-consolidation.md)
supersedes the earlier package names, aggregate base and marker requirements. Descriptions and
test counts below retain their dated scope; source links lead to current replacements.

Historical record: the owner subsequently authorized [EF1.1](../reports/ef1-1-capability-composition.md)
common capability composition and clear-before-handling dispatch. That implementation supersedes
retained sessions, Stage, owned transactions and completion enforcement described below. Source
links now lead to replacements; test counts below remain dated evidence.

Historical interface/evidence: [D2.1d](d2-1d-module-owned-event-composition.md) supersedes the explicit save-service integration
and custom false-save acceptance described below. Source links for removed adapters/composition
now point to their current interceptor/module-registration replacements; earlier results remain dated.

Date: 2026-10-14. Assessment completed on `feat/ddd-sample-patterns`, based on the owner's
proposed marker/base, module registry, typed handlers, sink and bounded SaveChanges utility.

Subsequent status, 2026-10-15: the owner authorized promotion into Gloaming.Domain and its optional
EF adapter. The [promotion report](d2-1b-gloaming-domain-promotion.md) contains fresh library results.
This report retains the dated prototype assessment, not the current promotion status. Links to
mechanism source/core proofs follow their new family locations; consumer migrations are unchanged.

## Outcome and recommendation

**A reusable local dispatch/save lifecycle is now proven in standalone sample prototypes.**
Recommend extracting a platform-only core and an optional EF Relational adapter after owner
review of the [complete proposed interface](../plans/d2-1a-domain-event-extraction.md).
No Gloaming package or library interface has been adopted by this assessment. No ADR is needed
until the interface/dependency choices are settled. D2.2 durable deferred reactions remain separate.

The same core and save adapter execute two real PostgreSQL consumers: directly tracked aggregates
with an optional base, and immutable domain decisions mapped to separate persistence rows through
an explicit sink. Both have two submission handlers and a downstream allocation handler. Deletion
releases the reservation, with facts surviving deletion/detachment until successful native saving.
There is no reflection scanner, global service provider, event store, transport or tenant dependency
in the core. The executable, migrations and [consumer setup](../../samples/EventReactionsDemo/README.md)
are included, rather than treating a copied routing loop as sufficient extraction evidence.

D2.1's earlier conclusion that no reusable mechanism was proven remains accurate for that slice's
single typed Sales reaction. This assessment supplies new evidence; it does not rewrite those
results or replace the owner's staged Sales implementation.

## Proven mechanism versus consumer policy

| Concern | New evidence and disposition |
| --- | --- |
| Marker, optional base and explicit sink | DirectOrders uses AggregateRoot; MappedQuotes has no base or tracked domain objects. Deletion is still an aggregate-specific fact, even when captured through a sink. |
| Module registry | Explicit scoped handler instances, exact CLR types, registration order, duplicate-instance rejection and frozen bindings. A standalone proof isolates two module registries even for the same public event type. Public visibility grants no cross-module subscription automatically. |
| Repeated draining | Collected FIFO waves execute all current handlers before discovering new facts/sources. Equal event values remain separate occurrences. Newly loaded allowance buffers participate in subsequent waves. |
| Bounded handling | Positive invocation ceiling, default 128 per successful native save; repeated staging cannot reset it. A cycle fails before native save and a finite oversized database cascade rolls back. |
| Save acknowledgment | Stage does not consume facts. Successful native save acknowledges handled prefixes; repeated saves do not repeat effects. Failure/cancellation requires a fresh operation. |
| Sync and async | Native sync handlers avoid blocking async work. Async-only handlers require async staging/save; async staging can precede native sync save. All four native SaveChanges families are exercised for both shapes. |
| Transaction ownership | Caller transaction is required by default. Optional CreateIfAbsent starts before handling, commits only its own transaction, joins existing transactions, and rejects reuse after the participating transaction ends. |
| Validation and failure | Validation sees the complete cascade. Handler rejection, nested save, wrong-owner staged rows, late facts, required write failures and a server-rejected deferred commit leave no committed partial effects in the exercised protocols. |
| Concurrency | Different producer roots compete on the same allowance's native version predicate. Losing operations roll back, then re-decide using a fresh context. The library does not create those predicates. |
| Native write bypass | An explicit direct-SQL counterexample submits a row without reservation/receipts; a subsequent empty save does not repair it. SQL/bulk writes remain outside event collection. |

The reusable mechanism owns buffering, routing, draining, invocation bounds, acknowledgment and
operation/transaction lifecycle. Consumers still own facts, subscriptions, domain decisions,
source/sink adaptation, persistence mapping, ownership checks, native concurrency predicates,
required row/fact matching, and rollback/disposal of caller transactions. In particular, this
assessment does **not** establish that Sales-specific fact/row validation can be removed.

No new production library or template preset was added. A reusable mechanism was proven as a
candidate; its public API and promotion remain proposed. The business examples and migrations
are sample policy. No archived implementation was imported or modified.

## Interface choices requiring owner review

The proposal records the entire public surface, not just marker interfaces. Review the following
choices together with the executable usage:

- One operation-scoped registry/session, generic module tag, exact-type dispatch and rejection of
  unhandled facts. No implicit integration publication, polymorphic subscriptions or DI scanning.
- Optional aggregate inheritance plus a composition buffer/sink. A buffer belongs to one session;
  recovery rehydrates fresh objects rather than replaying an old failed queue/tracker.
- Explicit native sync handler interface alongside async handlers. Serialized execution and a
  configurable handler-invocation ceiling; collected source order does not promise global raise order.
- Default caller-owned transaction, optional adapter-owned save transaction, and disposal after the
  participating transaction ends. The adapter cannot retroactively include earlier command writes.
- Explicit adapter AcceptAllChanges after **every** successful false-acceptance save, including an
  empty one. Acknowledgment before saving is rejected. Direct native tracker acknowledgment does not
  clear the adapter's flag. This is a deliberate integration obligation rather than assuming version
  property names or inferring acceptance from arbitrary tracked mutations.

Following owner naming feedback on 2026-10-15, proposed promotion would create `Gloaming.Domain`
with platform-only dependencies and `Gloaming.Domain.EntityFrameworkCore` depending on core and
EF Relational. Those identities are proposals; current assemblies use sample namespaces.
The core dependency proof inspects actual
assembly references. The two EventHandler interface names have narrowly scoped CA1711 suppressions
for domain-handler vocabulary; no analyzer family is disabled globally.

The same follow-up renamed the prototype's optional base from DomainAggregate to AggregateRoot.
IDomainEventsSink remains the proposed write-only raising interface. Cascade handling already lives
in the session's Drain/DrainAsync loop: handler-produced facts are collected in subsequent waves
before one native save. The ceiling bounds cycles; no persistence callback/post-save event cycle
is supported. These clarifications do not add another runtime capability.

Fresh checks for the 2026-10-15 naming follow-up: both affected test projects build with zero
warnings/errors; 19 core and 69 PostgreSQL cases pass again, with no failures or skips. CSharpier
checks all 25 assessment C# files, documentation links resolve, and the owner's staged snapshot
is unchanged. The rename and documentation edits remain in the working tree.

## Verification executed for this assessment

All counts below are fresh executions for D2.1a, with zero failures or skips:

| Check | Result |
| --- | --- |
| New standalone Core.Tests | 19 passed |
| New PostgreSQL Consumer.Tests | 69 passed, both persistence shapes and executable journey |
| Existing Sales DomainEventsDemo.Tests | 36 passed |
| Existing ArchitectureTests | 84 passed |
| Full solution build | Passed, zero warnings/errors |
| Full solution style/analyzer verification | Passed with verify-no-changes |
| CSharpier including generated files | Passed, 697 files |
| Archive integrity | All 800 original files verified |

There are **88 new tests and 120 freshly rerun regression tests**, 208 total. Earlier D1.1/D2.1
test totals are historical evidence, not part of this new count. The full repository test suite was
not rerun: existing Sales runtime code is unchanged, and focused regression plus full solution
build/style/analyzer checks cover this isolated assessment's integration changes.

The first database pass exposed six fault-injection probe failures: the probe expected quoted
table names while PostgreSQL emitted unquoted lowercase names. The probe was corrected and all
fault cases rerun. Final coverage was then expanded to all save/deletion overloads and explicit
acceptance guards. An initial sandboxed test launch could not open the test runner's local pipe;
the tests passed with the permitted local IPC/container execution environment.

## Review map and preservation

- [Core contracts](../../src/Gloaming.EventReactions/Gloaming.EventReactions/Contracts.cs),
  [buffer/base](../../src/Gloaming.EventReactions/Gloaming.EventReactions/PendingEvents.cs),
  [registry](../../src/Gloaming.EventReactions/Gloaming.EventReactions/EventReactionRegistry.cs),
  [session](../../src/Gloaming.EventReactions/Gloaming.EventReactions/EventReactionDispatcher.cs)
  and [EF adapter](../../src/Gloaming.EventReactions/Gloaming.EventReactions.EntityFrameworkCore/EventReactionServiceCollectionExtensions.cs)
  are the proposed mechanism implementation.
- [DirectOrders](../../samples/EventReactionsDemo/Consumer/DirectOrders.cs),
  [MappedQuotes](../../samples/EventReactionsDemo/Consumer/MappedQuotes.cs) and the
  [finite journey](../../samples/EventReactionsDemo/Consumer/ExtractionJourney.cs) show
  actual consumer obligations.
- [Core proofs](../../src/Gloaming.EventReactions/tests/EventReactionsTests/DispatcherTests.cs),
  [database lifecycle proofs](../../samples/EventReactionsDemo/Consumer.Tests/DispatchTests.cs)
  and [database fault proofs](../../samples/EventReactionsDemo/Consumer.Tests/DispatchTests.Faults.cs)
  show expected outcomes and limits independently of the implementation.

Five assessment projects are included in the solution. CI explicitly maps this sample directory
to the existing consumer test job and runs both new suites. Development instructions and the
extraction status link this assessment. There is no change to existing runtime source or migrations.

The owner's existing partial staged D2.1 snapshot is unchanged. Its pre-existing working files
are unchanged except the solution, two CI files, development instructions and extraction status,
which receive assessment-only additions in the working tree. All new assessment files are unstaged.
No staging, commit or publication was performed.

## Remaining limits

This is local synchronous-before-save handling, not durable delivery. Handlers stage changes in
one native context/transaction and complete before returning. Independently committed external
effects, process death, ambiguous connection loss during commit, automatic retries and background
redelivery are unproven. Tests cover a server-rejected deferred COMMIT, not an unknown commit result.
Cancellation is cooperative; a handler that never returns cannot be bounded by an invocation count.

Callbacks and context overrides must be trusted and wired consistently. The adapter cannot guard
all arbitrary tracker mutation, bypass writes, alternate contexts or later independently committed
operations. Caller rollback can follow a successful save; acknowledged queues do not repair that
context. D2.2 must define durable reaction/integration policy separately, using the retained
inbox/outbox where appropriate rather than turning these private facts into public messages by default.
