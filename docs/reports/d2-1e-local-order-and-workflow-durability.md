# D2.1e: bounded local reactions, capture order and workflow durability

Current review entry: [EventReactions consolidation](event-reactions-consolidation.md)
supersedes the earlier package names, aggregate base and marker requirements. Descriptions and
test counts below retain their dated scope; source links lead to current replacements.

Date: 2026-10-15 (Europe/Kiev). Owner-directed follow-through to the
[independent reassessment](domain-events-independent-reassessment.md). The owner retained local
transactional dispatch for bounded reactions, preferred explicit orchestration for complex
dependencies, and selected Raise order across event sources. This is prepared on
feat/ddd-sample-patterns; the existing staged review snapshot remains unchanged.

## Decision and implemented ordering

Required intra-module reactions participate in the producer's owning transaction. All required
handlers finish before commit; a failure aborts the operation. Explicit application orchestration
remains supported and is recommended for complex or interdependent business decisions. Handler
registration routes facts and does not infer a dependency graph or schedule a workflow. D2.2 is
deferred until a concrete requirement needs completion after producer commit; it is not a default
replacement for atomic local writes.

PendingDomainEvents now assigns an internal process-local sequence to each Raise occurrence.
The dispatcher merges each wave by capture sequence across aggregate buffers, composed buffers
and the operation sink. Previously, sink/source enumeration grouped events by buffer and could
reorder interleaved decisions. Handler registration order within each event is unchanged. Newly
raised handler facts drain in later waves, ordered by their own capture sequence.

One Interlocked counter spans buffers without requiring aggregate constructors to receive a shared
operation service or exposing event-envelope metadata. Unrelated operations may introduce gaps;
sequence values have no domain meaning. They are neither persisted nor an aggregate version,
timestamp or cross-process order. The buffers remain serialized operation state. Equal payloads
and the same event object raised twice remain separate occurrences. Existing clearing, failure,
cancellation and invocation-ceiling contracts remain.

The collector must expose all current participating buffers. Previously hidden facts discovered
after work has completed cannot be retroactively ordered before that work. Newly loaded database
roots participate when tracked; loading does not regenerate historical events. A rule requiring
B's decision before C remains explicit orchestration or an explicitly subsequent fact. Capture
order does not make a cyclic or incorrectly modeled dependency graph correct.

AggregateRoot remains optional and now has its own source file instead of being hidden below
PendingDomainEvents. No public domain interface, constructor or event shape changes. No new
domain rules, transaction owner, durable queue or dependency-graph mechanism is added.

## Executable ordering evidence

Four new core cases exercise sync/async dispatch with deliberately interleaved sources and a
source enumeration order that disagrees with capture order. They cover aggregate-style buffers,
the operation sink, duplicate source collection, handler-created sources and downstream waves.

Two real PostgreSQL consumer cases demonstrate a business-sensitive failure of buffer grouping:
one budget starts reserved at 40 with a ceiling of 100. Two independently buffered reservation
batches request +60, -40, +40 in that order. Grouping the first batch's +60 and +40 would exceed
the ceiling before the release; capture order succeeds and commits a budget of 100, first batch
100 and second batch 0. Both native save paths execute through registered Gloaming capabilities,
and a fresh observer verifies the committed result. This is consumer-owned sample policy rather
than a reusable budget or aggregate design.

The newly proven library guarantee is capture-order preservation across participating sources.
No new reusable workflow durability mechanism was proven: that part reviews and re-executes
existing capabilities.

## Does the existing workflow persist the saga itself?

Yes, within its explicit request/reply protocol. Source inspection of EfWorkflow,
WorkflowInstanceRecord and the native workflow guard confirms:

- Initial input, consumer state, definition version, status and named continuation are persisted.
- The pending command and operation ledger retain requested identities and semantic outcomes.
- Applying a turn stages process changes and the next outbox request in the same native transaction.
- Inbox processing commits a reply's completion with the continuation and next request.
- Native version predicates arbitrate competing turns. Equal retained input/outcomes do not
  re-run accepted starts/continuations; conflicting outcomes fail.
