# D2.1: required local domain reactions

Current review entry: [EventReactions consolidation](event-reactions-consolidation.md)
supersedes the earlier package names, aggregate base and marker requirements. Descriptions and
test counts below retain their dated scope; source links lead to current replacements.

Superseded integration, 2026-10-15: [D2.1c](d2-1c-domain-event-integration-simplification.md) replaces
the original Sales-only coordinator with registered Gloaming handlers and removes default row/fact
mirroring. Results below describe that earlier implementation, not today's additional safeguards.

Date: 2026-10-14. Prepared for owner review on `feat/ddd-sample-patterns`, after D1.1
checkpoint `f55d803`. [Selected scope](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/d2-1-local-domain-reactions.md) and
[executable usage](../../samples/Wholesale/IntegrationTests/Scenarios/DomainEvents/README.md) define this increment.

## Outcome and consumer ownership

Sales draft submission constructs an immutable accepted candidate and a private `DraftSubmitted`
fact. The row adapter explicitly registers that fact with the owning context; domain instances
are not EF entities. Required handling loads a separate customer order allowance and invokes
its reserve decision. Submission freezes captured draft terms. Each accepted reservation advances
the allowance version, including several reservations staged before one native save.

The allowance enforces a trusted caller's positive currency-specific ceiling against its complete
reserved amount. Both aggregates persist in one caller-owned native Sales transaction. Two orders
can individually pass their limits yet compete on the allowance's original-version predicate;
one succeeds and the other rolls back. This is a bounded sample admission policy, not credit
assessment, payment, production authorization or stock reservation.

The finite journey submits 40 EUR, rejects a 70 EUR submission against the 100 EUR allowance,
then edits/submits 50 EUR in a fresh operation. Final allowance: reserved 90 EUR, version 3.
It demonstrates automatic save handling and explicit staging through actual consumer usage.
Ordinary D1.1 draft creation/editing remains independent of allowances; opening and saving the
customer's currency allowance is explicit setup before submission.

Money moves from the DraftOrders folder/namespace to Sales because both domain aggregates use
its existing nonnegative amount/currency semantics. No generic monetary policy is inferred.
Historical report links are repaired; existing monetary validation is unchanged. The native
migration adds `is_submitted` with false default and one `customer_order_allowances` table,
including same-tenant customer FK, version token and amount constraints. Earlier migration
files are unchanged; an existing valid draft remains editable and raises no historical fact.

## Dispatch comparison and lifecycle

Owning-context SaveChanges is the default boundary for this sample: required staging runs
before tenant, draft, allowance, audit, inbox/outbox and workflow validation, then native save.
The bool overrides cover all four native overloads. Sync handling uses native sync reads;
async handling awaits native async reads and cancellation. There is no sync-over-async bridge.

`StageRequiredDomainReactionsAsync` exercises explicit application coordination with the same
handler. Staging twice before save does not reserve twice. Default save handling retains the
required reaction even when the caller omits explicit staging. Scanning EF rows, row-diff event
inference, a mediator and an event subscription registry were not selected.

One bounded batch handles at most 32 distinct registered submissions in order. Further facts
during handling and nested saves are rejected. A recursively draining cascade is intentionally
unsupported: one known reaction needs no generic cycle-resolution engine. The 33rd registration
is rejected before modifying its row; the accepted batch can be saved before later decisions.

Facts remain pending after staging and are consumed after successful native save, before commit.
Unchanged repeat saves do not repeat effects; later newly accepted facts in the same transaction
are handled. SaveChanges(false) requires explicit AcceptAllChanges before subsequent work.
Handler/save/cancellation faults invalidate participating reaction contexts; they cannot retry
or skip the handler. Roll back/dispose, then re-decide in a fresh scope. A context that handles
facts stays bound to its native transaction and cannot continue saving after it ends.

Save success still does not mean committed success. Explicit rollback and deferred server-side
commit rejection preserve both previous aggregates, even after facts were consumed at save.
Recovery uses a new scope. No same-context rollback repair, ambiguous connection-loss guarantee
or independently committed external effect is claimed. No OS-process-death proof was added.

## Proven and rejected boundaries

- Creation/editing, hydration, ordinary reads, stale submissions, already submitted decisions
  and rejected decisions do not register submission facts.
- Required handling enforces the separate allowance's rules and native version, including
  independent decisions on different order roots. Save/handler/commit failures preserve both
  committed states; cancellation and nested-save/further-fact attempts do not commit a producer.
- Fault-injected work added during the actual handler read is checked by the existing tenant
  guard. Explicitly staged allowance changes cannot be tampered with before save. Tracked
  submission without its accepted fact and unsupported draft deletion are rejected.
- Bulk ExecuteUpdate, raw EF SQL and direct provider writes are executable counterexamples:
  they can mark a draft submitted without reserving the allowance. A later empty save neither
  discovers a fact nor repairs that missing effect. ExecuteDelete and other SQL forms likewise
  sit outside the supported tracked aggregate protocol; not every form was individually tested.
