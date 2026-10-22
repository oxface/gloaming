# Development and verification

Library projects and their tests live under `src/Gloaming.{Family}/`; each family has
a README and local capability documentation. Repository architecture tests and shared support
remain under `tests/`. The active solution, hooks and CI use the family paths below.
See [the layout scope](plans/library-family-layout.md).


`ModulithFoundry.slnx` contains the active projects: independent ActorIdentity and Tenancy
cores, their optional ASP.NET Core adapters, the EF ownership utility, the event codec and
ordered-range validation, package-free aggregate core and EF event-storage utilities, five finite console
samples, an HTTP identity/Organization host, eight populated Access/Inventory/Sales/Purchasing module projects and
their proof suites, an Aspire AppHost, sample ServiceDefaults and a runtime composition suite.
Messaging adds provider-free contracts, native EF outbox/worker, PostgreSQL dispatch and
an independent state-stored HTTP consumer, with library/adoption tests. Inventory's explicit
outbox journey also exercises native RabbitMQ; broker dependencies remain sample/test-only.
The archived solution is independent. Root build defaults target .NET 10; central package
management pins test/DI packages, EF Core Relational/Design, native Npgsql, Testcontainers,
sample-only OpenIdConnect, test-only TestHost and ArchUnitNET. The local tool manifest also
pins native `dotnet-ef` 10.0.12.

## Local .NET SDK

Use the SDK selected by root `global.json`; the template has a matching SDK manifest.
Ubuntu's apt feed can provide a different feature band: the repository currently
requires 10.0.401, while the inspected Ubuntu 26.04 feed offers 10.0.112. The
`latestPatch` policy stays within the selected feature band, so 10.0.112 cannot
satisfy a 10.0.401 request. Keep the pin rather than rolling it back for local
package availability. CI's `actions/setup-dotnet` installs from `global.json`
independently of the Ubuntu package feed.

For a per-user installation when apt does not provide the required SDK, run from
the repository root:

```bash
curl -fsSL https://dot.net/v1/dotnet-install.sh -o /tmp/dotnet-install.sh
bash /tmp/dotnet-install.sh --jsonfile global.json --install-dir "$HOME/.dotnet" --no-path
export DOTNET_ROOT="$HOME/.dotnet"
export PATH="$DOTNET_ROOT:$PATH:$DOTNET_ROOT/tools"
dotnet --version
```