- Recovery executes the saved continuation when admitted work arrives. It does not restore a
  C# call stack, run every waiting saga automatically or replay earlier business methods.

The owning business module retains the saga definition, state and compensation policy. The host
only composes dependencies and delivery. Existing Sales code follows that ownership and performs
compensation through explicit next requests. Compensation can be refused and require intervention.

The existing transaction proofs cover rolled-back start/continuation, atomic request enqueue,
inbox completion, duplicate replies and competing writers. Wholesale recovery proofs kill a host
after a committed compensation effect and recover its retained reply without repeating compensation.
Fresh suite executions are listed below; the original WF1/WF2 reports retain separately dated evidence.

## Direct in-process module calls: supported design candidate, unproven adapter

Process persistence does not automatically make arbitrary module calls durable. If a target
module commits and the process dies before the saga records its result, the durable pending step
can be retried. The target must recognize that same operation and return its retained outcome
rather than repeat the effect. Losing the result or issuing a new identity on retry is unsafe.

A reliable in-process delivery path through module Contracts therefore needs retained outgoing
intent before the call, a stable operation identity, target-side idempotency/result retention,
and recoverable result delivery. It can use the same process and need not require a broker, but
the current packages do not supply or prove that automatic direct-contract adapter. Do not put
independently committing calls inside the workflow's decision delegates: their current contract
requires returning a proposed turn without such effects.

[D2.3](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/d2-3-module-consistency-and-coordination-research.md) now explicitly retains module
ownership and this adapter/recovery question. Shared native transactions through module Contracts
remain another separately assessed choice; saga durability does not establish their atomicity.
No workflow runtime, public Contracts, compensation protocol or transport was changed here.

## Verification and review surface

The full active solution built with zero warnings/errors. Fresh tests passed with no skips:

| Suite | Passing tests |
| --- | ---: |
| Domain core, including four new capture-order cases | 16 |
| Standalone consumer, including two new PostgreSQL ordering cases | 100 |
| Sales required local reactions | 30 |
| Architecture | 88 |
| Workflow PostgreSQL | 38 |
| Wholesale workflow, including process-death/compensation recovery | 62 |
| **Total** | **334** |

Workflow suites exercise existing durability rather than a new direct-contract implementation.
Full solution style/analyzers and CSharpier passed (717 files). Archive verification retained all
800 original files, changed Markdown targets resolve and git diff --check passed. The staged
manifest remained 88a635b29bbbdd87c57294c276ed8ba487b3be17657a904bf88644cc84d21680.
Logs use /tmp/domain-policy-*.log. These fresh executions are separate from EF1.1's historical
930-test run. No benchmark, distributed-order test or direct-call crash-recovery proof is claimed.

Review [capture](../../src/Gloaming.EventReactions/Gloaming.EventReactions/PendingEvents.cs),
[dispatch](../../src/Gloaming.EventReactions/Gloaming.EventReactions/EventReactionDispatcher.cs),
[core ordering proofs](../../src/Gloaming.EventReactions/tests/EventReactionsTests/DispatcherTests.cs),
[PostgreSQL ordering usage](https://github.com/oxface/modulith-foundry/blob/dd6eb77a4a0e3c8afd7af710d7689ca265d43a64/src/Gloaming.EventReactions/samples/EventReactionsDemo/Consumer.Tests/EventOrderingTests.cs),
[local contract](../../src/Gloaming.EventReactions/docs/local-dispatch.md) and
[workflow ownership/durability](../../src/Gloaming.Messaging/docs/workflows.md).

Template behavior and dependencies are unchanged. No new template preset or saga framework is
justified. Consumer-owned rules, graph complexity, concurrency, source participation, failure disposal
and whole-operation retry remain explicit. Staging, committing and publication were not authorized.