- Callback code that mutates business state after validation is outside this recipe. A context
  hook is not a universal guard against consumers with direct database/tracker access.

## Verification

Fresh executions after final code changes on 2026-10-14:

| Check | Result |
| --- | --- |
| Whole active solution build | Passed, zero warnings/errors. |
| D2.1 domain/consumer suite | 36 passed: 2 pure domain cases and 34 PostgreSQL cases. |
| D1.1 relational aggregate suite | 42 passed. |
| D1 state-stored/event-sourced comparison | 22 passed. |
| HTTP admission/profile/audit suite | 105 passed. |
| Native persistence/migration suite | 36 passed. |
| Architecture, including new host boundary | 84 passed. |
| Existing workflow suite | 62 passed, including PostgreSQL/RabbitMQ behavior. |
| Active solution style/analyzers | Passed, no changes requested. |
| CSharpier, including generated files | Passed; 672 files checked. |
| Frozen archive | All 800 original files match the manifest. |
| Documentation and review preservation | All 364 local links in 12 changed/new Markdown files resolve; whitespace checks pass. Index unchanged; all changes unstaged. |

All **387 tests passed**, with zero failures/skips. The initial 35-case D2.1 run passed;
final verification adds explicit deletion rejection and extends false-acceptance handling
across later facts. Native migration scaffolding initially needed the repository's file-scoped
namespace and collection-expression formatting; the final build/style checks pass.

Earlier D1.1 results are historical evidence, not substitutes for these new executions.
Event-store/codec families, browser/Aspire topology and template generation were not rerun;
no change to their library contracts or deployment composition is claimed.

## Extraction and remaining work

**No new reusable mechanism was proven.** Private facts, required handlers, allowance ceilings,
submission eligibility and failure presentation are consumer-owned. The typed pending-fact
collection, save-phase wiring, transaction binding and failed-context policy are extraction
candidates only if another real event/handler shape repeats meaningful correctness work.
This one sample protocol does not establish a library interface. No Gloaming source/interface,
dependency, template preset or generated composition was changed.

Sample finding: an application/persistence handler can invoke another domain aggregate's behavior
and preserve a required local effect atomically without event sourcing. Template finding: the
context overrides and explicit operation scope are editable recipes; no template-generation or
browser/Aspire topology was exercised in this increment. D2.2 durable deferred reactions remain
unimplemented: no retained work record, broker publication, receiver receipt, restart or semantic
idempotency contract was added. Submission release, allowance adjustment and further reactions
require their own concrete scopes.

## Review-worthy files

- [Domain submission](../../samples/Wholesale/modules/Sales/Sales/DraftOrders/DraftOrderAggregate.cs):
  accepted private fact versus fact-free hydration and immutable submitted terms.
- [Command adapter](../../samples/Wholesale/modules/Sales/Sales/DraftOrders/DraftOrderCommands.cs):
  explicit fact registration alongside the owning versioned row transition.
- The original RequiredSalesReactions coordinator was subsequently removed; its
  [registered handler replacement](../../samples/Wholesale/modules/Sales/Sales/OrderAllowances/ReserveCustomerAllowance.cs)
  and [context overrides](../../samples/Wholesale/modules/Sales/Sales/SalesDbContext.cs): native
  sync/async handling, consumption, false acceptance, faults and validation order.
- [Allowance aggregate](../../samples/Wholesale/modules/Sales/Sales/OrderAllowances/OrderAllowanceAggregate.cs),
  [mapping](../../samples/Wholesale/modules/Sales/Sales/OrderAllowances/OrderAllowanceRow.cs) and
  [consumer Contracts](../../samples/Wholesale/modules/Sales/Sales.Contracts/ICustomerOrderAllowances.cs).
- [Concurrency/fault proofs](../../samples/Wholesale/IntegrationTests/Reactions/ReactionTests.Faults.cs)
  and [write-boundary proofs](../../samples/Wholesale/IntegrationTests/Reactions/ReactionTests.Boundaries.cs).

All D2.1 changes remain unstaged and uncommitted for owner review. The index remains the D1.1
checkpoint snapshot; its earlier approval does not authorize committing these changes.

## Primary reference checks

[EF Core 10.0.12 DbContext source](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/DbContext.cs)
confirms convenience overload forwarding; actual consumer tests prove these overrides execute.
[Native transaction guidance](https://learn.microsoft.com/en-us/ef/core/saving/transactions)
supports explicit grouping of multiple saves, while
[direct bulk writes](https://learn.microsoft.com/en-us/ef/core/saving/execute-insert-update-delete)
execute without the tracked-save phase. Sources checked on 2026-10-14; these references support
API constraints, not substitutes for the new PostgreSQL proofs.
