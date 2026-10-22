# S1 Wholesale product consolidation

Date: 2026-10-22. Base: `c346a51`. Branch: `feat/v1-wholesale-consolidation`.
Scope: [selected consolidation map](../plans/s1-sample-experiment-consolidation.md#selected-wholesale-map).

Wholesale now has one [API](../../samples/Wholesale/Api/README.md), one
[worker/maintenance executable](../../samples/Wholesale/Worker/README.md), one
[product integration-test project](../../samples/Wholesale/IntegrationTests/README.md), and its
existing real runtime/browser suite. Access, Inventory, Sales and Purchasing retain separate
Contracts/implementation assemblies, contexts, schemas and migrations. No archived file changed.

## Move, merge and proof transfer

The plan's concrete table is the path/ownership map. All 69 original product test source files
remain, with every prior public test/helper method name retained. A mechanical pre/post check
compared each moved file to its base counterpart. No unique assertion was removed to make the
assembly consolidation pass:

| Previous test project | Retained Fact/Theory methods | Canonical capability folder |
| --- | --- | --- |
| HttpIdentityDemo.Tests | 89 | `IntegrationTests/Api` |
| WorkflowDemo.Tests | 38 | `IntegrationTests/Reservations/Broker` |
| LocalWorkflowDemo.Tests | 10 | `IntegrationTests/Reservations/Local` |
| DddDemo.Tests | 12 | `IntegrationTests/Domain` |
| RichAggregateDemo.Tests | 19 | `IntegrationTests/DraftOrders` |
| DomainEventsDemo.Tests | 19 | `IntegrationTests/Reactions` |
| CqrsDemo.Tests | 42 | `IntegrationTests/Queries` |
| EventPersistenceDemo.Tests | 73 | `IntegrationTests/EventPersistence` |

These are method/attribute counts, not expanded theory-case results or a substitute for running
the tests. The 65 existing module migration files and historical Access SQL fixture were compared
byte-for-byte against the base and remain unchanged. Literal event JSON stays with the independent
Events consumer and is linked into test output. Module `InternalsVisibleTo` names now grant the
single product test assembly; modules gain no dependency on it.

The five former finite console compositions/journeys moved to `IntegrationTests/Scenarios`.
They still call live modules; there is no copied business implementation. The product never
references these helpers. Two prior process assertions continue to launch a real test-owned
child process through `TestEntryPoint --scenario history|schema-evolution`, with a disposable
database and 60-second deadline. Normal discovery/execution delegates to the installed native
xUnit/MTP entry behavior. Stock regeneration instead launches the actual Worker assembly,
preserving process kill, SIGINT cancellation and repeated-command convergence proofs.

Broker and local delivery use one Worker assembly with explicit transport selection. Each original
role implementation keeps its startup checks, fresh scopes, native save/commit and credentials.
Local process fixtures explicitly set `Transport=local`; the local relay namespace still cannot
reference module implementation/persistence or broker APIs. Assembly consolidation necessarily
adds a broker reference to the Worker, so the earlier whole-assembly prohibition became explicit
checks over both the local host/composition namespace and relay namespace, with nonempty-type
assertions and concrete broker assemblies loaded. Peer-module, Contracts, library and host isolation checks remain.

## Actual product entry points

This is more than retiring old `Program.cs` files:

- The default API now supports creating private drafts, batch add/change/remove lines and
  submitting orders. Existing native authentication, Organization membership, actor-bound CSRF,
  late Edit authorization and module predicates apply. The creator receives the module's existing
  human Edit grant. Required allowance reaction failures return Conflict and roll back draft and
  allowance together. Finite full product setup seeds EUR 1000 allowances for sample customers.
- Worker `--role inventory` exposes open/receive/issue/read and `--role purchasing` exposes
  draft/change-lines/read through the existing command/query Contracts. Only the chosen module's
  context/credential is registered. Writes own their explicit native transaction, save and commit;
  stale/rejected operations do not save. Runtime does not create storage. Operator admission is
  trusted local OS/database authority, explicitly selected Organization and a system actor.
- `--regenerate-stock` retains actual Inventory maintenance progress/cancellation/outcome handling.
  Full API setup now applies Purchasing's migrations, so the canonical product database supports
  the new operator commands without running an old slice executable.

The optional restricted Sales HTTP RLS composition retains its previous existing-draft/profile
scope. New create/submit routes are not registered there, and its role gains no grant/allowance
authority. Batch edits use the existing permitted tables. This explicit smaller composition is
tested and documented; there is no silent fallback to owner credentials or disabled RLS.
Sales reply/outbox restricted workers retain their stronger separate credential/admission guards.

## Verification and integration repairs

Fresh verification uses .NET SDK 10.0.401, real PostgreSQL 18.6, RabbitMQ 4.3.6 and local rootless
Podman:

- Complete consolidated product/provider/process suite: **696/696 passed**, no skips, 3m 55.390s.
  This is expanded theory-case execution; the separate table above counts 302 inherited
  Fact/Theory methods. Four new expanded cases cover the product lifecycle, restricted-route
  boundary and two native module-operations processes.
- Real Aspire/PostgreSQL/Keycloak/Chromium runtime and browser suite: **6/6 passed**, 5m 15.515s.
- Architecture suite: **101/101 passed**, 7.241s, including actual module and endpoint/local
  namespace dependencies. Both local host/composition and relay namespaces reject broker APIs.
- Full 78-project solution build, plus focused product/architecture/runtime builds: zero warnings
  or errors. Full restore succeeded with vulnerability checking enabled and network access.
- CSharpier: 154 files checked; .NET style and analyzers over affected product/architecture paths
  passed. `git diff --check` passed.
- Active changed-document links: zero missing targets. All 15 affected project owners' references,
  78 unique solution entries and CI project invocations resolve. Node 24's glob-matching smoke
  checked canonical hosts/tests, linked event fixtures/process helper, shared project changes,
  markdown exclusions and conservative unknown-input fallback. This is a local selection check,
  not a claim that GitHub Actions ran.
- Archive verifier: all 800 original files at its pinned revision verified. Separate pre/post
  comparison confirmed the 65 migration/Access-fixture files listed above remain byte-identical.

The first consolidated provider run exposed two real integration problems and was stopped before
completion: old executable assertions launched the newly merged test assembly without a scenario
selector, and native OTel exporter lists observed other classes' process-wide activity. The test-owned
entry point fixes process ownership without losing real-process assertions. A subsequent full run
passed 695/696 cases; its sole failure was the new restricted-mode test expecting HTTP 405 where
the native tenant middleware rejects the generated unmatched route with 400. The final assertion
checks actual absence of lifecycle POST endpoints and unchanged persisted rows, then that native
response. The final complete rerun passed all 696. The HTTP and inbox
telemetry classes now share an explicitly nonparallel collection; all other classes retain normal
scheduling. Initial failed/stopped runs are not reported as successful verification.

Relevant logs are `/tmp/s1-build.log`, `/tmp/s1-architecture-build.log`,
`/tmp/s1-architecture-tests.log`, `/tmp/s1-integration-tests.log`, `/tmp/s1-runtime-build.log`,
`/tmp/s1-runtime-tests.log`, `/tmp/s1-solution-build.log`, `/tmp/s1-solution-restore.log`,
`/tmp/s1-style.log`, `/tmp/s1-analyzers.log` and `/tmp/s1-ci-scope.log`.
Unprivileged full restore initially failed fetching NuGet vulnerability data (NU1900); unprivileged
style failed creating its MSBuild workspace host. Network/IPC escalation resolved both without
disabling checks. Socket/container execution also used local escalation. No push/deployment occurred.

## Extraction assessment and remaining coordinated work

**No new reusable Gloaming mechanism was proven.** The existing runner, module Contracts, native
EF save/commit, required reactions, inbox/outbox and context adapters implement the product. The
small API transaction owner and Worker command switch are consumer composition, rather than a
library module runtime, generic command framework or deployment coordinator. Product setup,
operator trust, route policy, sample allowance values and presentation remain consumer-owned.

Template finding: canonical sources are now suitable inputs for the separately coordinated T2
rehearsal; test-owned scenarios are not template product code. Library finding: independent family
examples remain beside their families and acquire no Wholesale dependency. Sample finding: existing
business mechanisms have canonical API/worker entry points and one preserved proof owner.

The concurrent B2 checkpoint is based separately and must be integrated after this checkpoint,
folding Directory runtime roles into Worker and Directory proofs into IntegrationTests, retaining
its migrations/fixtures and actual dispatch route. Root coordinates that merge, T2/P1, final
independent code/isolation reviews and release verification. No archive deletion is included;
S1's overall archive/template/integrated-review closure is not claimed by this Wholesale checkpoint.

Canonical Directory adoption subsequently folds the temporary B2 host/tests into Worker and IntegrationTests. The [adoption report](b2-directory-product-adoption.md) records new executable and route-claim mechanism evidence separately from this checkpoint.
