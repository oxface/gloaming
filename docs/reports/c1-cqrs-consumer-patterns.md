# C1: CQRS consumer patterns

Owner-approved, 2026-10-15, including the extraction assessment below, on `feat/cqrs-consumer-patterns` in
`/tmp/modulith-foundry-c1`, from freshly fetched `origin/main` at `e400e73`.
The [scope and concrete usage](../plans/c1-cqrs-consumer-patterns.md) were presented
before implementation. At the initial handoff all changes were unstaged; the owner
has since staged a review snapshot. The extraction follow-up below changes only
the working tree and preserves that snapshot. No commit or push was made.

## Outcome and evidence

The [executable](../../samples/Wholesale/CqrsDemo/README.md) composes real Sales
commands and queries with Inventory Contract reads using native typed scoped DI.
It observes availability 3 (on-hand 10 minus reserved 7), creates drafts, changes
one from total 40 to 45, rejects total 105 against limit 100, and reads a filtered
page from committed Sales state. Filtering excludes the cheap draft before paging.
Availability is advisory and causes no stock reservation or shared module transaction.

Sales' `DraftOrderReadView` joins draft/customer on the complete ownership key and
derives totals/counts from Sales-owned relational children. It is a query-time
shape rather than a mapped table or persisted projection. Existing single-summary
reads reuse it. The internal `DraftOrderFilters` methods compose native
`IQueryable` predicates for current customer name, exact currency and minimum total.
`SearchAsync(SearchDraftOrders)` applies all predicates before a matching count,
deterministic total/ID sorting and offset/limit, then materializes Contract DTOs.
The page limit is 1–100; beyond-end pages still carry the filtered count.

New PostgreSQL assertions independently expect totals 5/40/40/50/60/80, two children
for the first total-40 draft, exactly three Alpha/EUR matches at minimum 30, IDs
20/30/40 across one-row pages and an empty beyond-end page with count 3. Other
local customer/currency rows and a Beta draft with the same ID/code demonstrate
that filters, tenant isolation and correlated child totals do not bleed together.
Renaming the current customer changes joined matches without rewriting the draft;
`%_` is matched literally, and lowercase text/currency do not silently match.

Queries remain untracked database reads. An unsaved tracked proposal is absent;
another operation still sees the old values after save and before commit. Caller
commit exposes the new version/total; rollback preserves the old read view.
Rejected over-limit and stale-version commands leave the accepted data unchanged.
Selected-tenant requirements, input guards and cancellation remain explicit.

Count and page are two statements using the same predicates. A controlled commit
between statements demonstrates count 3/page 4 under native read committed.
The same scenario in a caller-owned repeatable-read transaction returns the
original three matches; a fresh operation then sees all four. Query code opens
no transaction. Deterministic ordering does not freeze separate offset-page
requests, and separate module reads do not imply a common snapshot.

Inventory's existing `StockPositionQueries` / `StockPositionFilters` are adopted
unchanged through `IStockPositionQueries`. New SQL observation verifies the inline
state/header join, reserved-quantity filter, tenant isolation and absence of
stored-event history reads. The consumer sees availability 3 at threshold 3 and
no match at threshold 4. Sales' SKU validation still uses `IStockCatalog` through
Inventory Contracts; no cross-module persistence join or `IQueryable` export appears.

## Fresh verification