Persist the environment in your shell configuration. Per-user SDKs coexist with
apt's installation, but selecting the per-user `dotnet` host does not automatically
discover SDKs under `/usr/lib/dotnet`; install any older SDKs required by other
repositories into the same per-user directory. Repeat installation when a reviewed SDK update changes
the manifest. See [Microsoft's installer reference](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-install-script).

## Repository tooling

Follow [the .NET conventions](conventions/dotnet.md) when changing C# code.

```bash
dotnet tool restore
npm ci --prefix tools/repository
python3 tools/repository/verify-archive.py
dotnet csharpier check . --include-generated
```

Root formatting excludes `archive/` through `.csharpierignore`; archived formatting is
verified separately against its original configuration. CSharpier owns C#/XML layout.
Semantic style and analyzer checks operate on a concrete solution.

Lefthook checks root formatting, active style/analyzers, architecture, context, EF models,
event codecs/history and the PostgreSQL rebuilding suite. Restore the active solution
before using hooks. Other container suites run through CI or explicit local commands;
archived suites are excluded from hooks.

## CI lanes

The [main workflow](../.github/workflows/ci.yml) exposes seven separately named Gloaming
family checks, plus Wholesale consumer composition and repository integrity. Workflows
start on PRs/main pushes, but expensive jobs run only for relevant changed paths. Family
selection uses SHA-pinned `dorny/paths-filter` and the explicit
[lane input map](../.github/ci-paths.yml), including dependent consumers and linked fixtures.
Update that map when project references or generator inputs change; no custom project parser
or selection script is maintained.
Unselected jobs are reported as skipped without allocating their runners. Each selected
test restores/builds its project and dependencies; no job consumes another job's build outputs.

Independent family jobs run concurrently. Inbox PostgreSQL proofs share one assembly-level
container and allocate a separate database per test, with at most two tests running at once.
This explicit scheduling policy does not apply to other suites. Outbox keeps its collection
fixture: measured parallel candidates did not establish a speed improvement.

| Check | Responsibility |
| --- | --- |
| Gloaming.Auditing | Explicit transactional accepted-change audit and PostgreSQL mapping proofs. |
| Gloaming.ActorIdentity | Core identity and native HTTP adapter proofs. |
| Gloaming.Tenancy | Core tenant context and independent HTTP adapter proofs. |
| Gloaming.Persistence | EF model/write validation and real PostgreSQL GUID ownership consumer. |
| Gloaming.Events | Serialization/upcasting, ordered history integrity and independent event-codec consumer tests/executable. |
| Gloaming.EventSourcing | Aggregate core, PostgreSQL history/rebuilding/concurrency, independent event-storage consumer and Wholesale event-sourcing adoption. |
| Gloaming.Messaging | Provider-free envelopes, PostgreSQL enqueue/claims/inbox processing, HTTP/RabbitMQ adopters, real separate-worker process proofs and focused Inventory broker proofs. |
| Wholesale consumer composition | Context tests/executable, module migrations/ownership and persisted HTTP admission/business/telemetry tests. |
| Repository integrity | Always: commitlint and frozen archive checksums. Relevant code/build changes also run whole-solution restore/style/analyzers/build, architecture boundaries and CSharpier. |
| Template integrity and adoption | TypeScript generator checks and two external generated consumers against PostgreSQL. |
| Wholesale runtime | Full Aspire setup/readiness/outage and real Keycloak/Chromium journeys on relevant PRs or manual dispatch. |

Test jobs have a 15-minute limit, including setup/build; scope selection has a five-minute
limit. All three workflows use the same [filter workflow](../.github/workflows/changes.yml)
and path map. The action reads PR changes through GitHub's paginated API and push changes
through Git. Markdown-only changes, including
library-local and sample READMEs, omit .NET, database, template and browser execution.
Shared build/toolchain, project-graph and CI-tooling changes select all checks; unknown
inputs outside mapped roots also select full coverage. A detection/configuration failure
fails required Repository integrity rather than silently skipping verification.

Template adoption runs for generator/template changes and the four selected library source
snapshots it copies. Wholesale runtime runs for Wholesale implementation or shared inputs
on PRs; it still has no post-merge push run. All workflows support manual full verification
and cancel superseded runs on the same PR/ref. Scope selection still incurs lightweight
runner/setup work in each workflow; no exact hosted duration/minute reduction is claimed.
Whole-solution validation remains centralized. Wholesale broker class filters
assign the three focused RabbitMQ tests to Messaging; the remaining event tests stay in
EventSourcing, including state/events/outbox composition. Neither lane revives archived suites.

Existing family, composition, repository and template check names remain suitable for branch
protection: conditional jobs report skipped instead of leaving a workflow-level required
check pending. Repository integrity fails if its selector job fails. The runtime check can
remain optional under the existing ruleset. This scheduling policy preserves deployment
assertions when selected. See the [path-scoping report](reports/ci-path-scoping.md) for new
filter verification and the
[family-lane verification report](reports/ci-family-lanes.md) for coverage accounting,
fresh executions and remaining hosted/performance limits.

## Separate worker proofs

The Messaging family includes [W1's native separate-host proofs](../src/Gloaming.Messaging/samples/MessagingWorkerDemo/README.md).
They launch actual producer and worker executables against isolated PostgreSQL/RabbitMQ, with
API-independent delivery, competing workers, process termination and recovery. They use native
Testcontainers fixtures and do not add another Aspire deployment lane. Update the family path
map when changing their executable dependencies or linked fixtures.

## Dependency updates

[Dependabot configuration](../.github/dependabot.yml) checks every Monday at 09:00 UTC.
It covers the active solution and local .NET tools, the independent state-stored template,
both npm tooling directories, SHA-pinned GitHub Actions and both pinned .NET SDK manifests.
The frozen archive is excluded from update scans. Add a separate frontend npm entry when
a frontend exists; repository/template tooling is not a frontend dependency group.

Routine NuGet, npm and SDK releases wait seven days after publication; major releases wait
30 days. Actions uses 30 days for every release because its ecosystem supports only a
single cooldown. These delays govern bot proposals, not all package installs or transitive
dependencies, and do not certify release safety. Security updates bypass cooldowns.
See [GitHub's options reference](https://docs.github.com/en/code-security/reference/supply-chain-security/dependabot-options-reference).

Minor/patch EF/provider/tool updates, .NET platform packages, Aspire, OpenTelemetry and
Testcontainers each have their own group. Other NuGet dependencies and all NuGet majors
use one dependency per PR across the root/template manifests. Each npm tool directory has
its own minor/patch group; Actions minor/patch updates share a CI group. Their major updates
remain individual. An SDK upgrade is separate from package updates and updates both SDK
manifests together. Groups express review boundaries, not compatibility guarantees; check
release notes, provider/framework compatibility and transitive lockfile changes. Existing
preview dependencies, such as the sample's Keycloak integration, still need explicit review.

Template tooling supports Node 24; its `@types/node` updates exclude versions 25 and above.
Keep 24.x minor/patch updates eligible. Remove that version boundary when deliberately
upgrading the tooling's runtime, engines and CI together. Version-range exclusions also
limit security-update candidates; a fix available only outside the supported range requires
reviewing the runtime upgrade rather than silently changing the type definitions.

Each update configuration permits three open routine PRs, not three across the repository.
Security fixes remain individual and separate from routine batches. Do not enable broad
repository-level grouped security updates if that separation is to be preserved. If a routine
group fails and the cause is unclear, split the group so healthy updates can proceed.
Require normal owner review and CI; this setup adds no automatic merging or ruleset bypass.
Use conventional `chore(deps)` / `chore(deps-dev)` bot commit messages.

After merging the configuration into the default branch, confirm dependency graph,
Dependabot alerts and Dependabot security updates are enabled under GitHub repository
Settings → Advanced Security. The YAML configures version updates; it does not turn on
those repository security settings. See
[security-update setup](https://docs.github.com/en/code-security/how-tos/secure-your-supply-chain/secure-your-dependencies/configure-security-updates).
Confirm the first hosted run discovers both solutions/tools and both SDK manifests, opens
the intended groups and passes the existing PR checks. Central packages, root SDK/tools
and Actions updates select broad CI coverage; template-only tooling changes select template
checks. Versions pinned inside scripts or container image strings are not covered by this
initial configuration and need explicit review when their corresponding dependency changes.

## Template creation and external-consumer proof

T1 was owner-approved and checkpointed as `8ccf4c8`. On the supported Linux/glibc environment
with Node.js 24.21+ (24.x), npm and the pinned .NET SDK:

```bash
npm ci --prefix tools/template --ignore-scripts
npm --prefix tools/template run check
npm --prefix tools/template run create -- --config example.json --output /tmp/Cedar
```

The npm script resolves configuration relative to `tools/template`; output must not exist.
See [the creator guide](../tools/template/README.md) for the exact configuration, platform
requirements and failure behavior. The output is independent of the Foundry checkout and
contains no required event/messaging setup. Its README documents build, explicit migration
and the adoption journey.

For the complete creation/adoption proof, set `CATALOG_TEST_ADMIN_CONNECTION_STRING` to a
disposable PostgreSQL 18.6 server with permission to create/drop databases, then run:

```bash
npm --prefix tools/template run verify
```

The [separate CI workflow](../.github/workflows/template.yml) supplies PostgreSQL and runs
tooling checks and this proof. It covers two names/namespaces, omission, deterministic
creation, refusal/races, conflicting parent SDK configuration and real generated-consumer
journeys outside the checkout. [The checkpoint report](reports/t1-template-rehearsal.md)
distinguishes those executions from earlier foundation results. These instructions add no
new execution evidence.

## Container-free verification

Run from the repository root:

```bash
dotnet restore ModulithFoundry.slnx
dotnet csharpier check . --include-generated
dotnet format style ModulithFoundry.slnx --verify-no-changes --no-restore
dotnet format analyzers ModulithFoundry.slnx --verify-no-changes --no-restore
dotnet build ModulithFoundry.slnx --no-restore
dotnet test --project tests/ArchitectureTests/ArchitectureTests.csproj --no-build --no-restore
dotnet test --project src/Gloaming.ActorIdentity/tests/ActorIdentityTests/ActorIdentityTests.csproj --no-build --no-restore
dotnet test --project src/Gloaming.ActorIdentity/tests/ActorIdentityAspNetCoreTests/ActorIdentityAspNetCoreTests.csproj --no-build --no-restore
dotnet test --project src/Gloaming.Tenancy/tests/TenancyAspNetCoreTests/TenancyAspNetCoreTests.csproj --no-build --no-restore
dotnet test --project src/Gloaming.Tenancy/tests/TenantTests/TenantTests.csproj --no-build --no-restore
dotnet test --project samples/Wholesale/ContextDemo.Tests/ContextDemo.Tests.csproj --no-build --no-restore
dotnet test --project src/Gloaming.Persistence/tests/EntityFrameworkCoreTests/EntityFrameworkCoreTests.csproj --no-build --no-restore
dotnet test --project src/Gloaming.Events/tests/EventSerializationTests/EventSerializationTests.csproj --no-build --no-restore
dotnet test --project src/Gloaming.Events/tests/EventHistoryTests/EventHistoryTests.csproj --no-build --no-restore
dotnet test --project src/Gloaming.EventSourcing/tests/EventSourcingTests/EventSourcingTests.csproj --no-build --no-restore
dotnet test --project samples/Wholesale/EventCodecDemo.Tests/EventCodecDemo.Tests.csproj --no-build --no-restore
dotnet run --project samples/Wholesale/ContextDemo/ContextDemo.csproj --no-build --no-restore
dotnet run --project samples/Wholesale/EventCodecDemo/EventCodecDemo.csproj --no-build --no-restore
```

These checks require no containers, identity provider or personal credentials. The active
architecture suite uses ArchUnitNET for compiled type dependencies. Declaration tests read
copied project files with native XML APIs: foundation cores, event codec/history and aggregate
core allow no package/project/extra-framework references. EF ownership allows only its native
EF Relational package; EF event storage additionally references the aggregate core and DI
abstractions for its optional scoped aggregate registration helper. HTTP
adapters permit their corresponding core and the native ASP.NET Core framework. The independent
event adopter declares just aggregate core/EF storage plus its native provider/design packages.
No restored-graph parser or exact transitive-package whitelist is maintained. These checks
inspect direct declarations, not evaluated MSBuild imports or transitive dependencies.

Two container-free sample policies also inspect actual Inventory/Sales models and native
migration operations for module-owned schemas, explicit ownership/global classification,
Sales's tenant-bearing customer/address relationship, explicit customer version token and
snapshot/model consistency.
They build design-time contexts without connecting to a database.

Standalone adoption is exercised by the real actor-only, tenancy-only and GUID EF consumers;
ordinary restore/build and their behavior tests remain part of CI. Sample project/package
graphs are editable composition rather than exact test snapshots. Architecture policies are
repository-owned; consumers select their own module structure.

CI distributes these commands among the owning family, Wholesale composition and repository
integrity jobs described above. The local command list remains a convenient container-free
verification pass. See [the test audit](reports/test-audit.md),
[architecture checks](reports/architecture-tests.md), [the E1 split report](reports/e1-identity-split.md)
and [the E2.1 report](reports/e2-1-tenant-ownership.md).

The two standalone HTTP suites run native requests without a remote identity provider,
database or personal credentials. The standalone
HTTP suites use a test-only secondary scheme to prove effective scheme selection; the executable
sample has no header authentication. Actual OIDC login/callback/session topology remains
unproven here. The executable HTTP sample now requires PostgreSQL-backed Access, Inventory and Sales; initialize
its disposable demonstration data explicitly before running it with provider configuration using
[the sample guide](../samples/Wholesale/HttpIdentityDemo/README.md); fresh results and limits
are in [the E3.1 report](reports/e3-1-http-actor-identity.md) and
[the E3.2 report](reports/e3-2-http-tenancy.md). Tenancy-only proofs add requirements,
admission failures, cancellation, route/subdomain/custom selection and scoped publication.
The sample composes both adapters through guarded Inventory reads, while actor-only adoption
continues through its independent suite. Those E3.2 fixture results remain historical;
[E3.3](reports/e3-3-persisted-access.md) records persisted Access admission proofs.
[E3.4](reports/e3-4-persisted-business-ingress.md) connects admitted requests to tenant-owned
catalog rows and separate module/Contracts projects. Focused ArchUnitNET rules enforce
module/host business-call boundaries without a restored-project graph.
[E3.5](reports/e3-5-profile-mutation.md) adds cookie JSON protection, actor-bound native
antiforgery, tenant-owned Sales profile changes and explicit versioned transactions.

## Standalone event serialization and history

The sixth technical library, `Gloaming.Events.Serialization`, is independently
adoptable. Its finite [two-family consumer](../samples/Wholesale/EventCodecDemo/README.md)
now composes it with the seventh library, `Gloaming.Events.History`, through two project
references. Each library remains package-free and independent of the other. Build and run
the consumer directly:

```bash
dotnet build samples/Wholesale/EventCodecDemo/EventCodecDemo.csproj
dotnet run --project samples/Wholesale/EventCodecDemo/EventCodecDemo.csproj --no-build --no-restore
```

Inventory and Purchasing explicitly register their durable aliases and choose their own
native JSON contracts. The library and consumer suites above protect exact dispatch,
configuration snapshots, failure classification and literal payload compatibility with
independently expected quantities/totals. They require no database, HTTP host, identity
provider or other Foundry segment. See [the E4 report](reports/e4-event-serialization.md)
for fresh results and the boundary before stream persistence. Existing database/browser
results remain separate evidence.

[E5.1](reports/e5-1-event-history.md) validates consumer-selected ordered ranges without
copying rows or retaining a complete-history object. The consumer selects/materializes a
version/recorded-time prefix, validates metadata, then decodes and folds its selected events.
The focused range suite uses positions/timestamps with no payload generic or serialization
dependency; the combined executable uses its own row shape and native JSON envelopes.
Current versus earlier quantities/totals, equal-time inclusion and before-first absence are
observable in its output. Stream heads/timestamps are authored demonstration metadata; real
database capture and append/transaction proofs remain E5.2.
Reassess the final package division with those real native EF consumers; the validator
currently certifies only the returned range, not excluded history.

## PostgreSQL verification

ES1's reviewed [append interface](plans/es1-bounded-event-append.md) is exercised by both event
test projects below. The [Wholesale executable](../samples/Wholesale/EventPersistenceDemo/README.md)
adds a stock-issue acceptance/rejection journey through its module Contract; the
[independent storage executable](../src/Gloaming.EventSourcing/samples/EventStorageDemo/README.md) adds a bounded counter
using captured history/direct JSON. Both visibly own native transactions/saves/commits, and
both retain their existing authored fixtures/output. No migration or template option is added.
The provided IEventStore implementation now concentrates loading/version/required-state
coordination; explicit model declarations and native save guards enforce tracked participation.
See [the store report](reports/es1-library-write-store.md) for fresh executions and limits,
and [the earlier ES1 report](reports/es1-bounded-event-append.md) for prior appender results.

After restoring and building the active solution, run with a reachable Docker-compatible
container engine. Tests start disposable PostgreSQL 18.6 instances and keep the resource
reaper enabled; no application process or personal database credentials are required.

```bash
dotnet test --project src/Gloaming.Persistence/tests/PersistenceTests/PersistenceTests.csproj --no-build --no-restore
dotnet test --project samples/Wholesale/PersistenceDemo.Tests/PersistenceDemo.Tests.csproj --no-build --no-restore
dotnet test --project samples/Wholesale/HttpIdentityDemo.Tests/HttpIdentityDemo.Tests.csproj --no-build --no-restore
dotnet test --project samples/Wholesale/EventPersistenceDemo.Tests/EventPersistenceDemo.Tests.csproj --no-build --no-restore
dotnet test --project src/Gloaming.EventSourcing/samples/EventStorageDemo.Tests/EventStorageDemo.Tests.csproj --no-build --no-restore
dotnet test --project src/Gloaming.EventSourcing/tests/EventSourcingPostgresTests/EventSourcingPostgresTests.csproj --no-build --no-restore
```

For this repository's rootless Podman setup, prefix each command with:

```bash
DOCKER_HOST=unix:///run/user/1000/podman/podman.sock \
TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE=/run/user/1000/podman/podman.sock \
DOTNET_PROCESSOR_COUNT=4
```

Use your actual user socket path when it differs. The console smoke proof starts its own
finite child process from the source checkout and passes the disposable connection through
its environment. The sample executable must have been built in the same configuration.
CI runs these suites in Gloaming.Persistence, Gloaming.EventSourcing and Wholesale consumer
composition according to ownership. Per-case databases disable
connection pooling so their idle pools do not exhaust the shared container; production connection
configuration is unaffected. The event-history suite
launches the built native executable and coordinates captured-head races through EF command
interception, with no production test hook. It also exercises module command staging,
expected-version conflicts and caller-owned rollback on actual PostgreSQL; see
[the append findings](reports/e5-2-2-native-event-append.md). The HTTP sample uses
native protected cookies and actual Access migrations, without contacting an identity
provider. Its suite moved out of container-free CI/hooks rather than retaining fake directory
implementations; both independently adoptable HTTP library suites remain container-free. See
[the E2.1 report](reports/e2-1-tenant-ownership.md),
[the E2.2 report](reports/e2-2-module-migrations.md),
[the E2.3 relationship report](reports/e2-3-tenant-relationships.md),
[the E2.4 profile-change report](reports/e2-4-versioned-profile-changes.md) and
[sample run instructions](../samples/Wholesale/PersistenceDemo/README.md), plus
[the E3.3 Access report](reports/e3-3-persisted-access.md) and
[HTTP/database setup](../samples/Wholesale/HttpIdentityDemo/README.md), and
[the native event-history consumer](../samples/Wholesale/EventPersistenceDemo/README.md).
The [independent storage consumer](../src/Gloaming.EventSourcing/samples/EventStorageDemo/README.md) uses the mapping
segment alone with custom table names and mixed stream types; its tests use actual migrations.

## Wholesale runtime verification

### Closed integration-feed assessment

The [B1 assessment](reports/b1-retention-and-contract-assessment.md) retains research and
dated results only. Experiment code, tests, migration, snapshot and patch artifacts are
removed. There is no feed suite to run; common Wholesale setup and profile writes have
no feed dependency. Revisit implementation only for a concrete receiver requirement.

### Existing host topology

The [runtime guide](../samples/Wholesale/AppHost/README.md) documents required parameters,
native start/wait/setup/stop commands, dynamic endpoints and retained local data. The graph
starts PostgreSQL and the API; migrations/seeding happen only when you explicitly start
`demo-setup`. A fresh graph is live but unready until that command succeeds. ServiceDefaults
provides native request/database traces, logs and metrics, with OTLP export when configured.
It remains editable host source and is not required by any technical library.

After restoring/building the active solution, run:

```bash
aspire_version="$(DOTNET_NOLOGO=true dotnet msbuild samples/Wholesale/AppHost/Wholesale.AppHost.csproj -getProperty:AspireHostingSDKVersion -nologo)"
dotnet tool install --global Aspire.Cli --version "$aspire_version"
# On Linux, configure trust before running dev-certs, retaining OpenSSL's system roots.
export SSL_CERT_DIR="$HOME/.aspnet/dev-certs/trust:${SSL_CERT_DIR:-/etc/ssl/certs}"
dotnet dev-certs https --trust
dotnet dev-certs https --check --trust
pwsh samples/Wholesale/RuntimeComposition.Tests/bin/Debug/net10.0/playwright.ps1 install --with-deps chromium
dotnet test --project samples/Wholesale/RuntimeComposition.Tests/RuntimeComposition.Tests.csproj --no-build --no-restore
```

Use an existing matching CLI instead of reinstalling it. This suite uses native
Aspire.Hosting.Testing, real Kestrel and PostgreSQL, ephemeral storage and randomized ports.
It requires a Docker-compatible engine, a trusted native development certificate and Chromium matching
the pinned test-only Playwright package, but no running AppHost/provider or personal credentials.
The provider-free runtime client uses HTTP; Aspire's native API health check uses HTTPS.
Three optional-provider browser journeys use
actual HTTPS login/callback, browser cookies and same-origin fetch. They explicitly ignore
browser development-certificate trust errors; native API backchannel validation stays enabled.
The runtime test assembly runs sequentially: concurrent Aspire graph startup/teardown can
change the machine's container networks while another test is navigating in Chromium.
Each test still creates and disposes its own graph. Authentication assertions, test deadlines
and failure reporting remain in place; failed browser journeys are not automatically retried.
The runtime CI job restores/builds this test project and its native project references,
including the AppHost and HTTP API, rather than the whole solution. Repository integrity
continues to check the complete active solution independently.
On Linux without PowerShell, the package's bundled `.playwright/node/linux-x64/node` can run
its `.playwright/package/cli.js install chromium` from the same build output; no Node workspace
is required. CI uses the generated PowerShell installer with system dependencies.
Certificate creation alone does not establish trust: Aspire otherwise leaves Keycloak on
HTTP and HTTPS health checks may fail. CI verifies trust before launching any test resources.
For rootless Podman, set `ASPIRE_CONTAINER_RUNTIME=podman`,
`DOCKER_HOST` to your user socket and optionally `DOTNET_PROCESSOR_COUNT=4`.
Keep this container test outside commit hooks. The separate
[Sample runtime workflow](../.github/workflows/sample-runtime.yml) runs on PRs
changing Wholesale implementation or shared build/toolchain/CI inputs. Markdown changes
under Wholesale are omitted by the shared job selector.
It also supports manual dispatch from GitHub Actions. Library-only changes run their focused
contract, PostgreSQL and HTTP consumer proofs; they do not automatically run the complete
sample deployment. Manually dispatch this workflow when a library change warrants testing
the full sample composition. External provider, proxy/subdomain and production trust/session
guarantees remain outside these local proofs.

The workflows now start on every PR and conditionally skip unrelated jobs, so existing
required check names still receive a result. Keep Repository integrity required so selection
failures cannot bypass it. Workflow-level path exclusions would leave absent required
checks pending; see
[GitHub's path-filter behavior](https://docs.github.com/en/actions/how-tos/write-workflows/choose-when-workflows-run/trigger-a-workflow#using-filters-to-target-specific-paths-for-pull-request-or-push-events).
This scheduling policy preserves the sample suite and its assertions.

## Single-stream rebuilding proofs

Family-local tests exercise native stamp conflicts between writers and independent rebuilders,
aggregate-state-only maintenance saves, cancellation, rollback and fresh recovery on PostgreSQL
18.6. Wholesale exercises both module maintenance Contracts and the executable journey. A local
supported Docker/Podman runtime is required; absence is a failure, not a skipped proof.
CI's Gloaming.EventSourcing lane and pre-commit's event-rebuilding-postgres-tests job run this suite.

~~~sh
dotnet test --project src/Gloaming.EventSourcing/tests/EventSourcingPostgresTests/EventSourcingPostgresTests.csproj --no-build --no-restore
dotnet test --project samples/Wholesale/EventPersistenceDemo.Tests/EventPersistenceDemo.Tests.csproj --no-build --no-restore
~~~

For rootless Podman set DOCKER_HOST=unix:///run/user/1000/podman/podman.sock and
TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE=/run/user/1000/podman/podman.sock.
The retained TypeScript diagnostic
[node proof](../src/Gloaming.EventSourcing/docs/proofs/es2-coordination.ts) uses Node
24.21+ and rootless Podman to replay **historical gate design** observations in a disposable
container. It is not a current library regression test and supplies no stamp-concurrency claim.
Current executable coverage lives in the family/consumer test projects above. See
[the supported EF contract](../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/README.md).

## Explicit aggregate regeneration

ES3's separate [stock-position regeneration command](../samples/Wholesale/EventPersistenceDemo/aggregate-regeneration.md)
runs against an existing migrated database. Its progress, native-conflict, cancellation and
process-restart proofs are `StockRegenerationTests` in `EventPersistenceDemo.Tests`, included
in the existing EventSourcing CI lane. The guide records the invocation and test commands.

## DDD sample decisions

[DddDemo](../samples/Wholesale/DddDemo/README.md) is a finite comparison of state-stored Sales
profiles and event-sourced Purchasing orders. It requires a fresh disposable PostgreSQL database,
applies existing module migrations and runs without a broker or HTTP host:

```sh
WHOLESALE_DEMO_CONNECTION_STRING='Host=localhost;Database=ddd_demo;Username=postgres;Password=local' \
  dotnet run --project samples/Wholesale/DddDemo/DddDemo.csproj
dotnet test --project samples/Wholesale/DddDemo.Tests/DddDemo.Tests.csproj
```

The tests create disposable PostgreSQL 18.6 databases through Testcontainers. For rootless Podman
use the socket environment above. [The D1 report](reports/d1-ddd-sample-patterns.md) separates
new domain/consumer proofs, existing regression checks and the decision to retain sample patterns
without extracting another library or changing T1's template.

## Required local reactions and independent adoption

[DomainEventsDemo](../samples/Wholesale/DomainEventsDemo/README.md) submits Sales drafts and reserves
customer allowance in one native transaction. Its domain facts are ordinary records. The module
registers EventReactions and shared EF capabilities; commands use an operation sink and save natively.

```sh
WHOLESALE_DEMO_CONNECTION_STRING='Host=localhost;Database=domain_events_demo;Username=postgres;Password=local' \
  dotnet run --project samples/Wholesale/DomainEventsDemo/DomainEventsDemo.csproj
dotnet test --project samples/Wholesale/DomainEventsDemo.Tests/DomainEventsDemo.Tests.csproj
```

The independent [EventReactions consumer](../src/Gloaming.EventReactions/samples/EventReactionsDemo/README.md) demonstrates a
consumer-owned base with composed buffers, a package-free immutable model and scoped request sources.
Source selectors are explicit. No event marker, source interface or library aggregate base is required.

```sh
dotnet test --project src/Gloaming.EventReactions/tests/EventReactionsTests/EventReactionsTests.csproj
dotnet test --project src/Gloaming.EventReactions/samples/EventReactionsDemo/Consumer.Tests/Consumer.Tests.csproj
```

The PostgreSQL/Testcontainers proofs cover ordered capture, selected/deleted sources, downstream
waves, all native save overloads, concurrency, rollback/cancellation and native bypasses. Both save
paths use the shared prepare/detect/validate order. There is no Stage or acknowledgment service.
Use native AcceptAllChanges after a successful SaveChanges(false).

The [package contract](../src/Gloaming.EventReactions/README.md) and
[consolidation report](reports/event-reactions-consolidation.md) are current. Earlier D2.1/EF1 reports
retain separately dated evidence. Required intra-module work remains atomic; durable deferred work
and in-process saga delivery need separately selected requirements/proofs.

## Rich state-stored aggregate

[RichAggregateDemo](../samples/Wholesale/RichAggregateDemo/README.md) creates and edits a Sales
draft with relational children and a joined customer summary, without a broker or event replay.
Use a fresh disposable PostgreSQL database; fixed finite setup is not repeatable application setup.

```sh
WHOLESALE_DEMO_CONNECTION_STRING='Host=localhost;Database=rich_draft_demo;Username=postgres;Password=local' \
  dotnet run --project samples/Wholesale/RichAggregateDemo/RichAggregateDemo.csproj
dotnet test --project samples/Wholesale/RichAggregateDemo.Tests/RichAggregateDemo.Tests.csproj
```

Tests use Testcontainers PostgreSQL 18.6 and the rootless Podman socket environment above.
[The D1.1 report](reports/d1-1-rich-state-stored-aggregate.md) separates new domain/persistence
proofs from earlier evidence and records native-write bypass limitations.

## Sales resource permissions

Sales draft resource-permission adoption follows the separate
[A2.1 scope](plans/a2-1-sales-resource-permissions.md). Its PostgreSQL query and HTTP proofs
run in the existing `CqrsDemo.Tests` and `HttpIdentityDemo.Tests` projects using the same
Podman environment below. The [Sales guide](../samples/Wholesale/modules/Sales/README.md)
and [HTTP routes](../samples/Wholesale/HttpIdentityDemo/README.md#private-draft-permissions)
describe explicit migration/setup and the supported limits; no RLS service is required.

## Optional Sales PostgreSQL RLS

The separate [A2.2 consumer](../samples/Wholesale/CqrsDemo/TenantIsolation/README.md) uses
`CqrsDemo --tenant-isolation` with separately provisioned owner/runtime credentials and a
fresh disposable database. Its guide lists provisioning, the five-table inventory and
native transaction-local setup. The [report](reports/a2-2-sales-postgres-rls.md) distinguishes
tenant isolation from Sales permissions and default HTTP/worker behavior.

The existing CQRS CI project includes all RLS proofs. To run only those cases:

```bash
DOCKER_HOST=unix:///run/user/1000/podman/podman.sock \
TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE=/run/user/1000/podman/podman.sock \
DOTNET_PROCESSOR_COUNT=4 \
  dotnet test --project samples/Wholesale/CqrsDemo.Tests/CqrsDemo.Tests.csproj --filter-class '*TenantIsolationTests'
```

## PostgreSQL tenant-context utility

The optional [Tenancy adapter](../src/Gloaming.Tenancy/Gloaming.Tenancy.EntityFrameworkCore.Postgres/README.md)
requires an explicit custom setting name and caller-owned native transaction. It reads the
immutable operation accessor; it does not configure RLS policies, credentials or transactions.
Its [extraction report](reports/a2-3-postgres-tenant-context.md) records independent adoption
and the transferred Sales lifecycle proofs.

```bash
DOCKER_HOST=unix:///run/user/1000/podman/podman.sock \
TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE=/run/user/1000/podman/podman.sock \
DOTNET_PROCESSOR_COUNT=4 \
  dotnet test --project src/Gloaming.Tenancy/tests/TenancyPostgresTests/TenancyPostgresTests.csproj
```

The Tenancy CI lane includes this suite. Wholesale's CQRS suite uses the same public utility
through its separately provisioned opt-in RLS mode.

## Organization membership administration

The [A1.1 Access scope](plans/a1-1-membership-suspension.md),
[consumer setup and limits](../samples/Wholesale/modules/Access/README.md) and
[implementation report](reports/a1-1-membership-suspension.md) cover the focused
suspend/reactivate/audit journey. The existing HTTP suite runs its PostgreSQL, concurrency,
same-cookie and migration proofs; no additional CI project or service is required.

```bash
dotnet build ModulithFoundry.slnx --no-restore
DOCKER_HOST=unix:///run/user/1000/podman/podman.sock \
TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE=/run/user/1000/podman/podman.sock \
DOTNET_PROCESSOR_COUNT=4 \
dotnet test --project samples/Wholesale/HttpIdentityDemo.Tests/HttpIdentityDemo.Tests.csproj --no-build --no-restore
```

Use the documented Podman socket for this workstation or your reachable Docker-compatible
engine. Testcontainers owns disposable PostgreSQL 18.6 databases and keeps its resource reaper
enabled. No runtime endpoint starts migrations/seeding or changes customer IdP accounts.

## CQRS consumer patterns

[CqrsDemo](../samples/Wholesale/CqrsDemo/README.md) reads Inventory availability through
Contracts, creates/edits a Sales draft, then filters its joined draft/customer view before
deterministic pagination and a matching filtered count. Native typed DI and explicit native
transactions remain visible. Count/page share a snapshot only when the caller selects a
repeatable-read transaction; tests also demonstrate read-committed drift.

```sh
WHOLESALE_DEMO_CONNECTION_STRING='Host=localhost;Database=cqrs_demo;Username=postgres;Password=local' \
  dotnet run --project samples/Wholesale/CqrsDemo/CqrsDemo.csproj
dotnet test --project samples/Wholesale/CqrsDemo.Tests/CqrsDemo.Tests.csproj
```

Use a fresh disposable database for the console; tests use PostgreSQL 18.6 Testcontainers
and the rootless Podman environment above. Wholesale CI runs this suite. The
[C1 report](reports/c1-cqrs-consumer-patterns.md) records verification, reusable-mechanism
findings and remaining template work.

## Archived backend

The archived solution and tests remain historical reference material. Active CI and commit
hooks exclude archived restore/build/style/analyzer/test commands; the repository checks
job still verifies all frozen source hashes. Do not add archive tests to active gates.
See [the archive guide](../archive/README.md) for opt-in manual commands and provenance,
and [the CI correction report](reports/ci-runtime-and-archive-scope.md) for the boundary.

Use the archived test guide's Docker/Podman setup for container suites. It includes socket
configuration, keeps the resource reaper enabled, and explains runner concurrency limits.
No test may require personal credentials or an undeclared local process.

## New library and sample checks

Add each test to a documented CI lane in the increment that introduces it. Pure decision
and interface tests run without containers. PostgreSQL proves storage/transaction semantics;
real broker tests prove delivery behavior; Topology tests prove host wiring. Test through the
public library interface or sample Contracts. Fault setup may use SQL/process barriers;
product assertions use the consumer-facing seam.

Independence checks must build and exercise minimal consumers with only the selected
segment and its documented dependencies. Include separate state-stored, event-sourced,
and reliable-messaging compositions as those capabilities arrive. Sample architecture
policy should also demonstrate a configurable alternative and reject a deliberate violation.


## Ownership-declared PostgreSQL RLS

The optional [Persistence/PostgreSQL adapter](../src/Gloaming.Persistence/Gloaming.Persistence.EntityFrameworkCore.Postgres/README.md)
combines ownership selection with automatic transaction-local setup for scoped EF contexts.
Register the capability and use `.UseRls<TContext>(provider)` in options. The existing operation
owner begins the transaction once; setup is automatic for all its reads/writes. No extra RLS
scope or explicit setup call is needed. Selected reads without a transaction are rejected.
Its independent consumer retains EF filters and proves database writes through reviewed
USING/WITH CHECK policy operations, with separate restricted runtime credentials.

```bash
DOCKER_HOST=unix:///run/user/1000/podman/podman.sock \
TESTCONTAINERS_DOCKER_SOCKET_OVERRIDE=/run/user/1000/podman/podman.sock \
DOTNET_PROCESSOR_COUNT=4 \
  dotnet test --project src/Gloaming.Persistence/tests/PostgresTests/PostgresTests.csproj
```

The suite belongs to the Persistence CI lane. This adds no default Wholesale HTTP/AppHost/worker
adoption; the existing Sales RLS executable keeps its explicit local-transaction setup. The
[A2.5 report](reports/a2-5-transaction-rls-simplification.md) distinguishes the refined mechanism,
current proofs, explicit migration obligations and unsupported transaction/pooling modes.

The separate [Sales HTTP opt-in](../samples/Wholesale/HttpIdentityDemo/README.md#optional-sales-http-row-security)
selects automatic setup with `ConnectionStrings:Sales`. Provision its two restricted runtime
roles and run finite `--initialize-sales-rls` after ordinary migrations/seeding, following that
guide. HTTP startup validates the finite role/policy inventory but never installs it. New cases
run in `samples/Wholesale/HttpIdentityDemo.Tests/HttpIdentityDemo.Tests.csproj` through the
existing Wholesale PostgreSQL lane. The [A2.6 report](reports/a2-6-sales-http-rls.md) records
the bounded adoption and distinguishes unchanged AppHost/transport behavior.

The [Aspire RLS opt-in](../samples/Wholesale/AppHost/README.md#optional-sales-rls-graph) now carries
that composition into native orchestration. Set `LocalDevelopment:UseSalesRowSecurity=true`
and the two secret runtime password parameters, run explicit finite setup on a fresh database,
check exit 0, then explicitly start the API. Retained initialized storage needs only API start.
The runtime suite includes both ordinary and RLS browser/profile journeys; run it separately
from other graph lifetimes to avoid container-network changes during browser navigation.
The [A2.7 report](reports/a2-7-aspire-sales-rls.md) records current role/readiness and credential
separation evidence. Worker/transport adoption remains separate.

The workflow sample now also has a separate [Sales dispatch role](../samples/Wholesale/WorkflowDemo/sales-outbox-dispatch.md):
run finite `setup-sales-dispatch` against an already migrated outbox, then start `dispatch-sales`
with only its dispatch and broker connections. Its context maps one delivery table, and its
runtime grants permit envelope reads plus delivery-state column updates. It does not install
tenant RLS on the global queue or adopt inbox/business worker paths. The existing workflow test
project includes both-tenant delivery, database privilege controls, real publication failure,
competing child processes and death/restart after confirmed publication. The
[A2.8 report](reports/a2-8-sales-outbox-dispatch.md) records fresh evidence and limits.

The optional [WF2 RLS inbox role](../samples/Wholesale/WorkflowDemo/sales-inbox-rls.md) uses explicit
`setup-sales-replies`, then `process-sales-replies` with only `ConnectionStrings:SalesReplies`
and one admitted `Organization`. Existing scoped processing automatically receives immutable
tenant/system-actor contexts before its transaction; no per-message tenant-database setup call
is needed. It protects workflow/inbox rows and outgoing INSERTs while retaining global dispatch.
The same workflow suite exercises this composition and the related local-delivery suite covers
the retained default handler. [A2.9 evidence](reports/a2-9-sales-inbox-rls.md) is recorded separately.
