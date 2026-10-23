# Library, template and sample strategy review

2026-10-06. Planning and source review of HEAD `abcd370` and the existing uncommitted
E6.1 work. No runtime implementation, tests, migrations or dependencies were edited.
Nothing was deleted, staged or committed. Recommendations below remain proposals.

Subsequent checkpoints: E6.1 was preserved as `1ae13d4`, and the recommended T1 rehearsal
was completed and owner-approved as `8ccf4c8`. The body below preserves the review's original
observations and recommendation sequence; its descriptions of uncommitted E6 or absent
template output are historical. Use [the current plan](../plans/library-extraction.md) and
[ES1 brief](../plans/es1-bounded-event-append.md) for the next task. This note adds no test execution.

## Outcome

The foundation is useful. Keep independent ActorIdentity, Tenancy, their optional HTTP
adapters, and EF ownership. Reorder delivery so a small generated consumer is exercised
before adding more sample capabilities. Hold event expansion until its intended consumer
experience is explicit. Do not automatically proceed to E6.2 repair or E7 messaging.

The owner clarified that event sourcing is optional and Marten can be a reference for a
simpler capability. This does not select Marten as a runtime dependency, approve a new EF
interface, or authorize deleting committed work. The owner explicitly requested assessing
committed event removal with **no deletion yet**. All existing event files remain intact.

No new reusable mechanism was proven by this review. The new evidence is regression
verification of existing mechanisms, source findings, and a proposed delivery sequence.

## Context and finished slices

The archive records a working, much larger Wholesale application. Its repair, messaging,
workflow and failure scenarios are useful test inputs, but its scope is not the new product
backlog. Its source remains frozen; the manifest freshly verified all 800 original files.

| Area | Established work | Assessment |
| --- | --- | --- |
| E1 ActorIdentity and Tenancy | Separate package-free identities, immutable scoped establishment, explicit missing/anonymous/tenantless cases | Keep. Separate attribution, isolation and authority remain a good choice. Small duplicated holders preserve independent adoption; a common holder abstraction adds little leverage. |
| E2 EF ownership | Explicit model filter, write checks and stored-owner concurrency predicate; native schemas, migrations and same-tenant relationships | Keep. This is meaningful depth: deleting the module redistributes isolation rules across consumers. Its supported mapping limits and privileged bypass obligations must remain visible. |
| E3 HTTP adapters | Effective native principal establishment and independent tenant admission; consumer resolvers and failure presentation | Keep. The actor adapter's two integration points solve a demonstrated ordering problem. Tenancy remains unavailable to earlier native authorization handlers in this composition. |
| E3 sample integration | Persisted Access registry, module Contracts, state-stored Inventory reads, Sales profile mutation, native runtime/telemetry and local OIDC/browser journeys | Useful template evidence. Access policy, native configuration and business Contracts stay editable consumer code. This is more than two completed slices, but not a generated template. |
| E4 codec, `2a49ef3b` | Durable name/version dispatch, duplicate registration rejection, immutable JSON settings and decoding failures | Keep provisionally. It earns depth independently of the custom event store. Package promotion still depends on a selected adopter. |
| E5.1 history, `4cc12a1` | Selected ordered-range integrity and typed failure classification | Keep provisionally. Small size is not evidence of shallowness. Timestamp monotonicity is a documented supported protocol, not an automatic property of every event store. |
| E5.2.1–E5.3, `4f4d5b2` / `abcd370` | Native captured reads, append proofs and an extracted stream/envelope mapping utility | Useful experiments. The mapping module is a real utility, but it is GUID/JSON storage registration, not an end-to-end event-sourcing capability. |
| E6.1, uncommitted | Inline decision state, an independent summary, required-view consistency and rollback | Existing report records substantial earlier proofs. No library mechanism was extracted; broader adoption remains unresolved. Do not extend this experiment automatically. |
| Template population | Recipes in sample code; implementation deferred to E10 | Largest delivery gap relative to the stated product. No generated second repository currently proves omission, naming or independence. |

Prior reports contain historical counts and superseded limitations. For example, E3.1's
provider gaps were subsequently exercised in bounded E3.7 journeys. Treat each report as
dated slice evidence; use the current plan entry point to determine the next task.

## Why the plan is drifting