Executed against this working tree on 2026-10-15 with .NET SDK 10.0.401 and
PostgreSQL 18.6 Testcontainers on the rootless Podman socket; resource reaping
remained enabled. Commands and required environment are in the
[consumer README](../../samples/Wholesale/CqrsDemo/README.md) and
[development guide](../development.md#cqrs-consumer-patterns).

| Check | Fresh result |
| --- | --- |
| Active solution restore/build | Passed; 84 projects, zero build warnings/errors |
| New `CqrsDemo.Tests` suite | 20 passed, zero failed/skipped; includes executable journey, PostgreSQL semantics and pre-SQL guards |
| Existing `RichAggregateDemo.Tests` | 40 passed, zero failed/skipped; native root concurrency, required writes, commit failure/rollback and domain decisions |
| Existing `DomainEventsDemo.Tests` | 30 passed, zero failed/skipped; current EventReactions-backed Sales behavior |
| Architecture suite | 90 passed, zero failed/skipped; C1 host added to existing dependency checks |
| Native style/analyzer verification | Both passed for the active solution |
| CSharpier | Passed; 750 active files checked, generated files included |
| Frozen archive verification | All 800 original files match the retained manifest |
| Documentation / CI syntax | 373 local links checked across seven changed documents; CI YAML parses |
| Whitespace / Git preservation | `git diff --check` passed; original worktree clean and index SHA-256 unchanged; C1 staged entries unchanged, all edits unstaged |

The regression rows are newly executed existing proofs, not newly introduced
mechanisms. Earlier D1/D2/E5/ES2 report counts remain historical evidence. Current
local reactions are Gloaming.EventReactions; superseded D2 marker/base proposals
were not brought forward. C1's executable test invokes its journey against a
disposable database in-process; it does not claim a new HTTP or deployed-host proof.

## Review-worthy files

| Files | Review concern |
| --- | --- |
| [Sales query Contracts](../../samples/Wholesale/modules/Sales/Sales.Contracts/IDraftOrderQueries.cs) | Concrete search request/page and documented sort/snapshot obligations; no new Gloaming interface |
| [DraftOrderQueries](../../samples/Wholesale/modules/Sales/Sales/DraftOrders/DraftOrderQueries.cs), [DraftOrderReadView](../../samples/Wholesale/modules/Sales/Sales/DraftOrders/DraftOrderReadView.cs), [DraftOrderFilters](../../samples/Wholesale/modules/Sales/Sales/DraftOrders/DraftOrderFilters.cs) | Complete-key joins, correlated children, predicate reuse, validation, deterministic pagination and untracked materialization |
| [CqrsJourney](../../samples/Wholesale/CqrsDemo/CqrsJourney.cs), [composition](../../samples/Wholesale/CqrsDemo/CqrsComposition.cs), [finite data](../../samples/Wholesale/CqrsDemo/CqrsDemoData.cs) | Concrete typed usage, separate operation scopes and explicit independent module transactions |
| [QueryTests](../../samples/Wholesale/CqrsDemo.Tests/QueryTests.cs), [transaction cases](../../samples/Wholesale/CqrsDemo.Tests/QueryTests.Transactions.cs), [SQL probe](../../samples/Wholesale/CqrsDemo.Tests/QueryProbe.cs) | Independent expected values, controlled concurrent commit, tenant collision and ownership evidence |
| [Wholesale CI](../../.github/workflows/ci.yml), [solution](../../ModulithFoundry.slnx), [module-boundary tests](../../tests/ArchitectureTests/SampleModuleBoundaryTests.cs) | Suite discovery and module/Contracts/library independence from the new host |

The Sales project grants internal visibility to the proof assembly for finite
additional customer setup. Product assertions run through module Contracts.
Library source, dependencies, migrations and frozen archive are unchanged.

## Extraction, consumer policy and remaining template work

**No new reusable mechanism was proven.** Native EF query composition and scoped
typed DI supply this capability. The one duplicated Sales join/projection is now
shared internally between point-summary and search reads. This does not justify
a cross-module helper: Inventory filters JSONB/validates inline metadata, whereas
Sales joins customer/children and derives relational totals. No new or changed
Gloaming interface needs approval in this slice; library extraction would require
a separate concrete proposal and owner interface review.

Search fields, string/currency semantics, totals, sort order, page-size policy,
domain candidate acceptance and tenant admission remain consumer-owned. Native
transactions, concurrency predicates and context lifetimes retain their existing
ownership. No mediator, handler registry, pipeline, generic repository, generic
paging/filter utility or persisted projection engine was selected. This bounded
result does not reject such mechanisms for an independently justified future need.

Template candidates are editable typed registrations, immutable operation scopes,
explicit save/commit and repeatable-read examples, and module-local Queries/Filters.
The generated T1 Catalog composition remains unchanged. Selecting/configuring
these richer patterns in T2, HTTP binding/permission policy, alternate sorts/cursor
paging, query-plan/index work and larger provider/performance claims remain gaps.
The page cap does not bound offset scan or aggregate-query cost. Cross-module
atomic reads/writes and stock-reservation semantics require separate requirements
and proofs; advisory reads confer neither guarantee.

## Extraction follow-up: existing execution patterns

Owner clarification, 2026-10-15: Gloaming may extract simple common mechanics that
consumers would otherwise implement themselves. Complexity is not a prerequisite.
Adoption, customization, opt-out and eventual NuGet release/update/support costs
must be justified by the repeated work removed. An execution pipeline would be a
general composition mechanism, not inherently a CQRS or message-bus capability.
Transaction/save behaviors were illustrative possibilities, not selected requirements.

The source assessment starts from the current consumers:

| Current evidence | What a shared wrapper could remove | Present finding |
| --- | --- | --- |
| [DraftOrderCommands](../../samples/Wholesale/modules/Sales/Sales/DraftOrders/DraftOrderCommands.cs) calls its local `RequireWriteScope` from create/edit/submit | Repeated selected-tenant, human-actor and existing-transaction checks | The shared block is already local to the handler. Tenant/identified-actor presence helpers already exist in their owning libraries; human eligibility and admission remain consumer choices. General behavior composition has not yet removed repeated setup here. |
| [CustomerProfiles.ChangeAsync](../../samples/Wholesale/modules/Sales/Sales/CustomerProfiles.cs) saves twice inside its native transaction; draft commands stage without saving | Transaction/save/rollback syntax | These consumers have different execution contracts. Automatic wrapping can be explicitly opted into, but moving these calls requires preserving intermediate saves, result meaning and failure handling. This is possible future behavior, not demonstrated value in C1. |
| [Reservation orders](../../samples/Wholesale/modules/Sales/Sales/ReservationOrders/StockReservationOrders.cs) start/retry and [stock-issue deadlines](../../samples/Wholesale/modules/Sales/Sales/StockIssues/StockIssueDeadlines.cs) repeat begin/stage/save/commit | A small delegate-based transaction wrapper | Concrete repeated syntax exists. An ordinary local helper is the first comparison; introducing handler adapters and a general behavior registry has no demonstrated additional payoff. Native transaction ownership is still part of these current Contracts. |
| [Sales outcome intake](../../samples/Wholesale/modules/Sales/Sales/ReservationOrders/StockReservationOutcomeIntake.cs) and [Inventory command intake](../../samples/Wholesale/modules/Inventory/Inventory/Reservations/StockReservationIntake.cs) map/validate a message, create a fresh scope, receive in a native transaction and commit | The nearly identical scope/context/transaction/receipt block | The strongest narrow duplication found. Mapping, subscription, producer and admission are consumer-owned. Any later utility belongs for assessment with existing Messaging intake, where it must simplify these two callers without general pipeline machinery; it does not automatically warrant another NuGet package. |
| [PostgresInboxProcessor](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/PostgresInboxProcessor.cs) already wraps handler execution with a lock, save, completion and commit | Reliable processing orchestration and attempt diagnostics | An existing library already owns and proves this specific execution protocol. An outer generic save/transaction behavior could duplicate or interfere with that ownership. Inbox intake itself uses native SQL and needs no EF save. |
| [EF capability composition](../../src/Gloaming.EntityFrameworkCore/README.md) already sequences selected preparation, change detection and validation | Repeated save-lifecycle integration across feature families | Existing reusable composition is at the native save seam. It is not evidence that the application needs a second general handler pipeline. |
| Sales/Inventory methods use request-specific guards and domain/result mapping; C1 queries use native projections | Common validation or diagnostics behavior | The inspected module methods do not currently repeat a common validator invocation or handler-duration wrapper. Such behaviors remain examples until an actual consumer needs them. Domain validation and query policy are not extracted merely to populate a pipeline. |

This assessment therefore selects no general pipeline interface or implementation.
The reason is the absence of demonstrated net savings for a shared execution
protocol in these consumers, rather than the simplicity of the possible mechanism.
The existing named-method Contracts also mean generic handler composition would
need explicit delegate wrapping or an adoption of a common handler interface;
that cost must be included in the consumer comparison.

A future small behavior can earn extraction without a larger framework. Compare
its real call sites before/after with an ordinary local helper or decorator,
including registration, ordering, result/failure meaning and removal of the
optional dependency. An explicitly selected transaction/save behavior is valid
to assess if consumers actually want to delegate that responsibility. It would
need its own native transaction/failure proofs; handler completion alone does
not establish commit success. Business acceptance, audit meaning and external
effects must retain their explicit consumer decisions.

No source, interface, dependency, package or template changes accompany this
follow-up. No new reusable mechanism was proven. It is a source assessment and
documentation update; the test results above belong to the earlier C1 execution
and were not rerun for this follow-up. The current staged review snapshot is
preserved, and this report amendment remains unstaged.

The owner approved this outcome and assessment on 2026-10-15. Continue bounded
slices and extract mechanisms supported by the available evidence; a slice may
finish as consumer patterns without a new library. Bootstrap and usage by the
incoming real project can expose further needs and test the usefulness of existing
choices. This approval selects no general pipeline or additional package and does
not change the instruction to leave the work uncommitted.
