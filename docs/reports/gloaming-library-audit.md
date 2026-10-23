# Gloaming library architecture audit

Date: 2026-10-22. Inspected source: `1582e77849088e243881c82a37c0a42b2a11a322`.
Status: active audit and implementation record. The local package command and two thin
adapter removals are committed as `d63f567`; optional worker hosting as `ffc6ec2`.
Continuation package consolidation is committed as `56a8b12`; Messaging example/fixture
consolidation as `5d86b20`; application scaffolding retirement as `dd6eb77`. Remaining family
example consolidation is committed as `ae2909c`; Purchasing retirement as `8601204`.
Wholesale Dispatch retirement is the current uncommitted batch. Integrated cleanup and reviews
remain open, followed by the owner-requested extra document/test pass and fresh-context sweep.

Make Gloaming the repository's primary product. Keep its useful persistence and delivery
mechanisms, simplify package divisions that provide little independent value, and reduce
the application examples. The strongest changes are to combine the workflow definition and
EF runner packages, separate optional messaging worker hosting, and replace two thin
adapter packages with ordinary consumer configuration.

These recommendations revisit adoption cost under the new library-first scope. Earlier
v1 reviews assessed the previous integrated deliverable; this assessment does not establish
a regression in its tested guarantees.

## Implementation progress

All current work is consolidated in `/home/oxface/repos/modulith-foundry`, on
`docs/gloaming-library-audit`, at the owner's request. Continue subsequent audit increments
in this checkout. The earlier isolated worktrees remain unchanged as recovery copies;
there is no need to switch to them to review or use the combined result.

- [Local package development](local-package-development.md): the everyday `pack` command,
  source-mapping guidance and full package-adoption verifier are present here.
- [Thin adapter retirement](thin-adapter-retirement.md): PostgreSQL audit JSONB mapping and
  messaging source/meter subscriptions now use native consumer configuration. The current
  checkpoint inventory was 21 libraries; the package table records the original 23-package audit.
- The shared verifier combines both increments: it invokes the real pack command, expects
  21 packages and adopts the retained audit EF package directly.

The two earlier passes' results remain dated evidence in their reports. Fresh verification
from this consolidated checkout passed all 112 architecture tests, release-tool TypeScript/
formatting checks, all 21 archive checks and all four fresh-cache package-only consumers
(core: two Gloaming packages; events: two; PostgreSQL: seven; all-packages: 21). The full
proof invoked the real `pack` command. All 706 checked local file links and active tracked
project references resolve. The original C# and project changes match the tested isolated
pass byte for byte; its 55 audit/telemetry runtime cases were not rerun for this transfer.

Combined proof version: `1.0.0-local.1792705144078.gdce65da5`. Local evidence:
`/home/oxface/.cache/tmp/gloaming-package-proof-tkyLuy/manifest.json`, SHA-256
`34701eb77bfe1ab6ee7f10e671cb7799e411a4f86b53decae4e2e93a3b986ee2`.
Logs: `/tmp/gloaming-audit-consolidation-{architecture,packages}.log`. The disposable
PostgreSQL server was stopped and auto-removed. No new reusable runtime mechanism is
introduced by combining these changes. The owner subsequently approved that snapshot,
committed as `d63f567` after all commit hooks passed; it has not been pushed.

The next [worker-hosting slice](messaging-worker-hosting.md) adds the explicitly selected
hosting package, bringing that checkpoint inventory to 22. It is committed as `ffc6ec2`;
333 fresh test cases and all 22 packages/five independent package consumers pass. Persistence
consumers omit Hosting abstractions; selecting the hosting package runs the existing workers.
Its detailed results and remaining demo dependency cleanup are recorded separately. The staged
review snapshot remains empty.

