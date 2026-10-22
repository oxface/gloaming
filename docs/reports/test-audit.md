# Active test value audit

2026-10-04. The owner requested removal of unnecessary/framework-only tests and an assessment
of custom architecture-test support. Scope: all seven active test projects. Archived tests
and fixtures remain frozen historical evidence; they were not modified or rerun.

## Decision and outcome

Keep a test when it protects a documented library choice, consumer wiring we own, or an
integration failure that cannot be established from a unit assertion. Remove tests of
framework machinery in isolation, repeated implementation details, and fixtures requiring
private annotations outside the supported consumer contract. A framework API appearing in
a test does not make it redundant: the question is which mistake in our code it would catch.

The active suite shrank from **146 to 114 cases**, removing 32 cases net. Library and sample
runtime source was unchanged. No new reusable product mechanism was proven.

| Suite | Before | Current | Maintained value |
| --- | ---: | ---: | --- |
| ActorIdentity | 21 | 19 | Input policy, actor-kind identity, explicit attribution, exactly-once establishment, concurrent publication/reads and disposal guards. |
| Tenancy | 19 | 17 | Opaque key policy, tenantless versus missing, required-tenant checks and independent accessor lifecycle. |
| ContextDemo composition | 23 | 15 | Real DI aliases/lifetimes, independently selected segments, module calls, tenant isolation and consumer admission requirements. |
| EF model/validation | 25 | 23 | Explicit helper effects, owner validation, supported-model rejection and lazy resolution through public calls. |
| Inventory PostgreSQL | 30 | 21 | Real consumer wiring, filtered reads, all save-overload guard paths, protected writes, stored-owner predicates and finite console startup/repeat behavior. |
| Independent GUID PostgreSQL | 6 | 6 | Different key/property/schema without context libraries; prevents accidental sample-specific assumptions. |
| Architecture | 22 | 13 | Ten compiled dependency prohibitions and three small runtime-project declaration policies. |

## Removed or reduced coverage

| Change | Why the removed portion did not earn maintenance |
| --- | --- |
| Remove `RestoredProject`, exact EF transitive package whitelist and sample/test graph snapshots. | They froze incidental NuGet graphs and required a custom assets reader, repository discovery and assertion abstraction. No recurring product defect requiring that machinery was demonstrated. |
| Remove two synthetic package-free fault tests and the ArchUnitNET dependency-detection test. | Mutating arrays and asserting that `Assert.Empty` throws tests xUnit; checking that ArchUnitNET recognizes the real sample dependency tests its algorithm. Nonempty selectors remain directly in the actual rules. |
| Replace nine graph-policy cases with three direct declaration cases. | Only the runtime library dependency posture is a deliberate stable rule. Samples and tests are editable consumer composition; native build and behavior tests exercise standalone usage. |
| Remove child-scope and exception/cancellation disposal demonstrations. | Native scope/`await using` behavior has no custom pipeline here. Accessor disposal guards, actual alias registration/disposal and simultaneous isolated sample operations remain tested. |
| Reduce the six actor-kind/tenant combination cases to one tenantless identified-actor rejection. | Existing human success, system attribution, anonymous rejection and anonymous tenancy-only usage already cover the other consumer policies. |
| Remove four extra whitespace cases, generated hash-code assertions, fixture schema/type assertions and 100-read inner loops. | These repeated native whitespace/record/mapping behavior and identical reads. Empty versus whitespace rejection, exact opaque identities, public ownership model effects and concurrent readers still exercise our choices. |
| Remove the fast test inspecting detached `Update`'s modified flag. | EF's flag behavior is a framework detail. Successful same-tenant detached PostgreSQL writes in both consumers still catch our validator wrongly rejecting that flag. |
| Remove the owned-entity fixture that manually forged private ownership annotations. | It bypassed the public registration interface and tied tests to internal metadata names. It did not establish a supported consumer scenario. |
| Reduce positive save-overload cases from four to two, and foreign detached-write cases from eight to two. | All four guard entry paths remain tested with foreign inserts. Positive saves exercise both consumer overrides; stored-owner update/delete predicates do not require a Cartesian product with native overload forwarding. |
| Remove dedicated native rollback demonstration and a duplicate filtered count query. | This slice owns no transaction protocol. Native rollback remains consumer-controlled; the result-array assertion already proves filter composition without another SQL count. |

