# Development and verification

Library packages, contracts and adoption tests live under src/Gloaming.{Family}/.
[Wholesale](../samples/Wholesale/README.md) is the reference application, with Access, Sales
and Inventory plus native API, worker, migration/setup and Aspire composition.

## Build

Use the .NET SDK selected by global.json and Node selected by the tooling package manifests.
The SDK uses `patch` roll-forward: prefer the specified version, or use a later installed
patch in the same feature band when that version is absent. CI installs the specified SDK
instead of resolving a moving latest-patch channel. Update the version in global.json to
change the SDK installed by CI.

Restore the pinned tools and dependencies:

~~~bash
dotnet tool restore
npm ci --ignore-scripts
npm ci --prefix eng/repository
npm ci --prefix eng/release
dotnet restore Gloaming.slnx
~~~

Run the day-to-day commands from the repository root.

| Task | Command |
| --- | --- |
| Build the full solution | `dotnet build Gloaming.slnx --no-restore` |
| Test projects affected by your changes | `npm run test:affected` |
| Run all regular .NET tests, including architecture and Wholesale runtime | `npm test` |
| Run selected .NET test projects | `npm test -- -p InboxPostgresTests,OutboxPostgresTests` |
| Run ordinary .NET tests without architecture and Wholesale runtime | `npm run test:ordinary` |
| Run all correctness suites and package adoption proofs | `npm run verify` |
| Run the opt-in performance suites | `npm run perf` |
| Start Wholesale with the configured AppHost | `aspire start` |

Run `npm test` or `npm run perf` from a capability directory such as
`src/Gloaming.Messaging` or `samples/Wholesale` to select its subtree. Explicit
`-p` project names work from either location. Use
`npm run nx -- show projects -t test-dotnet` or
`npm run nx -- show projects -t performance` to list selectable projects.

CI uses these same commands after installing their prerequisites. The scope job
selects the affected ordinary tests and the checks that require separate runner
setup. Manual CI dispatch runs the full correctness suite. Its `performance`
option runs all performance targets or selects the Events, Messaging, or Wholesale
suite using the corresponding npm command, then uploads the native reports.
The [test guide](../tests/README.md#run-one-suite) shows the individual suite
commands, execution order and report locations.

Full correctness needs a container engine, the Wholesale browser prerequisites
below, and the [package proof's PostgreSQL connection](../eng/release/README.md#full-package-adoption-proof).
Run `npm run verify:packages` or `npm run verify:kafka` to select either proof.

When the pinned SDK is unavailable from your OS package feed, Microsoft's
[SDK installer](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-install-script)
can install the version selected by global.json. Keep SDK selection explicit.

## Native provider and process tests

PostgreSQL and RabbitMQ tests use Testcontainers and require a reachable Docker-compatible
runtime. Rootless Podman can use its user socket through DOCKER_HOST. Configure socket access
for the test process; TESTCONTAINERS_RYUK_DISABLED=true is useful for environments that manage
container cleanup independently.

The repository uses xUnit on Microsoft Testing Platform. Use `dotnet test`
directly for runner-level options.

~~~bash
dotnet test --project tests/ArchitectureTests/ArchitectureTests.csproj --no-progress --no-ansi --output Normal --show-stdout Failed --show-stderr Failed
dotnet test --project samples/Wholesale/IntegrationTests/Wholesale.IntegrationTests.csproj --no-progress --no-ansi --output Normal --show-stdout Failed --show-stderr Failed
~~~

Use the [test map](../tests/README.md) to find family suites and their provider requirements.
If a constrained environment requires serial builds, build separately with -m:1, then run
tests with --no-build. MSBuild arguments are not test-runner arguments.

## Native Kafka package proof

The separate [Kafka recipe](../modular-monolith-patterns/kafka.md) uses the pinned SDK and
Node with Testcontainers. It packs current libraries and builds an external consumer with
a fresh package cache before running it against disposable Kafka and PostgreSQL.

~~~bash
npm run verify:kafka
~~~

Use the Docker-compatible socket for your engine. The [Kafka consumer guide](../eng/release/consumers/Kafka/README.md)
includes the rootless Podman invocation, retained evidence and resource ownership. Native provider/process, ordinary
package and this Kafka package profile protect different integration obligations.

## Run the reference application

The [sample guide](../samples/Wholesale/README.md), [API setup](../samples/Wholesale/Api/README.md)
and [worker roles](../samples/Wholesale/Worker/README.md) describe explicit initialization,
authentication and credentials. Normal runtime startup does not migrate or seed.

The [AppHost guide](../samples/Wholesale/AppHost/README.md) covers Aspire, OIDC, HTTPS trust
and browser prerequisites. The runtime suite uses actual processes and containers:

~~~bash
dotnet build samples/Wholesale/RuntimeComposition.Tests/RuntimeComposition.Tests.csproj
pwsh samples/Wholesale/RuntimeComposition.Tests/bin/Debug/net10.0/playwright.ps1 install --with-deps chromium
npm run test:system -- --excludeTaskDependencies
npm run test:browser -- --excludeTaskDependencies
~~~

Run local runtime graphs sequentially: container-network teardown can disturb another graph.
Use the AppHost guide's trust instructions for Linux browser and HTTPS tests. To retain
diagnostics locally, create a results directory, set `WHOLESALE_RUNTIME_RESULTS` to its
path, and append `--results-directory="$WHOLESALE_RUNTIME_RESULTS" --report-xunit-trx
--report-xunit-markdown` after `--` on either suite command. This retains AppHost state and
recent logs alongside the Playwright traces and native test reports.

## Use packages from another repository

~~~bash
npm --prefix eng/release run pack
~~~

The command prints a unique prerelease version and a completed local folder feed.
The [package guide](../eng/release/README.md) covers source mapping, exact versions and the
heavier archive/adoption verifier. Take a new snapshot after library changes so consumers
restore new bytes through ordinary NuGet tooling.

For a new consuming application, start with
[modular monolith patterns](../modular-monolith-patterns/README.md).

## Repository checks

~~~bash
npm --prefix eng/repository run check
npm --prefix eng/release run check
dotnet csharpier check . --include-generated
dotnet format style Gloaming.slnx --verify-no-changes --no-restore
dotnet format analyzers Gloaming.slnx --verify-no-changes --no-restore
~~~

[Repository tooling](../eng/README.md) checks public Markdown and literal
project/source links. Ignored local notes do not participate in published documentation.
The [release-tool checks](../eng/release/README.md#full-package-adoption-proof) also format-check
the retained package-consumer C# sources. The [native proxy suite](../eng/release/tests/KafkaProxy.Tests.csproj)
exercises socket lifecycles as part of the ordinary Nx tests.

The [CI workflow](../.github/workflows/ci.yml) uses one scope job for Repository integrity,
package adoption, Kafka adoption, and Wholesale runtime. The run summary shows the comparison
range and selected checks. Repository integrity verifies the results of the selected environment
checks before running repository validation. It builds the solution once for .NET checks and uses
Nx's MSBuild graph to select ordinary native tests. Inspect the graph and verify selection locally
with the following commands.

~~~bash
npm run graph
npm run check:nx
~~~

Provider/process tests still need a reachable Docker-compatible runtime. Build outputs may
be cached locally; tests always execute. Ordinary commands leave architecture checks to
Repository integrity and browser/Aspire tests to the runtime job. Shared inputs and manual
CI dispatch run all ordinary tests. Central package edits conservatively select full coverage.
See the [Nx and CI selection rules](../eng/repository/README.md#nx-task-graph-and-ci-selection)
for the environment scopes, comparison SHAs, linked fixture inputs and limits.
