# D1.1: rich state-stored aggregate and relational children

Current review entry: [EventReactions consolidation](../reports/event-reactions-consolidation.md)
supersedes the earlier package names, aggregate base and marker requirements. Descriptions and
test counts below retain their dated scope; source links lead to current replacements.

Subsequent boundary change, 2026-10-15: the owner removed the default tracked aggregate save guard
in [D2.1c](d2-1c-domain-event-integration-simplification.md). The guard-specific proofs below remain
historical; current guarantees cover the intended aggregate command path and native constraints.

Date: 2026-10-14. Initially prepared on `feat/rich-state-stored-aggregate`, from fetched
`origin/main` at `824c95b`, in `/tmp/modulith-foundry-d1-1`. At the owner's request, the changes
were moved into `/home/oxface/repos/modulith-foundry` on `feat/ddd-sample-patterns` after D1
was committed as `041c64a`. Both demos, CI checks and project references are retained. [Scope](../plans/d1-1-rich-state-stored-aggregate.md)
and [consumer usage](../../samples/Wholesale/RichAggregateDemo/README.md) define this increment.

## Outcome and ownership

Sales owns a draft order with stable line identities, captured quantities/prices and an
immutable total limit supplied by a trusted caller. The complete nonempty candidate must have
a representable total within that limit. Child add/change/remove operates on an immutable
candidate; rejected later input, totals or missing catalog references leave accepted/tracked
state unchanged. Only the final batch must remain nonempty and fit the limit.

Native root/child rows use separate `sales.draft_orders` and `sales.draft_order_lines` tables.
Every accepted mutation advances the root version once, even when it changes only a child.
An explicit single-query Include loads the complete graph; native version predicates fence
competing decisions on different children. An owning-context tracked-save guard requires root
participation, a complete graph, immutable root terms and a valid final candidate. Child IDs
are local to each order, and removal is explicit navigation plus Deleted staging, not orphan
inference. Root deletion and arbitrary child transfer are outside the supported recipe.

The customer remains a separate Sales aggregate referenced by identity. A composite owner/
customer FK prevents cross-tenant references. Catalog SKUs are checked through Inventory.Contracts
for creation/new lines, without accessing a peer implementation or creating cross-module FKs.
The native summary joins the current customer name and computes the line total; it does not
maintain another projection or copy the customer into the draft write model. Catalog lookup
does not reserve stock or certify eligibility/price at Sales commit.

Commands require trusted human/tenant scope and an already active native Sales transaction.
Changed is a proposal. The caller owns save, commit, rollback, fault classification and fresh
operation recovery. A native forward migration adds only the two draft tables and their keys,
checks and indexes; earlier migrations are preserved. The existing reservation workflow is a
separate process, not an automatic effect of creating a draft.

Regenerating the native snapshot also reorders existing entities/annotations under their
Gloaming names after the earlier mechanical rename. Those textual changes add no migration
operations to existing tables; the scaffolded Up/Down contains only the new draft tables,
relationships and indexes. All earlier migration files remain byte-for-byte unchanged.

## Mapping assessment and extraction decision

Direct EF mapping of a domain graph is valid consumer code, but mapping this immutable Money/
child model directly would require materialization/backing-field configuration and blending
tenant/version metadata with the domain shape. Separate rows allow explicit complete-candidate
validation, native keys and deliberate child deletion while keeping domain constructors pure.
The adapter is small owning-module code; this choice does not prescribe a generic repository,
mapping engine or row layer for all aggregates. Actual round trips/lifecycle proofs assess the
selected adapter, not a benchmark or executable proof of every mapping alternative.

**No new reusable mechanism was proven.** Money, child lifecycle, total-limit rules and SKU
policy are consumer-owned. Native FK/transaction/concurrency mechanics are reused. Complete-
graph/root-participation validation is a concrete sample mechanism with new proofs, but one
row shape does not establish a library API. It remains an extraction candidate only if another
real shape repeats meaningful correctness work. No library source/interface was changed.

Sample finding: aggregate-wide decisions work over multiple relational entities without event
sourcing. Template finding: explicit loading, staging and guard wiring are editable recipes;
no T1 preset or new generated composition was tested. Domain-event collection/handling and
SaveChanges dispatch remain the later D2 assessment, distinct from this validation-only guard.

## Verified limits and rejection

The proof suite contains an explicit counterexample to universal SaveChanges guard coverage:
ExecuteUpdate changes a child's quantity without advancing the root version, leaving a total
above its limit. The change tracker remains empty and a subsequent domain load rejects that
invalid state. Thus bulk/native writes are outside the supported aggregate-write protocol.
Ordinary row/FK/tenant constraints do not enforce the whole root-wide total rule. This new
negative proof supplies evidence for D2's write-path assessment; it does not implement dispatch.

Save success does not mean committed success. Required root/child failures and a deferred
commit failure roll back participating state; operation recovery uses a fresh scope. PostgreSQL
ends the transaction after the tested deferred-constraint commit rejection, so the proof disposes
it rather than attempting a second rollback on the completed transaction. This is a tested
server rejection, not proof of an ambiguous connection loss during commit.