The [continuation consolidation](continuation-package-consolidation.md) now brings the
inventory to 21 by retaining definitions and execution in the existing EF package. This
slice is committed as `56a8b12`. Its 176 selected test cases, 21 archives and
six package-only consumers pass, including real continuation execution from the combined
archive. Its evidence and EF-dependency trade-off are recorded separately. The estimated
remaining cleanup is tracked as [larger code-led batches](#remaining-cleanup-slices), excluding Umbrine.

## Messaging example and fixture consolidation

Committed as `5d86b20`. One [delivery example](../../src/Gloaming.Messaging/samples/Delivery/README.md)
replaces the separately advertised outbox, inbox, broker-journey and producer/worker examples.
Exports and Rendering are ordinary class libraries with separate contexts/migrations and no
peer-module, host, RabbitMQ or worker-hosting dependency. Producer uses only Exports and
native telemetry setup. Worker owns explicit polling, StopHost, broker routing and intake.
The existing Rendering registrations now live in `RenderServices`; host supervision stays
in the two actual hosts that choose it.

[Test-owned fixtures](../../src/Gloaming.Messaging/tests/Fixtures/README.md) retain the older
HTTP/operator/retention hosts, finite broker journeys and separately compiled workflow
Earlier/Current applications. Delivery adoption, migration, operational and process tests
are one suite; shared child-process support moves to `tests/Support`. CI runs the consolidated
suite once and tracks shared support in its dependent lanes. The public sample has five
projects (two modules, two hosts, native telemetry defaults), rather than six experiment
projects. This is a clearer ownership graph, not a large reduction of retained proof code.

No behavioral test case is intentionally removed. Existing schemas, migration IDs, stored
status numbers, wire aliases and literal JSON remain. Sample CLR namespaces/assemblies are
renamed; this is permitted by the no-external-consumer decision. The producer architecture
proof now checks its complete runtime graph for accidental worker/broker dependencies,
addressing the former indirect dependency through the outbox executable.

Fresh verification from this checkout:

| Check | Result |
| --- | --- |
| Consolidated delivery suite | 96 passed, including migration, broker, process recovery, retention and real OTLP export. |
| Separately compiled workflow deployments | 7 passed. |
| Architecture | 113 passed, including full producer runtime dependency omission. |
| Wholesale WorkflowRecoveryTests | 7 passed using the relocated shared process harness. |
| Test-source/fixture comparison | All 69 original Fact/Theory definitions remain; all four literal JSON files are byte-identical. |
| Full solution | Builds with zero warnings/errors; formatting, style and analyzers checked. |
| Local packages | All 21 archives and six isolated package-only consumers passed; packaged README targets resolve. |
| References and review state | All checked local Markdown links and active project/source references resolve; generator-substituted template paths are intentionally excluded. Existing index remains unchanged. |

Package evidence: `/home/oxface/.cache/tmp/gloaming-package-proof-moDWY3/manifest.json`,
version `1.0.0-local.1792709311578.gaed653bc`, SHA-256
`de47ed63563ff37ec039cf37bd03f836c96e3ce9c8e9c98af75ffc9804e80e6e`.
Logs: `/tmp/gloaming-delivery-{tests,workflow-tests,architecture-tests,wholesale-tests,packages}.log`.
The disposable package-proof PostgreSQL server was stopped and auto-removed. Earlier reports
retain their historical counts; the family-only runtime suites were not rerun for this
sample relocation because their implementation and contracts are unchanged.

Review the [module registration](../../src/Gloaming.Messaging/samples/Delivery/Rendering/RenderServices.cs),
[worker composition](../../src/Gloaming.Messaging/samples/Delivery/Worker/Program.cs),
[fixture ownership](../../src/Gloaming.Messaging/tests/Fixtures/README.md),
[dependency proof](../../tests/ArchitectureTests/AssemblyDependencyTests.cs) and CI/solution paths.
Consumer test-host policy includes failure classification, fixed-route operator authentication,
metadata-only grants and retention cutoffs. None becomes a Gloaming API, template requirement
or new extraction candidate. **No new reusable mechanism was proven; this batch reorganizes
existing consumers and proofs.**
Remaining gaps at that checkpoint were other families, Wholesale/template and integrated
cleanup. The owner approved this complete batch; commit `5d86b20` passed all hooks. Umbrine
durable human workflows remain separate.

## Application scaffolding retirement

Committed as `dd6eb77`. Retired **58 tracked files**: the native template skeleton,
TypeScript creator/proof/presets and their lockfile, generated-only CI workflow, and the
ContextDemo application/test graph. Generator-specific `jsonc-parser`/`koffi` dependencies
and Dependabot roots disappear with that workspace. Removed projects no longer enter the
solution, architecture loader, hooks or native CI commands; the reusable CI scope output
no longer advertises a template lane. Retired local build/dependency output and empty folders
are removed as well. No replacement generator, framework host or skill router is introduced.

The [application patterns](../application-patterns.md) describe existing consumer setup.
The [native operation recipe](../../src/Gloaming.Tenancy/docs/composition.md) is exercised by
[ContextCompositionTests](../../tests/ContextCompositionTests/CompositionTests.cs), which reference
only ActorIdentity core, Tenancy core, native DI and xUnit. Root/family entry points use these
current owners. The glossary reflects Umbrine's library/pattern adoption. Historical links
resolve against the last retained source, `5d86b20`; the generator's creation guarantees remain
historical evidence rather than supported current behavior.

| Removed behavior or proof | Current disposition |
| --- | --- |
| ContextDemo alias ownership, independent registration, attribution, required contexts and concurrent scopes | Twelve direct native-DI cases retain these obligations, adding explicit child-scope, cancellation/failure cleanup and partial-establishment assertions. No business model or joint context runtime is required. |
| SKU availability, draft fulfilment and fixed console output | Retired invented consumer policy; no library quantity/preview contract remains. The test count deliberately changes rather than retaining business assertions to preserve a number. |
| Catalog ownership, caller transaction/save/commit, stale version and rollback | Current Persistence adoption tests retain actual database cases, including stored-owner forgery, stale server reload, failed-save rollback and independent module migrations. These are existing proofs, rerun here; Catalog-specific DTO/rule/response details are retired. |
| Generated Catalog price/immutability rules and optional report worker | Retired consumer examples, including worker mandate, reporting/output and process CLI behavior. They were not Gloaming APIs. Fresh-scope composition is demonstrated directly; no replacement worker engine is claimed. |
| Generator naming/source copies, destination atomicity and selected worker materialization | Retired tooling guarantees. Six fresh-cache package-only consumers prove ordinary retained adoption without ProjectReferences; no source-copy or existing-destination behavior is promised. |
| Catalog template migration chain | Retired with its only consumer; there are no external databases to upgrade. All 157 fixtures/migration files outside the retired applications remain byte-identical, including current Persistence module migrations and historical payloads. |

Five obsolete architecture theory cases referring to ContextDemo are removed along with its
loader entry; one dependency assertion checks the new composition consumer. Library-to-library
isolation constraints remain. The CI map adds the cross-family consumer to Tenancy's lane,
which already tracks ActorIdentity core, and maps the consumer root explicitly. Local native-glob
comparison across 1,299 active paths and 14 retained filters changes selection only for the new
consumer. Five changed YAML files parse; this is local mapping evidence, not GitHub scheduling.

Fresh verification: 12 composition, 19 actor, 17 tenancy, 109 architecture and 36 Persistence
adoption cases passed (**193 total**). All 21 local package archives and six isolated consumers
passed. The full solution builds with zero warnings/errors; all 919 CSharpier inputs, native
style and analyzer checks pass. A local scan checks 2,926 Markdown targets and 279 project/source
references with no missing targets; historical links were validated with native Git object
lookup before replacement. Other library implementations and public contracts
are unchanged; their unrelated runtime suites were not rerun for this retirement. The retired
generator/ContextDemo executions were not rerun or recertified; their earlier results remain
dated historical evidence.

Package evidence: `/home/oxface/.cache/tmp/gloaming-package-proof-kJvaXl/manifest.json`,
version `1.0.0-local.1792710381178.g2f905b8e`, SHA-256
`ee402029c99d6182a58107655667c0460c078bd22ed591d2f83203544773547d`.
Logs: `/tmp/gloaming-scaffolding-{composition,actor,tenant,architecture,persistence,packages}.log`.
The package-proof PostgreSQL container was stopped and auto-removed. The owner approved this
complete batch, committed as `dd6eb77` after all hooks passed.

**No new reusable mechanism was proven.** Native registration and scope ownership are editable
consumer setup; admission, Catalog/SKU rules and worker mandates remain consumer policy.
No extraction candidate earns a new API in this batch. Remaining work is the persistence/event
example reduction, Wholesale reduction and integrated publication cleanup below. Umbrine human
workflows and implementation remain outside this cleanup.

## Remaining family example consolidation

Current batch, uncommitted. Four focused executable paths replace the older multi-journey
entry points; their four adoption projects now live under family `tests/` directories.
Current guides, solution groups, CI and hooks follow that ownership in the same batch.
No Gloaming library interface or implementation changes.

| Family | Public example | Retained test-owned coverage |
| --- | --- | --- |
| Persistence | Two module-owned contexts with direct owned reads/writes and separate native commits. The CLI no longer runs the address/profile update journey. | All 36 adoption cases, including two-save profile atomicity, stale reload, cancellation, stored-owner forgery, relationships and independent migration order. The profile operation/DTO are fixtures. |
| EventSourcing | One counter append/rejection path using the caller's native transaction and `TimeProvider.System`; the old fixed sample clock is removed. | Raw counter/note mapping, alternate keys, bounded history, encoding faults, concurrency and default-envelope adoption. Native counter processes run twice both on an empty database and alongside the old stream families. |
| Events | One small receipt codec/upcaster reads unchanged v1/v2 literals and writes schema 2. The executable references only Serialization. | Stock/purchasing folds, historical ranges, nested/chained upgrades and alias replacement remain explicit fixture recipes. The old multi-journey output and `--schema-evolution` CLI are retired. |
| EventReactions | One package-free immutable quote model, explicit sink and mapped native EF reaction cascade. | Direct-source entities, consumer-owned base, registrations and migrations move into the adopter. Both shapes retain save/stage/delete/order/failure/cancellation proofs and real shared EF composition with ownership/outbox roles. The focused application is also launched as a process. |

The native-only `EfInterceptionResearchTests` prototype is retired: 11 methods/18 cases
exercised custom probes rather than Gloaming. Its dated findings/source remain pinned in Git.
Actual [capability composition](../../src/Gloaming.EventReactions/tests/AdoptionTests/CapabilityCompositionTests.cs)
and native save/fault adoption tests now own the supported library protocol; generic EF
characterization is no longer an advertised product regression suite. The previous dual-flow
reaction output assertion becomes an actual focused-process proof rather than a second shell
for the same helper. Its consumer-base assertion moves into the existing direct save cases.

All **125 migration source/literal JSON files** retain their original bytes, including the
relocated Direct migration chain and payloads linked into Wholesale. Public sample project/
assembly and context names remain stable for their model metadata. Direct-source types and
profile helpers are deliberately test-owned now; Direct migrations are discovered from that
context's new test assembly. No persisted schema, migration ID or payload repair was required.
There are no external consumers requiring compatibility with the retired sample APIs/CLIs.
Retained test/helper methods were mapped across owners; changed entry-point assertions are
identified above rather than treated as byte-identical tests.

Fresh verification:

| Check | Result |
| --- | --- |
| Ownership adoption | 36 passed. |
| Storage adoption | 37 passed. |
| Codec/history adoption | 25 passed. |
| Reactions/shared EF adoption | 86 passed after retiring the 18 prototype cases. |
| Architecture | 110 passed, including serialization-only sample dependency declaration. |
| Solution and code quality | Full solution builds with zero warnings/errors; formatting, style and analyzers pass. |
| Package adoption | All 21 archives and six isolated package-only consumers pass. |
| Fixtures/references/review state | 125 migration/literal hashes match; current local links/project references resolve; original empty index unchanged. |

These are **294 fresh test cases**, not the retired research count or prior-slice runs.
Other family-only runtime suites were not rerun: their implementations/contracts are unchanged.
The earlier native research findings remain dated evidence, not new executions.
Local native-glob comparison of 1,298 active paths and 14 filters changes selection only for
`tests/Support/ChildHost.cs`, adding its new Events/EventReactions adopters. YAML parses;
this does not execute GitHub scheduling.

Package evidence: `/home/oxface/.cache/tmp/gloaming-package-proof-IFnEvc/manifest.json`,
version `1.0.0-local.1792712551632.g53f9bb95`, SHA-256
`382efb13fc2290e10e619ee0ec74bb797df793e5cedfa56da53cbe7e4a2e851e`.
Logs: `/tmp/gloaming-family-{ownership,storage,codec,reactions,architecture}-tests.log` and
`/tmp/gloaming-family-packages.log`. The disposable package-proof PostgreSQL container was
stopped and auto-removed. The owner approved this family batch; `ae2909c` passed every commit
hook and preserved the exact approved snapshot.

**No new reusable mechanism was proven.** The receipt field rename, counter ceiling,
customer/profile policy and quote rules remain consumer code. Test-owned shape and fault
variants retain useful evidence without becoming more public applications; no new library,
framework host or extraction candidate is added. Next is Wholesale reduction, followed by
integrated documentation/test/tooling cleanup and publication review. Umbrine remains separate.

## Wholesale Purchasing retirement — 2026-10-23

Owner-approved snapshot committed as `8601204`, after all hooks passed. It followed the exact
owner-approved family snapshot `ae2909c`. Wholesale had four modules at that checkpoint:
Access, Sales, Inventory and the pending-retirement Dispatch experiment. Its API/reservation
journey needs only the first three. This increment removes Purchasing, not a technical library.

The solution, API, Worker and architecture project no longer reference Purchasing or its
Contracts. [Finite API setup](../../samples/Wholesale/Api/DemoSetup.cs) migrates only Access,
Sales and Inventory; [operator commands](../../samples/Wholesale/Worker/Maintenance/BusinessOperations.cs)
now expose Inventory directly without module switches. The existing setup/open/receive/read,
stale-command and foreign-tenant assertions still execute real product processes. Purchasing's
40 owned source files, including nine migration files, are retired; there are no external
consumers or databases requiring that sample schema. The unchanged literal Purchasing payloads
and codec/history/upcasting recipes remain with the Events family and are no longer copied
into Wholesale. Its test project now links only the Inventory literals it actually uses.

[Inventory append proofs](../../samples/Wholesale/IntegrationTests/EventPersistence/AppendTests.cs)
and [history proofs](../../samples/Wholesale/IntegrationTests/EventPersistence/HistoryReadTests.cs)
retain their native guarantees with typed Inventory helpers. Remove the former Boolean module
axis, Purchasing branches and service registrations rather than keeping a dead generic fixture.
The history child uses the shared disposable process harness and a 60-second deadline, with
six Inventory output lines. Forward migration still preserves the pre-existing availability
catalog and adds owned stock history. Retained module migration bytes are unchanged.

Proof disposition:

| Retired variant | Current obligation and evidence |
| --- | --- |
| Purchasing append/storage/history/tenant/race/save-fault matrix | Inventory retains the same technical axes, including caller commit visibility, complete winner, required state, rollback across saves, raw constraints and fresh-context recovery. Independent EventSourcing PostgreSQL readers/rebuilders retain native direct-JSON variation without sample module dependencies. |
| Purchasing JSONB replacement failure and recovery | Existing [ledger repair tests](../../src/Gloaming.EventSourcing/tests/EventSourcingPostgresTests/RebuildTests.cs) retain failed replacement, transaction rollback and fresh repair. Inventory upcasting/repair tests also retain corrupted JSONB and repair-write failure. Both retired and retained rows map native JsonElement; no separate EF value-converter guarantee is lost. |
| Price/total overflow, line replacement and query-derived summary | Retired consumer policy, not a Gloaming query/projection capability. [Aggregate tests](../../src/Gloaming.EventSourcing/tests/EventSourcingTests/AggregateTests.cs) retain final-candidate validation and unchanged accepted state/version/pending facts after rejected evolution. Inventory retains whole-batch overflow rejection before tracking. |
| Mixed Sales/Purchasing DDD journey | Retire the combined demonstration. Sales immutable profile/domain and native failure/concurrency proofs remain, together with actual API profile/audit usage. Inventory supplies the event-sourced consumer path. |
| Purchasing-first versus Inventory-first migration order | Retire the removed module's compatibility obligation. Inventory's native forward migration remains; the earlier family checkpoint separately proved independently owned Inventory/Sales migration histories. That earlier result is historical here, not another fresh run. |

Exact test accounting: 349 methods / 713 cases become 343 methods / 635 cases. Six methods
(eight cases) explicitly retire Purchasing policy, its overlapping JSONB repair or the mixed
journey. Thirty-one retained methods lose 70 duplicate/removed-module variants. Five method
names now describe their retained Inventory behavior. No unexplained test-method removal or
new mirrored tests; `--scenario schema-evolution`, event/intake/broker faults and actual
regeneration processes remain. Architecture cases become 100 from 110 because the removed
implementation and Contracts no longer participate in dependency matrices.

Fresh verification for this increment:

| Check | Result |
| --- | --- |
| Wholesale integration | 635 passed, PostgreSQL/RabbitMQ plus real setup/worker/maintenance processes. |
| Final Inventory persistence reruns | All 135 selected cases passed after helper typing/cleanup and again with only the four Inventory literals in output; a subset of the 635, not additional distinct cases. |
| Architecture | 100 passed for the retained module/library/host boundaries. |
| Independent EventSourcing | 92 PostgreSQL cases and 11 core aggregate cases passed. |
| Actual Aspire setup/runtime | 2 passed: explicit normal setup/readiness/outage and restricted Sales RLS provisioning/startup. Browser/OIDC journeys were not rerun; no authentication or browser wiring changed. |
| Solution/tooling | Full solution build, zero warnings/errors; CSharpier 877 inputs, native style/analyzer verification and diff whitespace pass. |
| Owned data/references | All 60 retained module migration files, nine literal JSON payloads and the Access historical SQL fixture match `ae2909c`; local links/project references resolve. |

These are **840 distinct fresh test cases**; the 135-case Inventory subset was rerun after
the final helper and fixture changes. Logs:
`/tmp/gloaming-purchasing-{wholesale,architecture,event-sourcing,aggregate,runtime}-tests.log`,
`/tmp/gloaming-purchasing-final-{event,fixture}-tests.log` and
`/tmp/gloaming-purchasing-{build-final,csharpier,style,analyzers}.log`.
All 21 package implementations and public interfaces remain unchanged; package-only consumers
were last run in the family checkpoint, not rerun here. Current package docs use Inventory
rather than the retired module. Twenty-one historical source links are pinned to verified Git
objects at `ae2909c`; they become remotely available with eventual branch publication.

Existing CI capability/module globs already cover these changed/deleted paths; no standalone
Purchasing lane or hook remains. Solution and architecture references are updated together.
The existing empty staged review snapshot is preserved. Review the operator/setup changes,
retained Inventory proof matrix, [module guide](../../samples/Wholesale/modules/README.md),
[scenario proof map](../../samples/Wholesale/IntegrationTests/Scenarios/EventPersistence/README.md)
and the explicit retirement above.

**No new reusable mechanism was proven or proposed.** Quantity, pricing, profile and reservation
rules remain consumer policy; no business Contracts, compatibility layer, generator or framework
host is promoted into Gloaming. This narrows supported sample behavior while preserving existing
library evidence. Dispatch population/disclosure/route-specific cleanup remains next, followed
by integrated document/test/CI relevance, package adoption and independent code/architecture
review. Those gaps remain open; this is not publication sign-off or Umbrine implementation.

## Wholesale Dispatch retirement — 2026-10-23

Uncommitted and unstaged continuation from owner-approved Purchasing checkpoint `8601204`.
Wholesale now contains **Access, Sales and Inventory**, with its existing API and local/broker
order/reservation journey. Dispatch's directory experiment, its worker role and Sales export/
listing/publication policy are retired together. No Gloaming library interface or implementation
changes, replacement framework, generator or new application is introduced.

The removed module owns 13 files, including its three migration files. Directory host/relay,
population/adoption fixtures, Sales directory Contracts/service/audit vocabulary and the B2
plan/adoption report are removed. [ADR 0022](../adr/0022-dispatch-directory-complete-state-population.md)
is marked retired; 12 historical links are pinned to verified Git objects at `8601204`,
including the removed B2 documents. No new historical archive directory is created.

[Profile changes](../../samples/Wholesale/modules/Sales/Sales/CustomerProfiles.cs) retain their
service-owned transaction, customer/address version checks and accepted-change audit. Profile
registration no longer installs directory publication or outbox services. Messaging registration
remains explicit in the stock/reservation roles, with their separate native contexts/credentials.
The two actual local/broker route-exclusion proofs remain under
[local reservations](../../samples/Wholesale/IntegrationTests/Reservations/Local/RouteSelectionTests.cs)
and [restricted broker dispatch](../../samples/Wholesale/IntegrationTests/Reservations/Broker/SalesDispatchTests.Routes.cs),
using `unselected.destination` rather than an active fourth module. Both check untouched
attempt/revision/lease/completion fields while selected stock delivery progresses.

Sales retains all prior migration classes. The native
[forward migration](../../samples/Wholesale/modules/Sales/Sales/Migrations/20261023083547_RetireCustomerDirectory.cs)
drops only `customer_profiles.delivery_listed`; its model snapshot loses that property's six
lines. [The native upgrade proof](../../samples/Wholesale/IntegrationTests/Api/ProfileMigrationTests.cs)
creates a populated pre-retirement schema, including withdrawn listings, accepted audit and
pending envelopes on retired/stock routes, then checks profile/audit/envelope preservation,
column removal and a real subsequent HTTP profile edit without new publication. Historical
queued directory envelopes are preserved, not automatically consumed/deleted/redriven. `Down`
restores a default-true column, not the old listing decisions. This is explicit consumer
compatibility work, not a library migration or a new durable-message guarantee.

[HTTP RLS provisioning](../../samples/Wholesale/Api/SalesHttpRowSecurity.cs) now selects six
business/audit tables and revokes the HTTP role's outbox table grants. Startup rejects an
unexpected outbox grant, and native HTTP credentials cannot insert an outgoing envelope.
The test fixture seeds the earlier SELECT/INSERT grants before invoking finite setup, proving
actual revocation. This installer remains fresh-database, single-use policy installation;
repeated installation/reconciliation is not claimed. The HTTP model removes its unused outbox
RLS declaration. Worker dispatch/reply privileges and their independent policies remain.

Proof disposition:

| Retired behavior | Retained mechanism or explicit policy retirement |
| --- | --- |
| Full-directory export, baseline cursor/readiness, overlapping population and late-key convergence | Retired consumer read-model policy; no general projection/bootstrap engine or reusable snapshot promise existed. |
| Newer-state merge, equal-revision conflict, withdrawal/relisting fences and receiver admission/search | Retired Dispatch/Sales domain policy. Native tenant/RLS and admitted stock intake remain covered independently. |
| Omitted `Listed` on wire and typed relay/intake | Retired consumer DTO requirement. Gloaming does not select business required fields. Inbox decode/handler failures still roll back effects and leave delivery incomplete under its documented protocol. |
| Directory copy/checkpoint/source-publication atomicity | Directory-specific side effects retire. Independent inbox handler/save/completion rollback, outbox faults and stock consumer transaction proofs remain; failed profile/audit writes still roll back. |
| Local and restricted broker route exclusion | Both consumer proofs retained, plus independent provider RouteSelectionTests for exact parameterized selection, disjoint progress and immutable options. |
| Directory's table/migration chain | Retired with its module under the no-external-consumer compatibility decision. All old Access/Sales/Inventory migration classes and owned payload fixtures remain. |

Exact integration accounting: **343 methods / 635 cases → 329 methods / 622 cases**. Fifteen
methods/cases retire directory policy or its end-to-end adoption; two routing methods move/
rename without losing their assertions. One native migration method is added, and the existing
HTTP privilege matrix gains the outbox-grant case. No unexplained test-method removal.
Architecture cases become **89 from 100**, removing only the retired implementation/Contracts
and Directory namespace variants; all three module and local relay fences remain nonempty.

Fresh verification:

| Check | Result |
| --- | --- |
| Full Wholesale integration | 622 passed: native PostgreSQL/RabbitMQ, API, upgrade, routing and real recovery/maintenance processes. |
| Architecture | 89 passed for retained libraries/modules/Contracts/hosts. |
| Independent messaging providers | 108 outbox and 93 inbox PostgreSQL cases passed, including the preserved route/transaction/fault obligations. |
| Actual Aspire/Keycloak/Chromium | All 6 passed: setup/readiness/outage, restricted runtime provisioning, normal/restricted authenticated profile edits, membership and unmapped-account rejection. |
| Final HTTP model alignment | All 15 HTTP/RLS cases and the real restricted-runtime setup case passed again after removing the unused outbox declaration; these are subsets of the counts above. |
| Build/tooling | Full solution build, zero warnings/errors; CSharpier 858 inputs, native style/analyzers and diff whitespace pass. |
| Owned data/references/review state | All 56 unchanged migration preimages, nine literal JSON payloads and the historical Access SQL fixture match `8601204`; only the intended Sales snapshot differs, with the two new migration source files. Local links/project references resolve and the original empty staged snapshot is preserved. |

These are **918 distinct fresh cases**; final affected-subset reruns do not add to that count.
The first full run's two new-fixture failures were corrected: native policy installation must
not be repeated, and EF raw-SQL formatting must not reinterpret literal JSON braces. The clean
full rerun is `/tmp/gloaming-dispatch-wholesale-final-tests.log`. Other evidence:
`/tmp/gloaming-dispatch-{architecture,outbox,inbox,runtime}-tests.log`,
`/tmp/gloaming-dispatch-final-{http,runtime}-tests.log` and
`/tmp/gloaming-dispatch-{build-final,csharpier,style-final,analyzers-final,links}.log`.
No package implementation/artifact input changes; package-only consumers were not rerun
here. The family checkpoint's 21-archive/six-consumer result remains historical until the
planned integrated verification.

Existing CI globs already select module/API/worker, integration, architecture and runtime
changes/deletions. The solution removes Dispatch's two projects; no dedicated Dispatch lane
or hook remains. Review Sales publication/registration removal, HTTP provisioning/model/grants,
the forward migration and upgrade proof, retained routing proofs and changed browser assertions.
Current [module](../../samples/Wholesale/modules/README.md), [Sales](../../samples/Wholesale/modules/Sales/README.md)
and [API](../../samples/Wholesale/Api/README.md) guides describe the resulting behavior.

**No new reusable mechanism was proven or proposed.** Existing inbox/outbox/RLS mechanisms are
re-exercised with explicit consumer ownership. Template work remains retired; sample directory
and disclosure policy is removed, not promoted into Gloaming. Remaining work is repository
consolidation and integrated verification/independent review closure, then the owner's extra
documentation/test pass and later separate-session sweep. These remain open; this increment
does not implement Umbrine or establish publication sign-off.

## Confirmed direction and review criteria

The owner selected the following direction in the discussion leading to this assessment:

- Gloaming is a set of reusable libraries. Application patterns belong in concise guidance
  and, after real usage, focused skills. A configurable bootstrap CLI is deferred.
- An explicitly selected library operation may own a bounded transaction and invoke a
  consumer handler when its guarantees and obligations are clear.
- Umbrine is the next real consumer: a modular-monolith ERP with module-owned persistence,
  multitenancy and durable business processes. Its checkout currently contains requirements,
  not an application implementation. Its business rules remain consumer-owned.
- There are no external consumers or databases requiring compatibility with today's public
  interfaces, sample schemas or retained messages. Breaking changes are acceptable.
- Most experimental applications may be removed. Retain a small Wholesale composition with
  three or four modules and focused examples for the retained libraries.

A library earns its interface when deleting it would return meaningful technical complexity
to consumers. Assess required configuration, ordering, scopes, failure behavior and supported
models together with public methods. Small source size alone does not decide package value.
Optional registration alone does not establish a useful interface either.

Consumers retain business decisions, authorization, tenant admission, domain/payload meaning,
module Contracts, deployment topology and final persistence outside deliberately delegated
operations. Provider-specific storage protocols and fixed technical tables can be legitimate
library choices; document their limits instead of promising arbitrary storage authority.

This review does not select another database provider, transport abstraction, authorization
engine, scheduler, application module runtime or general workflow builder.

## Package dispositions

The inspected tree has 23 packable library projects across nine families. Package entries
below cover the complete inventory. Keep means retain the capability and current package
division unless the row states otherwise; it does not certify every implementation detail.

| Package | Disposition | Reason and consumer obligation |
| --- | --- | --- |
| Gloaming.ActorIdentity | Keep | Opaque identities, actor/initiator attribution and single-assignment operation context. Authentication and permissions stay outside. |
| Gloaming.ActorIdentity.AspNetCore | Keep | Effective-principal mapping and native policy-evaluator ordering remove actual integration work. Preserve explicit custom-inner-evaluator support and consumer failure presentation. |
| Gloaming.Tenancy | Keep | Independent immutable tenant selection; deliberate tenantless execution remains distinct from missing context. Admission stays outside. |
| Gloaming.Tenancy.AspNetCore | Keep | Explicit resolver, requirement metadata and candidate helpers. Neither route/subdomain selection nor established tenancy grants membership or resource permission. |
| Gloaming.Tenancy.EntityFrameworkCore.Postgres | Keep | Explicit transaction-local setup with native connection/transaction checks, independently usable without ownership mapping. Present this and automatic RLS setup as separate recipes. |
| Gloaming.Persistence.EntityFrameworkCore | Keep | Named ownership filters, tracked-write checks and stored-owner concurrency predicates. Make its ordinary single-table model restrictions prominent. |
| Gloaming.Persistence.EntityFrameworkCore.Postgres | Keep | Selected transaction-local RLS integration and migration helpers. Policies, runtime roles, permissions and caller-owned read/write transactions remain explicit. |
| Gloaming.EntityFrameworkCore | Keep | Bounded preparation, change detection and validation ordering shared by actual feature families. Keep this independent of features, providers and hosting. |
| Gloaming.Events.Serialization | Keep | Explicit durable identities, native JSON policy and validated forward upcast paths. Consumers own schema meaning and reader/writer rollout. |
| Gloaming.Events.History | Keep | Independent contiguous-range and recorded-time integrity checks. Its small implementation still concentrates shared validation; it does not certify database ordering or isolation. |
| Gloaming.EventReactions | Keep | Consumer-composed buffers and bounded, ordered exact-type reactions without required domain inheritance. No external effects or business dependency graph is inferred. |
| Gloaming.EventReactions.EntityFrameworkCore | Keep | Explicit source selectors and native save preparation. Keep transactions, required sources and whole-operation recovery visible. |
| Gloaming.EventSourcing | Keep | Optional aggregate bookkeeping and a replaceable aggregate interface. Immutable evolution, domain decisions and event meaning remain consumer code. |
| Gloaming.EventSourcing.EntityFrameworkCore | Keep and simplify setup | Retain append/version coordination, required state, history and independent rebuilding. Keep complete regeneration optional; improve contextual setup only from concrete adopter feedback. |
| Gloaming.Auditing.EntityFrameworkCore | Keep | Explicit envelopes, mapping and same-transaction staging/validation. The ActorIdentity dependency supplies actual attribution types. Required-audit selection, visibility and retention stay outside. |
| Gloaming.Auditing.EntityFrameworkCore.Postgres | Retired in d63f567 | Its sole mapping helper called ConfigureAudit and selected JSONB. The audit protocol remains in the EF package. |
| Gloaming.Messaging | Keep | Owned wire envelopes, publication acceptance seam and native diagnostics. JSON payloads and GUID delivery IDs are supported protocol choices; routing and receiver trust remain consumer decisions. |
| Gloaming.Messaging.EntityFrameworkCore | Simplified in ffc6ec2 | Storage records, save guards and callable roles remain. Hosted loops, options and registration now belong to the optional Hosting package. |
| Gloaming.Messaging.EntityFrameworkCore.Postgres | Keep | PostgreSQL leases, deduplicating intake, locked processing, recovery and maintenance concentrate substantial correctness work. The fixed technical mappings are part of its contract. |
| Gloaming.Messaging.OpenTelemetry | Retired in d63f567 | Its two helpers only subscribed native builders to the documented activity-source and meter names. Emitted diagnostics and their tests are retained. |
| Gloaming.Messaging.RabbitMQ | Keep | Native connection/channel lifetime, partial-acquisition cleanup and terminal observation have independent value. Continue exposing native channels and consumer-selected recovery/topology. |
| Gloaming.Messaging.Workflows | Consolidated into the EF package in the working tree | Definition types keep their namespaces; the friend-only assembly split is removed. Definition-only consumers now take EF dependencies. |
| Gloaming.Messaging.Workflows.EntityFrameworkCore | Combined optional package, committed as `56a8b12` | Saved-continuation selection, retained outcomes, concurrency and atomic outgoing requests earn reuse. Narrow its description to bounded durable continuations. |

The combined continuation package retains the EF package/assembly identity and both existing
Workflows namespaces. Optional `Gloaming.Messaging.EntityFrameworkCore.Hosting` depends on
callable EF roles; the EF/provider layers do not depend on hosting. There is no package-count target.

## Findings requiring concrete follow-up

### Combine the workflow packages without discarding the protocol

[WorkflowDefinition](../../src/Gloaming.Messaging/Gloaming.Messaging.Workflows.EntityFrameworkCore/WorkflowDefinition.cs)
exposes its name/version but keeps its start delegate, serializer and reply/signal bindings
internal. [WorkflowTurn](../../src/Gloaming.Messaging/Gloaming.Messaging.Workflows.EntityFrameworkCore/WorkflowTurn.cs)
also hides its state, command and continuation data.
[The former core project](https://github.com/oxface/modulith-foundry/blob/ffc6ec22a01085cf7352b5ab2cc91d91a5fd6e4e/src/Gloaming.Messaging/Gloaming.Messaging.Workflows/Gloaming.Messaging.Workflows.csproj)
granted the EF runner friend access. An alternative runner could not consume the supplied definition
through its public interface. Former core-only tests exercised definition validation, not
independent execution. The inspected Wholesale Contracts do not require workflow types.

Combine these packages rather than making internal execution machinery public merely to
justify the split. Keep the combined package optional from ordinary Messaging. Preserve
caller-owned save/commit, frozen definitions, stable persisted continuation names, duplicate
outcome checks, concurrency and atomic request staging.

The trade-off is an EF dependency wherever consumers reference the combined definitions.
If Umbrine needs definitions in a provider-free policy assembly, retain the split and review
a small public execution/inspection seam with independent public-interface tests. The current
adopters do not establish that need. Combining is the present recommendation, not a reason
to abandon an independently useful dependency separation if real adoption demonstrates it.

[EfWorkflow](../../src/Gloaming.Messaging/Gloaming.Messaging.Workflows.EntityFrameworkCore/EfWorkflow.cs)
does meaningful work: it retains initial input, invokes only the saved continuation and
stages its next request with progress. Existing
[transaction](../../src/Gloaming.Messaging/tests/WorkflowPostgresTests/TransactionTests.cs)
and [inbox-composition](../../src/Gloaming.Messaging/tests/WorkflowPostgresTests/InboxCompositionTests.cs)
assertions cover rollback, competing turns, unchanged duplicate outcomes and next-request
atomicity. These are reasons to retain the mechanism.

Its limits must remain explicit: one outgoing request per turn, one pending continuation,
JSON state/input/outcome storage, synchronous consumer methods, definition-version checks,
and the runner's GUID-form correlation/causation convention. State changes can represent
business branching, but there is no provided human-assignment model, fan-out/join protocol,
reminder scheduler or authorization engine. Umbrine's human processes should retain their
own module state and rules; adopt this runner only for interactions that fit its protocol.

### Separate worker hosting from persistence

[The EF messaging project](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore/Gloaming.Messaging.EntityFrameworkCore.csproj)
originally referenced Hosting abstractions even for an enqueue-only consumer. The
[hosting slice](messaging-worker-hosting.md) removes that dependency and moves the workers to
[their optional package](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Hosting/README.md).
[OutboxWorker](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Hosting/OutboxWorker.cs)
and [InboxWorker](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Hosting/InboxWorker.cs)
own sequential polling, fresh DI scopes, delays, startup resolution and fault supervision.
Those are useful optional execution policies, distinct from record mapping and callable work.

The two loops, options and worker registration helpers move together. Keep callable
dispatch/processing and their failure distinctions in the owning persistence packages.
An enqueue-only consumer should restore no Gloaming worker integration and no Hosting
abstractions through Messaging. Selecting hosting should retain current fresh-scope,
cancellation and fatal-accounting/completion behavior. Consumers can still write native
workers around the callable operations.

This is a dependency/interface improvement, not a claim that importing today's package
starts a worker. Current worker registration is already explicit.

### Replace two thin adapters with native configuration

[ConfigurePostgresAudit](https://github.com/oxface/modulith-foundry/blob/1582e77849088e243881c82a37c0a42b2a11a322/src/Gloaming.Auditing/Gloaming.Auditing.EntityFrameworkCore.Postgres/PostgresAuditModelExtensions.cs)
adds only JSONB selection to the existing relational mapping. Its separate package also
declares the Npgsql EF dependency. The consumer already chooses its provider and can write:

```csharp
model.ConfigureAudit("operations", "audit")
    .Property(row => row.Details).HasColumnType("jsonb");
```

The [OTel helpers](https://github.com/oxface/modulith-foundry/blob/1582e77849088e243881c82a37c0a42b2a11a322/src/Gloaming.Messaging/Gloaming.Messaging.OpenTelemetry/GloamingMessagingTracerProviderBuilderExtensions.cs)
similarly reduce to native configuration:

```csharp
traces.AddSource("Gloaming.Messaging");
metrics.AddMeter("Gloaming.Messaging");
```

Document these source/meter names as a supported diagnostics contract. Remove adapter-only
extension tests with the adapters, while retaining tests of actual emitted activities,
links, measurements, exporter independence and audit persistence. These packages are
shallow conveniences, not evidence of a framework violation.

### Preserve the inbox and outbox protocols and their different transaction owners

| Operation | Persistence owner | Consumer responsibility |
| --- | --- | --- |
| Outbox enqueue | Caller stages business state and envelope, then saves/commits. | Choose semantic operation identity, wire contract and publication route. |
| Outbox dispatch | Library commits a short claim, publishes outside that transaction, then fences completion/accounting. | Supply acceptance semantics, transport lifetime and continued execution. |
| Inbox intake | Library executes retained insert/compare in the caller's transaction; caller commits before acknowledgment. | Validate producer binding/admission and control native acknowledgment. |
| Inbox processing | Explicitly invoked processor owns its row lock, savepoint, local save, completion and commit. | Supply bounded local handler effects and business concurrency; queue external work. |
| Dead-letter retry and retention | Explicit operations own bounded native transactions. | Authorize invocation, select retention/evidence windows and schedule repetition. |

[PostgresOutboxDispatcher](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/PostgresOutboxDispatcher.cs)
and [PostgresInboxProcessor](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/PostgresInboxProcessor.cs)
remove actual protocol complexity. Replacing them with public claim handles or consumer-authored
transaction choreography would spread that complexity. The owner's explicit-delegation
decision supports retaining these operations.

Keep lease fencing, lock-before-savepoint accounting, uncertain completion distinctions,
receipt generations, conditional redrive and bounded maintenance. Keep inbox and outbox
separate; they do not share one interchangeable delivery/lease protocol. Do not add a common
bus, repository, transaction wrapper or provider dialect.

The provider requires supplied records, fixed column/JSONB/time mappings and explicit schema
and table names, as checked by
[outbox storage](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/PostgresOutboxStorage.cs)
and [inbox storage](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/PostgresInboxStorage.cs).
Consumers own these tables' deployment and permissions; they cannot arbitrarily remap the
protocol. No second provider is established by the relational package names.

### Make operation-context and model constraints visible during adoption

Automatic RLS establishes tenancy when the transaction starts. Inbox processing starts its
transaction before selecting a row and invoking the handler. Establishing tenancy only in
that handler is too late for an RLS-selected context. A global dispatcher also executes raw
SQL outside EF filters. These compositions need deliberately different authority.

The existing [restricted reply composition](../../samples/Wholesale/Worker/Broker/SalesReplyProcessing.cs)
establishes the selected tenant/system actor through scoped factories before processing.
Retain this example's ordering and restricted-credential proofs. Umbrine must choose its
admission/revocation policy and tenant/global processing lanes; Messaging must not infer
authority from TenantKey or create a universal tenant worker.

[Shared EF composition](../../src/Gloaming.EntityFrameworkCore/Gloaming.EntityFrameworkCore/CapabilityConfiguration.cs)
has demonstrated value in preparation-before-validation ordering and missing-binding checks.
Keep explicit feature validators available. Its scoped options/interceptor state excludes
pooled contexts and singleton options; do not present it as general EF integration for
every lifetime. SQL/bulk bypasses and later native callbacks retain their existing limits.

[Ownership metadata](../../src/Gloaming.Persistence/Gloaming.Persistence.EntityFrameworkCore/TenantOwnershipMetadata.cs)
rejects inheritance, owned/shared/split mappings, complex properties and converted keys.
RLS further selects unconverted text discriminators. These are current supported-model
limits, not proof that rich domain objects or all EF mappings are supported. Revisit an
individual restriction when a concrete Umbrine model needs it, with the corresponding
stored-owner and RLS proofs; do not preemptively generalize the mapping system.

[Aggregate regeneration](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/AggregateRegenerator.cs)
also earns retention through bounded retries, disposal and indeterminate-commit handling.
Its contextual adopter currently
[decorates IServiceScopeFactory](../../samples/Wholesale/modules/Inventory/Inventory/StockPositions/StockPositionMaintenance.cs)
to initialize tenancy before resolving each context. This is adoption friction to revisit
with a real consumer, especially if asynchronous admission is needed. It does not justify
a new operation runtime or a new package in this audit. Direct rebuild remains available
with caller-owned transaction/save/commit.

## Samples and test ownership

Use three primary example goals: focused adoption of a selected library, one small
cross-family composition, and executable fault/compatibility proof fixtures. A process
fixture or literal payload need not remain a separately advertised application.

| Example | Current disposition | Evidence condition |
| --- | --- | --- |
| ContextDemo | Retired in the current batch; direct native-DI composition tests and the operation recipe replace the fixture application. | Independent cores and 12 composition cases retain lifecycle/requirement/isolation obligations; SKU/preview policy is explicitly retired. |
| PersistenceDemo | Narrowed in the current batch to owned reads/writes and native commits; profile mutation is test-owned. | All 36 adopter cases remain, including stored-owner, relationship, migration and profile transaction proofs; independent GUID/RLS consumers remain untouched. |
| EventCodecDemo | Narrowed to a receipt field rename/read/write example with only Serialization; larger stock/purchasing/history recipes are test-owned. | All historic JSON remains unchanged; 25 adoption cases include the actual new executable. |
| EventStorageDemo | Public executable is the counter append path; raw multi-family mapping moves to test fixtures. | 37 adoption cases retain raw/custom/default mapping, metadata, append and fault/concurrency behavior; actual processes cover fresh and populated storage. |
| EventReactionsDemo | Package-free immutable model is the public path; direct-source inheritance and extra variants are test-owned. Pure EF prototype probes are retired. | 86 current adopter cases retain actual Gloaming composition, selection, order, save/recovery and marker-free model proofs. |
| Messaging Delivery (formerly six separate demo/defaults projects) | Implemented in the current batch: two plain module libraries, producer/worker entry points and test-owned policy/fault fixtures. | All retained adoption, migration, broker, operational and process cases run in DeliveryTests; architecture checks include the complete producer runtime graph. |
| WorkflowDeployment Earlier/Current | Moved to test-owned fixtures in the current batch. | Seven separately compiled deployment tests and four byte-identical literal JSON files remain. |
| Wholesale | Reduce to Access, Sales and Inventory with one order/reservation journey. | Retain the chosen HTTP/admission, module-Contracts, tenant ownership, local reactions, audit, event-sourcing and durable-handoff composition. Keep focused failure scenarios with their library owners. |
| Wholesale Purchasing | Retire from the primary sample. | Transfer any unique event-store assertions first; independent raw/ledger consumers can retain variation without another business module. |
| Wholesale Dispatch | Retire from the primary sample. | Route-selection guarantees already have provider tests. Snapshot/population, disclosure and directory merge/readiness are consumer-owned policy; retaining that experiment is not required by Messaging's route selector. |
| templates/state-stored and tools/template | Retired in the current batch with generated-only CI and dependencies. | Fresh ownership/transaction proofs and package-only adoption remain; creation/naming/worker-specific promises are retired. Concise existing application patterns remain. |

Unimplemented dispositions remain proposals; existing tests are not assumed to be duplicates.
Before each deletion, map its assertions to a retained test or explicitly retire the
behavior. An application can be removed while its useful independent consumer fixture stays.
Conversely, do not preserve business-scenario tests solely to preserve historical counts.

Keep repository architecture checks for library independence, intended dependencies and
the retained sample's Contracts/persistence ownership. Remove assertions that only freeze
retired package identities or application graphs. Shared test support can remain under
`tests/Support`; it is not another application or runtime library.

## Proof obligations for implementation

The following source assertions were inspected or located as current proof owners. No test
execution is claimed by this table; the final-tree executions in
[the previous verification report](v1-final-verification.md) remain historical evidence.

| Retained guarantee | Current proof owner and required follow-up |
| --- | --- |
| Actor lifecycle, scheme-selected identity and independent tenancy | ActorIdentity/Tenancy family tests. Keep policy evaluation, anonymous/tenantless exceptions and fresh operation scopes when shortening examples. |
| Stored-owner checks and actual RLS | Persistence family tests, especially [RowSecurityTests](../../src/Gloaming.Persistence/tests/PostgresTests/RowSecurityTests.cs), plus selected restricted HTTP/worker tests. Keep restricted roles, same-backend reset/rollback and raw/bulk enforcement cases. |
| Explicit audit atomicity | [Audit transaction tests](../../src/Gloaming.Auditing/tests/AuditPostgresTests/TransactionTests.cs) and guard tests. Rerun after replacing JSONB helper calls; verify business/audit rollback and fresh recovery. |
| Preparation and validation ordering | [CapabilityCompositionTests](../../src/Gloaming.EventReactions/tests/AdoptionTests/CapabilityCompositionTests.cs), registration, event ordering and dispatch fault tests. Rehome shared-coordinator assertions when simplifying the demo. |
| Codec and history integrity | Events family tests and independently authored literal payload fixtures. Keep incompatible/unknown schema, upcast and range failures; no current serializer may manufacture the historic expectation. |
| Append, required state and repair | EventSourcing Guard, Concurrency, Rebuild, Regeneration and history-reader tests, plus the raw adoption consumer. Keep rollback, stale observations, disposal and ambiguous commit cases. |
| Outbox claims and completion | [DispatchTests](../../src/Gloaming.Messaging/tests/OutboxPostgresTests/DispatchTests.cs), RouteSelectionTests, failure recovery and CompletionSupervisionTests. Keep expiry/takeover, stale completion, acceptance followed by storage failure and worker containment. |
| Inbox intake, processing and expiry | Inbox Intake, Processing, FailureRecovery, DeadLetter, RetentionConcurrency and authority tests. Keep duplicate-content comparison, lock/savepoint atomicity, generation-conditional retry and post-retention re-execution. |
| Worker lifetime and native transport | Inbox HostingTests, Outbox WorkerTests, RabbitMq Lifecycle/NativeRecovery tests and MessagingWorkerDemo.Tests. Rerun startup, fresh scopes, cancellation, fatal fault supervision, SIGTERM/abrupt death and recovery after the hosting move. |
| Bounded continuation durability | Workflow Execution, Transaction, Serialization and InboxComposition tests plus separately compiled deployment fixtures. Preserve combined progress/outbox/inbox effects after package consolidation. |
| Independent package consumption | [Release harness](../../tools/release/README.md). Update its package inventory, dependency expectations and native telemetry/audit setup; verify archives and consumers without source references. |

Removing historic sample migrations is permitted by the compatibility decision. Keep old
schemas/rows only where a retained migration or durable-payload guarantee needs an executable
upgrade fixture. A clean initial schema and an intentionally old compatibility fixture have
different purposes; neither requires retaining every experimental migration chain.

## Remaining cleanup slices

The owner requested coherent code-led batches with their documentation, tests, CI and pruning,
and allows as many reviewable increments as needed to meet requirements. Purchasing retirement
is committed as `8601204`; Dispatch retirement is the current batch. The planning range after
this batch is **2–4 slices**, plus the explicitly selected extra documentation/test pass.
The owner's later clean-context sweep is a separate session, not claimed by this audit.

| Work | Estimated batches | Completion evidence |
| --- | --- | --- |
| Repository consolidation | 1–2 | Review every document/test for current relevance; reduce active queues and historical duplication; finish CI/hooks/tooling and empty-directory cleanup. Keep related code/docs/proofs together. |
| Verification and independent review closure | 1–2 | Integrated package-only adoption, retained sample/runtime checks, independent code and architecture reviews, and focused fixes for findings. |
| Additional documentation/test pass | At least 1 | Owner-selected final pass after the above, using the completed implementation and review outcomes to remove remaining stale or redundant material. |
| Fresh-context sweep | Separate session | Owner plans another review in a new session; do not count its conclusions as evidence already produced here. |

Every required defect or mismatch remains open until fixed or explicitly disposed of by the
owner. Estimates do not constrain the number of slices. Umbrine implementation and remote
publication are separate; reviewed local packages remain usable during cleanup.

## Original implementation sequence and completion checks

1. **Enable ordinary local consumption.** Add a small pack-only developer command reusing
   the existing library inventory. Pack unique prerelease snapshots into a persistent local
   feed; map Gloaming packages to it in the actual consumer. Retain the heavier isolated
   package-adoption harness as CI verification. This can use today's packages and does not
   depend on the simplifications below. No remote publication is required.
2. **Simplify the two thin adapters.** Replace JSONB and OTel helper usage with native
   configuration, remove their packages, and update package inventories, solution entries,
   dependency assertions and affected documentation. Pass audit persistence, emitted telemetry
   and package-consumption checks. Do not expand this into sample consolidation.
3. **Separate optional worker hosting.** Move loops/options/registration together, update
   consumers and assert that producer-only graphs omit Hosting. Pass worker startup,
   supervision, cancellation and process-recovery checks through the new package.
4. **Combine the continuation packages.** Remove friend-only assembly separation while
   retaining the protocol and current public behavior unless separately reviewed. Pass
   continuation transaction, duplicate outcome, competing turn and deployment-fixture checks.
   Keep human workflow policy outside the library.
5. **Validate the first Umbrine foundation.** Exercise trusted context, current admission,
   module-owned persistence, explicit business permission and accepted-change audit. Select
   one durable handoff from actual business requirements before extending Workflows or
   tenant-aware worker machinery. Event sourcing remains per-aggregate.
6. **Consolidate one sample family at a time, then Wholesale.** Include assertion transfer,
   references, fixtures, migrations, documentation and affected CI in each reviewable change.
   The retained sample has three modules and one useful journey; it need not showcase every
   supported option.
7. **Finish documentation and tooling cleanup.** Classify every tracked document as current
   consumer contract, active convention/decision, useful evidence to distill, or retired
   history to delete. Keep a short direction/pattern guide, family-local setup/limitations,
   relevant ADR rationale and one active work queue. Retire superseded plans/reports through
   Git history instead of building another archive. Remove generator-only dependencies and
   CI; keep family/provider, package-consumer and retained-sample checks. Remove truly empty
   local directories separately: Git does not publish empty directory trees.

Each increment must leave the selected consumer runnable and the repository coherent.
The first Umbrine foundation can proceed before package simplification is complete. Do not
make completion of every cleanup item a prerequisite for using Gloaming in Umbrine.
Skills should automate exercised tasks from the pattern guide; they should not duplicate
the architecture rules or grow an untested matrix of topology/provider/authentication options.

The local-feed proposal follows [NuGet local feeds](https://learn.microsoft.com/en-us/nuget/hosting-packages/local-feeds),
[cache behavior](https://learn.microsoft.com/en-us/nuget/consume-packages/managing-the-global-packages-and-cache-folders)
and [source mapping](https://learn.microsoft.com/en-us/nuget/consume-packages/package-source-mapping).
Change the prerelease version for each snapshot and pack its Gloaming dependency closure.
Third-party restore may still use nuget.org. This is development distribution, not another
runtime mechanism or a remote package-release commitment.

## Original assessment verification and remaining decisions

This assessment inspected every package's declared dependencies and public entry points,
selected implementation paths, independent consumer wiring, relevant test assertions and
CI/package/template ownership. It is an architecture assessment, not an exhaustive correctness
review of every implementation line or a completed audit of all existing documents/tests.

The initial assessment included no implementation, sample, fixture, migration, package
configuration or CI file changes. No runtime/provider/browser/package tests were rerun for
that assessment. Subsequent implementation evidence is linked above. Remaining proposed code and
removal changes require their listed fresh checks; previous passes do not validate them.

Static verification checked all 488 local file-link targets in this report and the three
updated entry documents, with no missing targets. Its 23 unique package-disposition rows
match the inspected project inventory exactly. Whitespace checks passed; the initially
empty staged snapshot remains empty. These checks validate the documentation/inventory,
not execution of the proposed changes.

Owner review should select the package simplifications and sample reductions, then review
each concrete implementation. Exact naming of the combined continuation/hosting packages,
the first Umbrine operation, authorization policy and any richer EF model support remain
implementation decisions. No replacement public interface is approved by this assessment.

**No new reusable mechanism was proven.** The proposed value is a clearer, smaller adoption
interface around existing mechanisms. Library protocols stay technical; recipes and skills
materialize consumer-owned setup; Wholesale and Umbrine retain their business policy.
