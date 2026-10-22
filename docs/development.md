# Development and verification

Independent library packages, contract tests and adoption examples live under
`src/Gloaming.{Family}/`. [Wholesale](../samples/Wholesale/README.md) is one reference product:
`Api`, `Worker`, `IntegrationTests`, `RuntimeComposition.Tests`, AppHost/ServiceDefaults and
module Contracts/implementations. Historical slice journeys are test-owned scenario support,
not separate products. The original solution and its historical commands remain available
through [immutable Git provenance](reports/s1-archive-retirement.md); active builds and proofs
use current owned sources.

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

## Tooling and infrastructure

Follow the [.NET conventions](conventions/dotnet.md), repository workflow and root `global.json`.
Use Node 24 for repository/template tools. Restore pinned tools and packages before validation:

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
smaller capability and credential boundaries. Operator Inventory/Purchasing commands and stock
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

## Library and generated-template proofs

Each family's README/docs give current guarantees, optional composition and commands for its
unit/provider and independent adoption suites. In particular, Events owns the historical literal
JSON linked into Wholesale and Messaging owns the neutral child-process helper used there.
These examples have no dependency on Wholesale. A family change may require both its own tests
and the product integration suite, according to the CI input map.

[Template tooling](../tools/template/README.md) owns the generated repository rehearsal:

```bash
npm ci --prefix tools/template
npm run check --prefix tools/template
npm run verify --prefix tools/template
```

The generator checks independent output and source snapshots; it does not copy test-owned
Wholesale scenarios. Template choices evolve separately from S1's product consolidation.

## Formatting, isolation and CI selection

```bash
dotnet csharpier check . --include-generated
dotnet format style ModulithFoundry.slnx --verify-no-changes --no-restore
dotnet format analyzers ModulithFoundry.slnx --verify-no-changes --no-restore
```

Historical archive source and its manifest remain available through the provenance record above.
The [main workflow](../.github/workflows/ci.yml) runs family contract/adoption jobs separately,
then Wholesale integration once in its owning lane. Runtime/browser and template rehearsals
remain separate deployment/adoption obligations. Repository integrity validates the complete
solution and architecture. Hooks retain their existing scope; a focused local checkpoint does
not replace final integrated verification.

The SHA-pinned [filter workflow](../.github/workflows/changes.yml) uses
[explicit path inputs](../.github/ci-paths.yml), not a custom dependency parser. Shared project,
SDK/tooling and workflow changes select all checks; unknown inputs conservatively select full
coverage. Library implementation changes select dependent Wholesale coverage. Its linked event
fixtures and process helper select the product lane too. Wholesale source selects runtime.
Markdown-only changes omit expensive provider/browser/template checks. Configuration/detection
failure fails Repository integrity instead of silently skipping tests.

The [S1 report](reports/s1-wholesale-consolidation.md) records the exact move/merge/remove map,
new results, compatibility repairs and pending integrated follow-ups. Earlier slice reports retain
dated historical evidence, even when their source links now resolve to canonical product/test owners.