Root limit, currency, customer and code are immutable after creation. The three-uppercase-letter
currency format is bounded sample validation, not an ISO catalogue. Real price/limit approval,
customer credit, submission/fulfillment, production authorization and stock reservation are not
delivered. Existing references are not continuously revalidated against catalogue changes.
One staged mutation per root is supported between saves; a later transaction needs a fresh scope.

## Verification

Fresh executions on 2026-10-14 in the original independent D1.1 worktree:

| Check | Result |
| --- | --- |
| Whole active solution build | Passed with zero warnings/errors. |
| D1.1 domain/consumer suite | 42 passed: 15 pure domain cases and 27 PostgreSQL cases; zero failures/skips. |
| Existing HTTP profile/admission/audit suite | 105 passed, zero failures/skips. |
| Native event consumer suite, excluding broker DispatchTests | 172 passed, zero failures/skips. |
| Existing persistence/migration suite | 36 passed, zero failures/skips. |
| Architecture suite, including new rich-aggregate host boundary | 83 passed, zero failures/skips. |
| Existing workflow suite | 62 passed, zero failures/skips, including real PostgreSQL/RabbitMQ behavior. |
| Active solution style/analyzers | Passed, no changes requested. |
| CSharpier, including generated source | Passed; 646 files checked. |
| Documentation and whitespace | All 253 local links in 7 changed/new Markdown files resolve; Git whitespace checks pass. |
| Frozen archive | All 800 original files match the archive manifest. |
| Independent review preservation | Before the transfer, all 27 pending D1/planning files in the original worktree remained byte-for-byte unchanged. Both indexes were unchanged, with no staged changes. |

All 500 tests above were executed after the final code changes. The first D1.1 run exposed
a test cleanup error after an expected server-side commit rejection; cleanup was corrected
and the full 42-case suite passed. Initial sandboxed restore could not reach NuGet audit data;
ordinary restore/build passed with network access, without disabling vulnerability checks.
No browser/Aspire topology or template-generation suite was rerun for this consumer increment.
Earlier D1/archive counts are historical context, not proof of this relational protocol.
The tests directly invoke the finite executable journey; no OS-process-death or deployment
guarantee is claimed. Real database tests use PostgreSQL 18.6 and rootless Podman/Testcontainers.

## Verification after transfer

Fresh checks on the combined `feat/ddd-sample-patterns` working tree, after D1 commit
`041c64a`, passed on 2026-10-14:

- Whole active solution build: zero warnings/errors.
- D1.1: 42 passed; D1: 22 passed; HTTP: 105 passed; architecture: 83 passed.
  All 252 tests passed with zero failures/skips.
- CSharpier: 57 relevant files checked, including generated source. All 266 local links
  in changed/new Markdown resolve; Git whitespace checks pass.
- Both CI test steps, solution entries, architecture references and Sales test friend
  assemblies are retained. Committed D1 files outside the explicit merge paths are unchanged.
- Both indexes are unchanged, all destination changes remain unstaged, and the source
  worktree is clean. The exact pre-transfer contents are backed up under
  `/tmp/d1-1-transfer-pirwopwq`.

The remaining suites in the earlier table were not rerun after the transfer; their results
describe the independent D1.1 worktree.

## Review-worthy files

- [Business Contracts](../../samples/Wholesale/modules/Sales/Sales.Contracts/IDraftOrderCommands.cs)
  and [read shapes](../../samples/Wholesale/modules/Sales/Sales.Contracts/IDraftOrderQueries.cs):
  proposal/failure meanings, child operations and reference identities.
- [Domain root](../../samples/Wholesale/modules/Sales/Sales/DraftOrders/DraftOrderAggregate.cs)
  and [Money value](../../samples/Wholesale/modules/Sales/Sales/Money.cs):
  complete-candidate invariants and child identity preservation.
- [Command adapter](../../samples/Wholesale/modules/Sales/Sales/DraftOrders/DraftOrderCommands.cs),
  [native mapping](../../samples/Wholesale/modules/Sales/Sales/DraftOrders/DraftOrderMapping.cs)
  and the original tracked-save guard (removed in D2.1c):
  full loading, root fencing, explicit deletion and supported write boundary.
- [Forward migration](../../samples/Wholesale/modules/Sales/Sales/Migrations/20261014171512_AddDraftOrderChildren.cs)
  and updated snapshot: only two new tables and native relationships/constraints.
- [Domain proofs](../../samples/Wholesale/RichAggregateDemo.Tests/DomainTests.cs),
  [consumer proofs](../../samples/Wholesale/RichAggregateDemo.Tests/OrderTests.cs) and
  [fault/concurrency proofs](../../samples/Wholesale/RichAggregateDemo.Tests/OrderTests.Persistence.cs).

D1.1 was owner-approved and checkpointed as `f55d803`. The earlier transfer preserved the
owner's D1 comparison and both indexes while this increment remained under review. D2.1 later
relocates the bounded Money value to Sales for reuse; the link above follows its current location.