## Why the remaining architecture checks are useful

[The three declaration tests](../../tests/ArchitectureTests/AdoptionDependencyTests.cs) read
copied runtime `.csproj` files with native XML APIs, without a parser/helper abstraction.
They catch an accidental unused `PackageReference`, `ProjectReference` or extra
`FrameworkReference` that compiled type analysis could miss. Examples: adding EF or ASP.NET
to ActorIdentity; adding ActorIdentity/Tenancy, Npgsql or hosting to the EF utility. They
protect our stated independently adoptable dependency posture with a small maintenance cost.

[ArchUnitNET rules](../../tests/ArchitectureTests/AssemblyDependencyTests.cs) catch actual
compiled calls/types flowing from libraries into consumer samples or forbidden segments.
Source and target selections must contain types. That guard remains because the first
implementation actually selected no types when using short assembly-name strings.

The trade-off is explicit: direct declaration checks do not evaluate shared imports,
conditional MSBuild execution, central changes or transitive packages. Removing the restored
graph reader gives up those unused-reference checks. Dependency changes still receive owner
review, ordinary restore/auditing/build, and real consumer tests; those are not claimed to
detect every unused reference. A future concrete problem can justify native evaluated-graph
tooling, without restoring a whitelist of every third-party package now.

These are repository/template policies, not compulsory rules shipped in runtime libraries.
No new reusable architecture-test library or extraction candidate was established.

## Retained integration and guard tests

PostgreSQL stored-owner checks remain essential. Our utility constructs the filter and marks
ownership as a concurrency token; a detached payload can otherwise claim the current tenant
while targeting a foreign row ID. Real update/delete rejection and fresh reads prove this
combination through our consumer wiring, rather than merely asserting EF metadata.

Cached-model tests ensure our key expression resolves against each current context. Global
saves ensure our validator does not require tenancy unnecessarily. Successful detached writes
ensure ownership validation does not block ordinary native operations. Four foreign-insert
save cases ensure both overrides protect all supported entry paths. Model classification
protects sample-owned mapping policy rather than relying on automatic library enrollment.

Argument guards and explicit enum-number checks remain because they exercise our public
input contract and the owner's persisted-number convention. Factory/accessor failures are
not assertions that native argument checking or `Enum.IsDefined` works in isolation.

## Verification, review and gaps

All seven active suites passed: **114 tests, no skips**, including **27 real PostgreSQL
cases** on disposable Testcontainers instances with the resource reaper enabled. The current
12-project build passed with zero warnings/errors. Formatting, style/analyzer and
whitespace/link checks passed; all 800 archived files passed their checksum check. Remote
CI has not been observed.