Its ownership principles remain relevant, but its sequence recreates much of the archived
application before testing repository population. E6–E9 are candidate capabilities; they
are not prerequisites for generating a useful state-stored modular application.

The extraction gate is also easy to misread in two opposite ways. Finding no new library
mechanism can be an honest sample/template outcome. Repeating that outcome should still
trigger a product-scope check. Conversely, native EF providing transactions does not prove
that coordinated append and loading have no reusable complexity. The question is how many
technical rules remain in every adopter after using the library.

There is concrete context pollution beyond conversation length. The plan's E5.2.2 section
still called committed work unstaged; this review corrects it. README, design, master plan,
slice plans and reports repeat status across many files. The current plan now starts with
the reassessment so a new session does not infer that the largest unfinished capability is
the next authorized task. A future documentation cleanup can shorten navigation without
rewriting historical proof reports.

## Architecture findings

### Make optional event sourcing removable from generated composition

[Inventory's project](../../samples/Wholesale/modules/Inventory/Inventory/Inventory.csproj)
references all three event libraries. Its [DbContext](../../samples/Wholesale/modules/Inventory/Inventory/InventoryDbContext.cs)
always configures event storage, and the state-stored HTTP host references that Inventory
implementation. State-stored operations still function, but their project graph and
migrations carry event capability. This is not proof of template-time omission.

A generated consumer should be a separate adoption proof with an event-free module
implementation and native persistence adapter. Keep the existing sample together for now;
do not split every module preemptively. This improves locality of composition decisions
and gives the template interface leverage through tested omission. It respects ADR 0004's
separation of business Contracts and native host composition.

### Settle the event capability before adding more projection machinery

The two [command](../../samples/Wholesale/modules/Inventory/Inventory/StockPositions/StockPositionCommands.cs)
[implementations](https://github.com/oxface/modulith-foundry/blob/ae2909cd2e8f46a8b4dc0da71cc1c4a3ab6d52b4/samples/Wholesale/modules/Purchasing/Purchasing/PurchaseOrders/PurchaseOrderCommands.cs)
repeat transaction preconditions, observed-version checks, timestamps, envelope positions,
header advancement and detached preparation. The two history readers likewise repeat
captured-head and temporal-prefix coordination. The technical protocol largely remains
consumer code despite the three event library calls.

The deletion test supports concentrating some of that knowledge in a deeper module if a
Foundry-owned implementation is selected. It does not yet select a public interface or
justify a generic repository, projector registry or adapter for every provider. Append
and captured reading can remain separate reviewable capabilities. Keep domain decisions,
reducers, tenant admission and final save/commit ownership explicit at their seams.

The next comparison must include one meaningful state-dependent decision. Current
[receipt decisions](../../samples/Wholesale/modules/Inventory/Inventory/StockPositions/StockPositionDecisions.cs)
and [line decisions](https://github.com/oxface/modulith-foundry/blob/ae2909cd2e8f46a8b4dc0da71cc1c4a3ab6d52b4/samples/Wholesale/modules/Purchasing/Purchasing/PurchaseOrders/PurchaseOrderDecisions.cs)
take requests alone; commands select events before loading existing state. State contributes
to evolution and arithmetic checks, which is legitimate, but provides limited evidence for
the broader decider/aggregate loading interface. A second consumer should vary an actual
obligation, not just the domain type names.

No correctness defect was established in the inspected inline protocol. The report already
acknowledges missing backfill/repair and changed damaged-history assurance. The concern is
extraction value and scope, not a claim that the existing 111-case database report is invalid.

### Preserve depth where it already exists

The codec and history validator concentrate actual dispatch/integrity behavior. Removing
them would redistribute those rules. The EF model utility also removes repeated mapping
and rejects unsupported identity configurations; its tenant-free executable supplies useful
adoption evidence. Their existence does not require continuing the complete custom store.

Reject an immediate generic fold, common context holder or projection registry: none of
those demonstrated abstractions currently removes enough caller obligations. Prefer a
deeper supported capability over a growing list of shallow interfaces. Multiple callers
prove repetition; two meaningfully different adapters are stronger evidence for a seam.

## Event removal options

| Option | Scope | Recommendation and consequence |
| --- | --- | --- |
| Hold existing work | Keep HEAD and all uncommitted E6.1 files while planning | Current action, per owner instruction. It preserves evidence but should not become permission to extend E6. |
| Drop E6.1 only later | Restore its runtime/test/migration edits and remove its added files after preserving them; reconcile documentation separately | Reasonable first reset if selected. The shared fixture's pooling fix is an independent test-infrastructure finding; assess it separately. Do not discard every modified document because some merely record E5's actual checkpoint. |
| Retain E4–E5 as a bounded experiment | Keep narrow mechanisms and proofs; exclude event code from the initial generated composition | Preferred provisional position. Existing work retains value without defining the baseline template. |
| Remove committed persistence/append experiment | Remove E5.2.1 and E5.2.2/E5.3 effects while retaining E4/E5.1 selectively | Viable if the owner rejects a custom store. Mixed commits touch solution, CI/hooks, architecture proofs, Inventory, Purchasing and docs; removal needs a separate exact file-level plan and fresh foundation regressions. |
| Return to a pre-event active tree | Use `dc3ac3b` as a comparison baseline for a reviewed forward change | Largest reset; loses useful codec/history mechanisms as well as storage experiments. Not recommended solely to obtain clean conversational context. No reset/revert command is authorized here. |

`abcd370` combines append, storage extraction and planning research. A blind revert would
also remove independent evidence. If removal is chosen, preserve fixtures/reports and plan
which active files change; do not move new experiments into the frozen archive. Existing
database use and migration compatibility must be established before selecting migration
deletion versus a forward schema change.

## Proposed sequence and clean sessions

1. Review this recommendation and the precise disposition of the existing E6 experiment.
2. Run [T1](../plans/t1-template-rehearsal.md) in a new session: generate one bounded
   state-stored consumer outside the Foundry tree, with tested naming and event omission.
3. If optional event sourcing remains a priority, use a separate session to define its
   supported adopter behavior and compare Foundry-owned EF mechanics with an established
   implementation. Do this before a new projection/repair implementation.
4. Resume selected event or messaging capabilities individually after their consumer need
   and extraction value are demonstrated. Add supported template choices incrementally.

Fresh sessions help, but do not resolve contradictory entry points or excessive slice scope.
Each should begin with the workflow, current plan entry point, applicable decisions and one
capability brief. Use archive material only for named failure scenarios. End with a clear
consumer outcome, interface review map, current proofs and explicit remaining limits.

## External checks

Native `dotnet new` supports custom multi-project source templates, parameter symbols and
conditional content. Evaluate it before building a file-replacement engine. A configuration
file or repository-aware updates may still justify a small CLI later; the first creation
proof should establish the need. [Microsoft template authoring](https://learn.microsoft.com/en-us/dotnet/core/tools/templates),
[template configuration reference](https://github.com/dotnet/templating/wiki/Reference-for-template.json).

Current Marten documentation includes EF Core projection integration and inline processing
in the committing transaction. Thus using EF for application persistence alone does not
rule out an established event implementation. Its lifecycle, schema and transaction
ownership still need comparison with Foundry's reviewed explicit-control direction. No
Marten dependency, compatibility claim or production evaluation was added.
[EF Core projections](https://martendb.io/events/projections/efcore.html),
[inline projections](https://martendb.io/events/projections/inline).

## Fresh verification and review map

The existing 40-project solution built with `--no-restore -m:1 --disable-build-servers`:
zero warnings/errors. All ten container-free suites passed after granting the local IPC
access needed by the native test launcher; none failed or skipped.

| Suite | Passed |
| --- | ---: |
| ActorIdentity / Tenancy | 19 / 17 |
| Actor HTTP / tenancy HTTP | 15 / 39 |
| Context consumer / EF model checks | 15 / 23 |
| Codec / history / codec consumer | 16 / 16 / 16 |
| Architecture | 65 |
| Total | 241 |

These checks exercise the current tree, including the pre-existing E6 files. They do not
certify a hypothetical rollback or newly generated repository. PostgreSQL, browser, Aspire,
broker and archived runtime suites were not rerun; their reports remain earlier evidence.
No new tests were authored. No reusable mechanism was newly established.

Review the proposed next session, the current-plan entry point, and the concrete command/
project/DbContext evidence above. Library implementations and business Contracts are
unchanged. Remaining decisions are event ownership and removal scope, the exact generated
baseline, distribution of selected libraries, and supported creation/update behavior.
