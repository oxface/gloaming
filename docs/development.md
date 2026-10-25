# Development and verification

Library packages, contracts and adoption tests live under src/Gloaming.{Family}/.
[Wholesale](../samples/Wholesale/README.md) is the reference application, with Access, Sales
and Inventory plus native API, worker, migration/setup and Aspire composition.

## Build

Use the .NET SDK selected by global.json and Node selected by the tooling package manifests.
Restore the pinned tools and dependencies:

~~~bash
dotnet tool restore
npm ci --prefix tools/repository
npm ci --prefix tools/release
dotnet restore Gloaming.slnx
dotnet build Gloaming.slnx
~~~

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
npm --prefix tools/release run verify:kafka
# Select Podman explicitly if both CLIs are installed:
GLOAMING_CONTAINER_RUNTIME=podman npm --prefix tools/release run verify:kafka
~~~

See the [consumer guide](../tools/release/consumers/Kafka/README.md) for execution, retained
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
npm --prefix tools/release run pack
~~~

The command prints a unique prerelease version and a completed local folder feed.
The [package guide](../tools/release/README.md) covers source mapping, exact versions and the
heavier archive/adoption verifier. Take a new snapshot after library changes so consumers
restore new bytes through ordinary NuGet tooling.

For a new consuming application, start with
[modular monolith patterns](../modular-monolith-patterns/README.md).

## Repository checks

~~~bash
npm --prefix tools/repository run check
npm --prefix tools/release run check
dotnet csharpier check . --include-generated
dotnet format style Gloaming.slnx --verify-no-changes --no-restore
dotnet format analyzers Gloaming.slnx --verify-no-changes --no-restore
~~~

[Repository tooling](../tools/repository/README.md) checks public Markdown and literal
project/source links. Ignored local notes do not participate in published documentation.
The [release-tool checks](../tools/release/README.md#full-package-adoption-proof) also format-check
the retained package-consumer C# sources and exercise helper lifecycles without a database or broker.

The [main workflow](../.github/workflows/ci.yml) selects family suites, architecture and
dependent consumers using [explicit path inputs](../.github/ci-paths.yml). Shared tooling,
project or workflow changes select broader coverage; unknown inputs select conservatively.
[Runtime](../.github/workflows/sample-runtime.yml) and
[package adoption](../.github/workflows/packages.yml) have their own checks.
