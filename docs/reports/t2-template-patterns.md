# T2: template pattern adoption

Date: 2026-10-22. Topic branch `feat/v1-template-patterns`, fixed P1 base `4bed3eb`.
The [bounded scope](../plans/t2-template-patterns.md) adds working generated Catalog
commands/queries and one explicitly selected separate report-worker composition.

## Outcome

The default generated consumer now owns immutable Catalog candidates, an explicit staged
command Contract, native version concurrency and module-local Queries/Filters. Consumer
transactions/save/commit stay visible. A forward migration adds price/version while the
initial migration remains intact. Default startup still performs no initialization.

Optional boolean `catalogWorker` adds a separate native reporting process; omission/false
leaves its files, solution entry, Hosting dependency and hosted registration out of the
generated application. Both compositions remain independently buildable without messaging,
event sourcing or a Foundry runtime. The generated README documents operational trust,
failure and consistency limits beside the actual source.

## Verification

Fresh execution uses Node 24.21.0, .NET SDK 10.0.401, EF 10.0.12/Npgsql 10.0.3 and a
disposable PostgreSQL 18.6 server. The real generator proof creates output in `/tmp` outside
the checkout, beneath a conflicting SDK 9 pin. No personal credentials or existing business
database is used. Verification command:

```sh
npm ci --prefix tools/template --ignore-scripts
npm --prefix tools/template run check
CATALOG_TEST_ADMIN_CONNECTION_STRING='your disposable PostgreSQL admin connection' \
DOTNET_PROCESSOR_COUNT=4 npm --prefix tools/template run verify
```

The complete final execution passed: Cedar **7**, HarborDesk (`Task`) **7**, CedarWorker
**9** adoption cases (**23 total**), all with zero failed/skipped. Each external solution
restored/built with zero warnings/errors and passed native style/analyzer verification.
This final execution includes documentation conditionals, candidate mutation atomicity and
explicit-false omission refinement; an earlier full 7/7/9 execution passed separately.

| Other fresh checks | Result |
| --- | --- |
| TypeScript types and Prettier | Passed through `npm --prefix tools/template run check`. |
| Template C#/XML formatting | CSharpier passed for all 31 source/project files, including generated migration source. |
| Local documentation links | All 18 local file links across the T2 plan/report and tool guide resolve. |
| Creator integrity | Default/explicit false equivalence, selected worker inclusion, input/unsupported choices, repeat, failure and race proofs passed. |
| Cleanup and Git | Generated temporary consumers/databases removed; disposable PostgreSQL container stopped/removed. Initially empty index preserved until authorized staging; changes restricted to the owned template/tool/docs paths. |

The generated database cases prove independent expected outcomes rather than source-shape
equivalence: tied-price IDs across one-item pages/count 2; another tenant and cheap item
excluded; invalid price/precision leaves no accepted mutation; another scope sees old state
before commit; rollback preserves it; competing expected-version-1 edits produce one
version-2 winner and native concurrency failure for the loser. The earlier ownership/context
journey remains active, including forged stored-owner updates and empty migration startup.

The selected worker tests start actual child processes. Once mode reads Beta's two items;
periodic mode reports Alpha's one item twice, proving fresh immutable operation establishment,
then accepts SIGTERM and exits successfully within the test's 30-second overall budget.
Missing tenant mandate exits 2; database unavailability exits 1 without a successful report.
This is a specific native host proof, not a universal shutdown/availability guarantee.

Creator inspection/proofs retain namespace/name substitution, source snapshot hashes, only
local project references, deterministic identical-input trees, strict invalid/duplicate
configuration and unsupported combination rejection, occupied/repeated destination refusal,
early/partial native failure cleanup and atomic no-replace publication under races. Explicit
false selection must equal default output except `foundry.json`. Default and restored graph
inspection reject worker/Hosting activation; all outputs reject event/transport dependencies.

## Review-worthy files

