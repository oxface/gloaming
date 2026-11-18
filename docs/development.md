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
| Run every ordinary .NET test project | `npm run test:ordinary` |
| Start Wholesale with the configured AppHost | `aspire start` |

The test scripts exclude architecture and browser/runtime suites, which have their own
commands below. Set up the [AppHost prerequisites](../samples/Wholesale/AppHost/README.md)
before starting Wholesale.

When the pinned SDK is unavailable from your OS package feed, Microsoft's
[SDK installer](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-install-script)
can install the version selected by global.json. Keep SDK selection explicit.

## Native provider and process tests

PostgreSQL and RabbitMQ tests use Testcontainers and require a reachable Docker-compatible
runtime. Rootless Podman can use its user socket through DOCKER_HOST. Configure socket access
for the test process; TESTCONTAINERS_RYUK_DISABLED=true is useful for environments that manage
container cleanup independently.

The repository uses the Microsoft Testing Platform runner. Its options are passed directly,
without an extra separator:

~~~bash
dotnet test --project tests/ArchitectureTests/ArchitectureTests.csproj --no-progress --no-ansi --output Normal --show-stdout Failed --show-stderr Failed
dotnet test --project samples/Wholesale/IntegrationTests/Wholesale.IntegrationTests.csproj --no-progress --no-ansi --output Normal --show-stdout Failed --show-stderr Failed
~~~

Use the [test map](../tests/README.md) to find family suites and their provider requirements.
If a constrained environment requires serial builds, build separately with -m:1, then run
tests with --no-build. MSBuild arguments are not test-runner arguments.

## Native Kafka package proof

The separate [Kafka recipe](../modular-monolith-patterns/kafka.md) uses a local Docker or Podman
CLI, the pinned SDK/Node and real task-owned Kafka/PostgreSQL containers. It packs current libraries
and builds an external consumer with a fresh package cache. It is not an additional native
Microsoft Testing Platform project:

~~~bash
npm --prefix eng/release run verify:kafka
# Select Podman explicitly if both CLIs are installed:
GLOAMING_CONTAINER_RUNTIME=podman npm --prefix eng/release run verify:kafka
~~~

See the [consumer guide](../eng/release/consumers/Kafka/README.md) for execution, retained
evidence, resource ownership and the selected topology. Native provider/process, ordinary
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
dotnet test --project samples/Wholesale/RuntimeComposition.Tests/RuntimeComposition.Tests.csproj --no-build --no-progress --no-ansi --output Normal --show-stdout Failed --show-stderr Failed
~~~

Run local runtime graphs sequentially: container-network teardown can disturb another graph.
Use the AppHost guide's trust instructions for Linux browser and HTTPS tests.

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
the retained package-consumer C# sources and exercise helper lifecycles without a database or broker.

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