Review the simplified [declaration tests](../../tests/ArchitectureTests/AdoptionDependencyTests.cs),
their copied-project setup in [the test project](../../tests/ArchitectureTests/ArchitectureTests.csproj),
the reduced [context composition](https://github.com/oxface/modulith-foundry/blob/5d86b20df5b0eb82f1aa96b889229ce6f5ca351f/src/Gloaming.Tenancy/samples/ContextDemo.Tests/CompositionTests.cs)
and [Inventory proofs](../../src/Gloaming.Persistence/samples/PersistenceDemo.Tests/OwnershipTests.cs).
Removed private annotations are visible in the unsupported-mapping test diff. No staging
or commit was performed this turn; previous owner-staged work was preserved.

Owned mappings remain unsupported; the removed private-annotation fixture is not replaced
by a claim about a public owned-entity configuration. Custom transaction protocols and
failure/cancellation orchestration need tests when such consumer/library code exists.
Two-module persistence, relationships, migrations and version conflicts remain subsequent
E2 work. Historical 63-context, 61-E2.1 and 22-architecture results stay in their original
reports with links to this current audit, rather than being rewritten as fresh executions.

## Subsequent E2.2 evidence

The audit was checkpointed with E2.1 as `50e7933`. Its 114-case results above remain evidence
of that audited state. [E2.2](e2-2-module-migrations.md) now passes 119 active cases, including
30 PostgreSQL cases. It adds focused two-module migration/history/save-wiring proofs and
two fast schema/artifact policies, replacing the earlier slow Inventory classification case.
No generic architecture helper or synthetic framework/assertion tests were reintroduced.

## Subsequent E2.3 evidence

[E2.3](e2-3-tenant-relationships.md) passes 126 active cases, including 37 PostgreSQL cases.
Its seven new cases protect actual Sales relationship/deletion wiring, detached-edit
compatibility and existing-customer migration. The fast model/artifact policy is extended
without new cases or a generic checker. These are consumer configuration proofs, not tests
of native foreign-key machinery in isolation; E2.2's counts above remain historical.

## Subsequent E2.4 evidence

[E2.4](e2-4-versioned-profile-changes.md) passes 131 active cases, including 42 PostgreSQL
cases. Five new cases exercise the actual consumer operation: caller-controlled commit,
stale request versions with a fresh server context, a real second-save constraint fault and
fresh recovery, cancellation after the first write, and existing-data upgrade. Assertions
observe SQL state and fresh-scope results instead of merely testing native transaction APIs.
The existing fast model/artifact policies now inspect the version mapping and migration;
no extra framework-only cases or generic infrastructure were added. Earlier counts remain
historical.

## Subsequent E3.1 evidence

[E3.1](e3-1-http-actor-identity.md) passes 161 active cases, including the unchanged 42
PostgreSQL cases. Fifteen HTTP adapter cases exercise actor publication in the actual native
pipeline, including policy-selected schemes, anonymous/public behavior, mapping failure,
permissions, overlap and cancellation. Six sample cases exercise the real directory/error
composition and configured OIDC claim actions through protected cookies. Applying those
actions caught native issuer deletion that would otherwise break the sample's actor mapping.

These are adapter/consumer behavior proofs, not isolated tests that cookies or authorization
work. No null-argument matrix, reflection over private features, generic HTTP test framework
or restored graph infrastructure was introduced. Architecture grows from 15 to 24 through
new segment/consumer dependency rules and one direct native-XML adapter declaration case.
Remote provider login/callback/session topology remains outside these counts.

## Subsequent E3.2 evidence

[E3.2](e3-2-http-tenancy.md) passes 106 focused cases: 39 tenancy HTTP, 15 existing actor-only
HTTP, 17 sample composition and 35 architecture. These counts describe four freshly run
suites, not an all-project or PostgreSQL execution. The tenant suite references no actor,
sample or persistence project. Native requests expose publication and policy-ordering errors;
fault/cancellation assertions check no publication/business work and fresh-request recovery.
The direct public candidate check protects exact text; invalid base domains fail preset registration.

The sample's new cases protect Organization mapping, same-actor tenant selection, independently
expected 42/7 Inventory quantities, actual native host-filter composition and the capability
guard outside HTTP. Native filtering initially rejected the terminal-dot host; the consumer
allow-list now explicitly includes that supported form. Architecture gains one adapter
declaration case and ten forbidden dependency relationships. No descriptor snapshots, private
reflection, native-framework-only tests or custom graph infrastructure were added. Fixture
reads do not prove durable membership or database isolation.

The review refinement reuses those same 106 cases for registration-time route/subdomain
presets and an explicit native host-options utility. Async faults/cancellation now exercise
the candidate lookup/admission interface, while custom user selection retains the full-request
resolver. The 17 sample cases use native global exception handling and assert ProblemDetails
status/title/media type for known mappings. No extra framework-only tests were added.

## Subsequent E3.3 evidence

[E3.3](e3-3-persisted-access.md) freshly passes **212 cases**: 163 container-free cases and
49 persisted Access/HTTP cases on PostgreSQL. The sample's 17 existing composition cases now
use actual migrated data; 32 additional cases exercise the new consumer policies/model/setup.
The sample suite moves to container CI rather than preserving a parallel fixture data adapter.
Standalone HTTP suites and the 35 architecture cases remain unchanged.

The constraint proofs apply the consumer's actual migration and protect its identity keys,
current-membership uniqueness, required relationships and persisted statuses, then observe
correct admitted identity. They are not a general native database constraint matrix. Native
cookies, HTTP and SQL barriers expose our mapping/admission ordering, revocation/public
policy, fault/cancellation behavior and explicit setup. The cancellation barrier observes a
real waiting admission query through autocommit activity reads; no private reflection,
EF interception framework, production timing hook or custom architecture parser was added.
E2 storage results remain historical because its unchanged PostgreSQL suites were not rerun.

## E3.4 integration coverage

The HTTP sample replaces its fixture read with real tenant-owned rows while retaining useful
admission cases. Ten new cases exercise actual persisted values/changes, other-tenant SKU
absence, overlapping requests, protected admission before business queries, the explicit
public exception, real faults/cancellation and checkpoint-database compatibility. Two
existing setup/context cases gain one scenario each. Eight focused ArchUnitNET cases inspect
real populated module/host boundaries. No custom graph/parser, framework-detection test or
assertion-failure fixture is added. [E3.4](e3-4-persisted-business-ingress.md) records the
fresh 61 HTTP and 43 architecture results and remaining limits.

## E3.5 mutation increment

[E3.5](e3-5-profile-mutation.md) adds 31 meaningful HTTP/PostgreSQL cases and six actual-assembly
architecture cases. The HTTP suite now has 92 cases and architecture 49. Existing finite-setup
and no-runtime-migration cases gained Sales checks. Native token issuance, unchanged-principal
application-actor rebinding, tenant/customer/address isolation, input boundaries, admission
before unavailable Sales, stale and deliberately blocked competing updates, second-save
constraint failure and cancellation at a blocked address UPDATE exercise consumer obligations.
There are no tests of token cryptography/serialization or production pool internals.
The HTTP test host disables pooling because each disposable database otherwise retains idle
connections across the expanded suite; production composition is unchanged.

All ten active suites were rerun: 311 passed (134 PostgreSQL, 177 container-free), none skipped.
The report distinguishes those fresh results from historical E2/archive evidence.

## E3.6 runtime and telemetry increment

[E3.6](e3-6-runtime-composition.md) adds three cases, bringing all eleven active suites to
314 freshly passed cases (177 container-free, 137 container/runtime), none skipped.
Two telemetry cases run the actual ServiceDefaults around successful/failing catalog requests
and real PostgreSQL queries. Native in-memory exporters expose missing producer registration,
lost request/database trace relationships, uncorrelated failure logs and absent request
metrics. No custom OTLP parser, registration snapshot, private reflection or instrumentation
encoding tests are introduced.

One native Aspire/Kestrel/PostgreSQL case protects the consumer's explicit startup/setup
protocol: startup leaves schema absent, manual setup enables independently expected catalog
values, and a database outage makes readiness/business reads fail while liveness succeeds.
These steps form one capability journey; they do not maintain an exact AppHost annotation
snapshot. Tests choose ephemeral storage through ordinary configuration and randomize ports.
Inert OIDC settings are used only for public work and prove no provider/browser behavior.
Manual dashboard OTLP observations are separately reported and do not inflate test counts.

## E3.7 real OIDC/browser increment

[E3.7](e3-7-oidc-browser-journey.md) adds six cases: three real browser capability journeys
and three rejected finite-setup configurations with PostgreSQL mutation checks. Browser
tests exercise our native host composition, exact realm callback and application mapping,
not OIDC cryptography or Keycloak implementation details. They navigate provider forms and
callbacks, retain real cookies and use same-origin fetch without injected sessions/XHR
headers. Slow browser/container checks remain outside commit hooks.

Alpha covers two admitted tenants, native anonymous API status, antiforgery, a committed
profile edit and a stale version. Beta covers denied admission before deliberately
unavailable business persistence and fresh membership revocation with an unchanged session.
The unmapped user covers fail-closed mapping despite matching email and no automatic
account/link creation. The HTTP suite retains detailed isolation/race/cancellation matrices;
the browser suite adds the actual provider/browser boundary rather than duplicating them.

The first browser run caught missing native JSON endpoint metadata on two untyped GETs,
despite the other HTTP 401 cases passing. Native typed results correct the declaration;
the capability assertions remain the proof, with no metadata snapshot tests. The finite
setup cases observe process failure and absent module tables, rather than testing every
URI/null variation. Manual login/edit observations are separately reported. All eleven
active suites passed freshly: 320 cases (177 container-free, 143 container/runtime), none
failed/skipped. The slice report records those results and local HTTPS/browser-trust limits.

## E4 event codec increment

[E4](e4-event-serialization.md) adds 23 focused cases: 16 library and seven executable-consumer
cases. They protect explicit registration conflicts, exact runtime dispatch, unknown identity
versus invalid payload, configuration snapshots and propagation of non-JSON converter faults.
Literal consumer payloads prove CLR renaming, Inventory's absent optional receipt reference,
required/null policy and Purchasing's required constructor field through independently expected
stock quantity and order totals. The console itself is exercised; round trips alone are not
the compatibility evidence.

These tests protect decisions in our codec/recipes, rather than enumerating native JSON
options. No null-argument matrix, descriptor snapshot, private reflection, generic fixture
framework or restored-project reader was added. Two architecture cases extend the existing
declaration/dependency policies, bringing that suite to 51.

All nine container-free suites passed freshly: **202 cases**, none failed/skipped. E4 changes
no database/browser composition; the 143 container/runtime cases remain E3.7 historical
evidence and were not rerun. The new reusable registry/dispatch mechanism is proven within
the documented JSON boundary; event-stream persistence and complete domain validation remain
later capabilities.

## E5.1 ordered-range revision

[E5.1](e5-1-event-history.md) revises the initial complete-history object into a metadata-only
range validator. Its 16 cases protect exact ranges, missing tails/excess rows, nonzero starts,
empty ranges, timestamp regression/equality, invalid bounds, maximum-version arithmetic and
single enumeration. Snapshot and in-library selection tests were removed with those capabilities.
These cases protect our policies and traversal, not native record/array behavior.

Consumer tests use actual fixture envelopes to assert current/historical stock quantities
and line-replacement totals, repeated reconstruction without accumulated state, existing
codec errors and preservation of consumer domain exceptions. The consumer suite now has
16 cases, including rejection of invalid version selectors and proof that earlier selected
ranges do not certify later metadata. Both unselected payload failures and metadata regression
can remain outside an earlier read; selecting the damaged range then fails. The existing
console test asserts both actual history journeys.
No fake command/publisher counters or pending-event framework were added merely to test replay.

Two focused architecture cases bring that suite to 53. The four affected suites passed
freshly: **101 cases**, none failed/skipped (16 range, 16 combined consumer, 16 serialization,
53 architecture). The remaining context/database/browser suites retain earlier evidence and
were not rerun; this is not an all-active-suites result. Native build/style/analyzer checks
cover the active solution, while standalone build/run verifies adoption of just the two event
libraries. No custom graph parser, reflection descriptor snapshot or framework-option matrix
was introduced.
The earlier 99-case execution describes the superseded complete-history shape only.

## E5.2.1 native history reads

The new **39-case** PostgreSQL consumer suite exercises real module migrations and Contracts:
independently expected state/metadata, colliding tenant/stream identities, server-side SQL
selection, a commit after head capture, selected corruption, native query faults, migration
preservation and the actual executable. It reuses the range validator rather than repeating
E5.1's traversal matrix. Schema checks protect our configured owner/stream relationship,
position uniqueness and positive persisted values; they are not generic EF feature tests.

Native DbCommandInterceptor observes relevant predicates and coordinates one deterministic
commit before the event query. No production hook, timing sleep, SQL snapshot, custom generic
repository or fabricated publisher effect counter was added. Consumers have no publishing or
pending-event path in this read slice. Two existing HTTP assertions now compare all applied
Inventory migrations against the module's declared set instead of assuming a single migration.

Eight actual module/Contracts architecture cases bring that suite to 61. The combined codec
consumer remains independently adoptable. [The slice report](e5-2-1-native-event-history.md)
distinguishes fresh executions from prior library/browser evidence and the deferred writer
protocol. No new reusable mechanism was proven; existing validation/decoding now compose
through two native EF readers.

## E5.2.2 native event append

The append increment adds **43 cases** to the PostgreSQL consumer suite, bringing it to 82.
Cases protect module command staging and the native composition protocol: creation/batch
visibility, independently expected state, two prepared writers at one head, stale/foreign
preflight outcomes, invalid preparation without tracked changes, damaged history, repeated
staging before/after save, rollback of saved work and fresh-scope recovery. Header, first/later
envelope and second-save faults prove complete rollback of our staged participants.

Constraint classification uses real PostgreSQL failures: known owned position collisions are
conflicts; event-ID/check failures are faults. Competing creation exercises the real header key.
One catalog concurrency case prevents our helper from classifying unrelated module writes as
event conflicts. These are assertions about our error policy and append protocol, not generic
transaction, foreign-key or argument-validation demonstrations. Cancellation is deliberately
before commit dispatch; it makes no ambiguous-commit recovery claim.

The tests use native contexts/migrations and existing command observation. No fake store,
publisher counter, production hook or custom architecture infrastructure is added. Codec/range
matrices are reused rather than copied. [The report](e5-2-2-native-event-append.md) records
fresh results, remaining limitations and the finding that no new library mechanism was needed.

Owner review removed StagedStreamIds bookkeeping from both contexts. The two repeated-staging/
cancellation cases now use native tracked headers before and after save; they no longer clear
tracking to test preservation of an artificial lifecycle restriction. Case count is unchanged.

## E5.3 explicit event-sourcing model registration

The independent storage consumer adds **10 cases**: six real PostgreSQL/executable cases and
four focused connection-free model cases. The database cases protect our selected shared-table
identity, position uniqueness, restrictive relationship, version token and customization. They
check independently authored counter/note output, actual migrations and absence of per-family
tables. These assertions protect configuration supplied by the utility, not generic native EF
behavior. Complete writer/fault/rollback coverage is reused from the module suite rather than
copied into a second store implementation.

The four model cases protect key-shape validation: reject wrong header order, missing reference
ownership and missing event-key ownership; accept matching prefixes with different consumer
property names. They open no connection and do not add a general parser, cache-key factory or
in-memory mapping matrix. Four new native-XML/ArchUnitNET architecture cases protect dependency
declarations and actual library/consumer boundaries; existing module/Contracts rules also include
the new library. [The report](e5-3-event-storage-registration.md) records fresh results and limits.

## E6.1 inline decision state and required views

The PostgreSQL event suite adds **29 cases**, bringing it to 111: 26 focused inline-state cases
and three required-view write faults. Existing append assertions now observe committed views,
so tenant isolation, winning writes, two saves, rollback and cancellation do not require a
copied transaction matrix. Both aggregate families edit under actual denied history SELECT
permissions; explicit live reads still fail. Required-view damage/races, complete-candidate
arithmetic and independently expected summary replacement exercise our new consumer protocol.

Two existing damaged-history cases deliberately change assurance: an inline edit does not
certify the recorded prefix, while an explicit live read does. The tests no longer require an
incidental history query just to preserve the old loading implementation. No new architecture
infrastructure or generic EF behavior tests were added. [The report](e6-1-inline-decision-state.md)
records extraction comparison and limitations; no new reusable mechanism was proven.

The larger native suite exposed retained per-database idle connection pools in the shared
fixture. Disposable proof connections now set Pooling=false, preventing shared-container
connection exhaustion without changing production configuration. All five fixture consumers
were rerun successfully; the report separates their fresh results from historical evidence.