| Files | Concern |
| --- | --- |
| [Creator](https://github.com/oxface/modulith-foundry/blob/5d86b20df5b0eb82f1aa96b889229ce6f5ca351f/tools/template/create.ts), [proof](https://github.com/oxface/modulith-foundry/blob/5d86b20df5b0eb82f1aa96b889229ce6f5ca351f/tools/template/verify.ts), [tool guide](https://github.com/oxface/modulith-foundry/blob/5d86b20df5b0eb82f1aa96b889229ce6f5ca351f/tools/template/README.md) | Strict bounded boolean option, native conditional generation, omission and independent generated-consumer evidence. |
| [Command Contract](https://github.com/oxface/modulith-foundry/blob/5d86b20df5b0eb82f1aa96b889229ce6f5ca351f/templates/state-stored/modules/Catalog/FoundryApplication.Catalog.Contracts/ICatalogCommands.cs), [candidate](https://github.com/oxface/modulith-foundry/blob/5d86b20df5b0eb82f1aa96b889229ce6f5ca351f/templates/state-stored/modules/Catalog/FoundryApplication.Catalog/CatalogItemState.cs), [commands](https://github.com/oxface/modulith-foundry/blob/5d86b20df5b0eb82f1aa96b889229ce6f5ca351f/templates/state-stored/modules/Catalog/FoundryApplication.Catalog/CatalogCommands.cs) | Consumer invariants, proposal meaning, context/transaction requirement and native version checks. |
| [Queries](https://github.com/oxface/modulith-foundry/blob/5d86b20df5b0eb82f1aa96b889229ce6f5ca351f/templates/state-stored/modules/Catalog/FoundryApplication.Catalog/CatalogQueries.cs), [filters](https://github.com/oxface/modulith-foundry/blob/5d86b20df5b0eb82f1aa96b889229ce6f5ca351f/templates/state-stored/modules/Catalog/FoundryApplication.Catalog/CatalogFilters.cs) | Predicates before matching count/order/page; immutable untracked results and explicit consistency limits. |
| [Migration](https://github.com/oxface/modulith-foundry/blob/5d86b20df5b0eb82f1aa96b889229ce6f5ca351f/templates/state-stored/modules/Catalog/FoundryApplication.Catalog/Migrations/20261022180000_AddCatalogPrices.cs), [context](https://github.com/oxface/modulith-foundry/blob/5d86b20df5b0eb82f1aa96b889229ce6f5ca351f/templates/state-stored/modules/Catalog/FoundryApplication.Catalog/CatalogDbContext.cs) | Forward native price/version storage and independent ownership/version concurrency policies. |
| [Worker](https://github.com/oxface/modulith-foundry/blob/5d86b20df5b0eb82f1aa96b889229ce6f5ca351f/templates/state-stored/src/FoundryApplication.CatalogWorker/CatalogReportWorker.cs), [entry point](https://github.com/oxface/modulith-foundry/blob/5d86b20df5b0eb82f1aa96b889229ce6f5ca351f/templates/state-stored/src/FoundryApplication.CatalogWorker/Program.cs), [generated README](https://github.com/oxface/modulith-foundry/blob/5d86b20df5b0eb82f1aa96b889229ce6f5ca351f/templates/state-stored/README.md) | Separate hosting, fresh operation scopes, explicitly trusted deployment mandate, once/periodic behavior and native failure/shutdown outcomes. |
| [Pattern tests](https://github.com/oxface/modulith-foundry/blob/5d86b20df5b0eb82f1aa96b889229ce6f5ca351f/templates/state-stored/tests/FoundryApplication.Adoption.Tests/CatalogPatternTests.cs), [worker tests](https://github.com/oxface/modulith-foundry/blob/5d86b20df5b0eb82f1aa96b889229ce6f5ca351f/templates/state-stored/tests/FoundryApplication.Adoption.Tests/WorkerJourneyTests.cs) | Actual external PostgreSQL command/concurrency and independent process behavior. |

## Findings and remaining gaps

**No new reusable Gloaming mechanism was proven or added.** Shared composition is a
small editable IServiceCollection helper; query filters/decisions/price rules, tenant/system
mandate and report presentation are consumer policy. Native libraries remove the already
proven immutable-context/ownership work. There is no new library interface, pipeline,
generic handler, repository or worker framework.

**Template:** new proof is actual generated DDD/CQRS adoption and explicit worker selection/
omission. **Sample/product:** Catalog stays a small independent consumer and does not replace
the coherent Wholesale business sample. No archive or Wholesale source moves, root solution,
CI or global README changes accompany this slice.

Remaining gaps: no HTTP production ingress/admission, durable work/restart cursor/broker,
cross-module snapshot, SQL plan/capacity proof, generalized provider/transport/event presets,
online projection cutover, generated-repository updates or package upgrade/distribution
protocol. The selected report worker is intentionally not evidence for those capabilities.
T1 dated test results remain historical; the counts above are fresh T2 generated executions.
