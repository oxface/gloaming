# Development and verification

Independent library packages, contract tests and adoption examples live under
`src/Gloaming.{Family}/`. [Wholesale](../samples/Wholesale/README.md) is one reference product:
`Api`, `Worker`, `IntegrationTests`, `RuntimeComposition.Tests`, AppHost/ServiceDefaults and
module Contracts/implementations. Historical slice journeys are test-owned scenario support,
not separate products. The original solution and its historical commands remain available
through [immutable Git provenance](reports/s1-archive-retirement.md); active builds and proofs
use current owned sources.

## Local .NET SDK

Use the SDK selected by root `global.json`.
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

## Tooling and infrastructure

Follow the [.NET conventions](conventions/dotnet.md), repository workflow and root `global.json`.
Use Node 24 for repository and release tools. Restore pinned tools and packages before validation:

```bash
dotnet tool restore
npm ci --prefix tools/repository
dotnet restore ModulithFoundry.slnx
dotnet build ModulithFoundry.slnx -m:1
```

Real provider/process proofs use Testcontainers with PostgreSQL and RabbitMQ. For the local
rootless Podman setup:

```bash
export DOCKER_HOST=unix:///run/user/1000/podman/podman.sock
export TESTCONTAINERS_RYUK_DISABLED=true
```

The socket must be reachable by the test process. Sandboxed runs may require socket/IPC access.
The `dotnet test` MTP runner takes its options directly; do not forward them through an extra
`--`. Where local parallel MSBuild restore fails silently, build separately with `-m:1`, then
run tests with `--no-build`; MSBuild `-m:1` is not a test-runner argument.

## Wholesale product

Use the [product guide](../samples/Wholesale/README.md), [API setup](../samples/Wholesale/Api/README.md)
and [worker roles](../samples/Wholesale/Worker/README.md). The API's finite `--initialize-demo`
applies existing module migrations and sample data; normal API/worker startup never migrates
or seeds. The default API supports admitted profiles/catalog/memberships and the private Sales
draft lifecycle. Restricted HTTP RLS and restricted reply/dispatch workers retain their explicit
smaller capability and credential boundaries. Operator Inventory commands and stock
regeneration live in the single Worker, with caller-owned admission/credentials.

```bash
dotnet test --project samples/Wholesale/IntegrationTests/Wholesale.IntegrationTests.csproj --no-progress --no-ansi --output Normal --show-stdout Failed --show-stderr Failed
dotnet test --project tests/ArchitectureTests/ArchitectureTests.csproj --no-progress --no-ansi --output Normal --show-stdout Failed --show-stderr Failed
```

The [integration proof map](../samples/Wholesale/IntegrationTests/README.md) identifies the eight
merged project obligations. PostgreSQL/RabbitMQ concurrency, failure/death recovery, RLS,
historical JSON/SQL and migration assertions remain. Native telemetry cases run in an explicit
nonparallel collection; other classes keep ordinary parallel scheduling. Two historical finite
child scenarios remain in the test executable; the product never references that code.

The separate [runtime guide](../samples/Wholesale/AppHost/README.md) covers Aspire, trusted HTTPS,
Keycloak mappings and Chromium. It uses real processes and containers, including explicit setup,
readiness, database outage and protected browser journeys. Install the matching Playwright
browser/dependencies from its built output before executing:

```bash
dotnet build samples/Wholesale/RuntimeComposition.Tests/RuntimeComposition.Tests.csproj
pwsh samples/Wholesale/RuntimeComposition.Tests/bin/Debug/net10.0/playwright.ps1 install --with-deps chromium
dotnet test --project samples/Wholesale/RuntimeComposition.Tests/RuntimeComposition.Tests.csproj --no-build --no-progress --no-ansi --output Normal --show-stdout Failed --show-stderr Failed
```

For local Linux HTTPS trust, follow the runtime guide; the local proof uses
`SSL_CERT_DIR=$HOME/.aspnet/dev-certs/trust:/etc/ssl/certs`. Keep runtime graphs serialized:
container-network teardown from one graph can disturb another browser navigation.

## Local package development

Pack a new local Gloaming snapshot without a database or remote publication:

```bash
npm --prefix tools/release run pack
```

The command prints a unique prerelease version and the feed at `.artifacts/local-feed`.
Use `-- --output /absolute/feed/path` to choose another feed. The
[local package guide](../tools/release/README.md#everyday-local-packages) covers consumer
NuGet.Config, source mapping and exact package references. Take a new snapshot version
after each library change so consumers receive the new bytes through normal restore.

The existing [package-adoption workflow](../.github/workflows/packages.yml) runs the
[full proof](../tools/release/README.md#full-package-adoption-proof) through this same
command, then checks archives and clean external consumers, including PostgreSQL.

## Library and consumer proofs

Each family's README/docs give current guarantees, optional composition and commands for its
unit/provider and independent adoption suites. Use the [root test guide](../tests/README.md)
to find all suites: family tests stay under `src/Gloaming.*/tests`, cross-family tests under
`tests`, and Wholesale integration/runtime tests with the sample. The standalone
[EF coordinator suite](../src/Gloaming.EntityFrameworkCore/tests/CapabilityTests/CapabilityTests.csproj)
requires no running database. Events owns historical literal JSON linked
into Wholesale; neutral process support lives in `tests/Support`. A family change may also
require product integration according to the CI input map.

The [actor/tenant recipe](../src/Gloaming.Tenancy/docs/composition.md) has a direct native-DI
consumer, without a sample application or generated repository:

```bash
dotnet test --project tests/ContextCompositionTests/ContextCompositionTests.csproj
```

Use [application patterns](application-patterns.md) and local packages when starting an
application. The generator and its generated-only CI lane are retired; package-only adopters
continue proving consumption without source references or a special project layout.

## Formatting, isolation and CI selection

Run the lightweight repository checks, including local documentation and literal linked
project/fixture targets:

```bash
npm --prefix tools/repository run check
```

The [tooling guide](../tools/repository/README.md#local-references) defines the supported
syntax and limits. These checks run for documentation-only changes as well as code changes.
Native .NET formatting remains separate:

```bash
dotnet csharpier check . --include-generated
dotnet format style ModulithFoundry.slnx --verify-no-changes --no-restore
dotnet format analyzers ModulithFoundry.slnx --verify-no-changes --no-restore
```

Historical archive source and its manifest remain available through the provenance record above.
The [main workflow](../.github/workflows/ci.yml) runs family contract/adoption jobs separately,
then Wholesale integration once in its owning lane. Runtime/browser proofs
remain separate deployment obligations. Repository integrity validates the complete
solution and architecture. Hooks retain their existing scope; a focused local checkpoint does
not replace final integrated verification.

The SHA-pinned [filter workflow](../.github/workflows/changes.yml) uses
[explicit path inputs](../.github/ci-paths.yml), not a custom dependency parser. Shared project,
SDK/tooling and workflow changes select all checks; unknown inputs conservatively select full
coverage. Library implementation changes select dependent Wholesale coverage. Its linked event
fixtures and process helper select the product lane too. Wholesale source selects runtime.
Markdown-only changes run repository tooling/reference checks and omit expensive provider/browser checks. Configuration/detection
failure fails Repository integrity instead of silently skipping tests.

The [proof map](../tests/README.md) records the retained test owners and distinct unit,
provider, package-only and process obligations. [Retirement provenance](reports/s1-archive-retirement.md)
links original move/merge/remove records and dated execution results in Git; current setup
comes from the owning library or sample guide.
