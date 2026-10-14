# Combined dependency update repairs

Date: 2026-10-09.

## Scope and review

At the owner's request, combine the Aspire watch repair, OpenTelemetry update and
local SDK setup guidance on `fix/dependency-update-integration`, starting from
fetched `origin/main` at `919909f`. This is the current change set; the earlier
isolated candidates are superseded. All repository changes remain unstaged and
the pre-existing empty index is preserved. No commit, push or remote PR changes.

| File | Final change |
| --- | --- |
| `Directory.Packages.props` | Stable Aspire hosting/testing packages 13.6.1; the six OpenTelemetry updates from PR #11. |
| `samples/Wholesale/AppHost/Wholesale.AppHost.csproj` | SDK 13.6.1 and explicit exporter/hosting references using central versions. |
| `.github/workflows/sample-runtime.yml` | Derive CLI version from the native SDK property, then install it. |
| `samples/Wholesale/AppHost/README.md` | Matching install command and remaining SDK/package alignment obligation. |
| `docs/development.md` | Native CLI install command and per-user SDK setup when apt lacks the pinned feature band. |
| Three reports | Preserve the separate diagnoses and record combined verification. |

The workflow uses `dotnet msbuild -getProperty:AspireHostingSDKVersion -nologo`:
no custom XML parser, repository script, new action or additional version manifest.
`DOTNET_NOLOGO=true` suppresses first-use banners while capturing the property.
The same SDK property produced `13.6.1` under a fresh CLI home and successfully
selected Aspire.Cli 13.6.1 in an isolated install. Dependabot still does not update
the AppHost SDK attribute: review SDK/package alignment together.

The AppHost exporter/hosting references no longer repeat the central 1.19.1
versions through `VersionOverride`. Package versions remain centrally managed and
the formatter accepts the shorter references. There are no production C# changes,
Gloaming interface changes, test exemptions or longer timeouts.

## SDK installation

Keep both repository/template SDK manifests at 10.0.401. The inspected Ubuntu
26.04 apt feed offers 10.0.112; that .1xx SDK cannot satisfy a .4xx `latestPatch`
request. CI already uses `actions/setup-dotnet` with `global-json-file`, so it
does not depend on apt's SDK availability. [Microsoft's installer](https://learn.microsoft.com/en-us/dotnet/core/tools/dotnet-install-script)
supports a specific SDK or the version in `global.json`.

The owner explicitly authorized per-user installation and PATH configuration.
Installed 10.0.401 and retained 10.0.112 under `~/.dotnet`, without replacing the
apt installation. Appended guarded `DOTNET_ROOT`/PATH setup to `~/.profile` and
`~/.bashrc`; both parse successfully. Fresh login and interactive shells resolve
`~/.dotnet/dotnet`, select 10.0.401 in this repository and list both installed SDKs.
These machine-local shell changes are outside the repository change set. Existing
terminals and IDE processes need an environment refresh; this does not edit their
already-running environment. Manual SDK installations require deliberate updates.

## Evidence and remaining limits

The [Aspire report](aspire-watch-timeout-upgrade.md) preserves the red/green
one-minute watch diagnostic and the initial Aspire-only verification. The
[OpenTelemetry report](dependabot-otel-formatting.md) preserves the actual formatter
failure and hosted passing tests for PR #11. Those results do not by themselves
prove the combined package graph.

Combined verification uses the user-installed .NET SDK 10.0.401, Aspire 13.6.1
packages/SDK/CLI and an isolated 13.6.1 runtime bundle. PostgreSQL, Keycloak and
Chromium are real; test source and assertions are unchanged.

Fresh results for the combined graph:

- Active solution build: passed with no warnings or errors.
- Wholesale runtime: four tests passed in 3m 20s. Both the provider-free runtime
  and first browser journey ran longer than the previous one-minute watch boundary.
- HTTP/PostgreSQL identity, business and native telemetry suite: 105 passed.
- Architecture suite: 78 passed.
- CSharpier: all 528 active files passed.
- Frozen archive: all 800 original files verified.
- Workflow YAML, changed documentation links and `git diff --check`: passed.
- Native SDK-version query and CLI install: selected 13.6.1, including a fresh
  CLI home with first-use banners suppressed.
- Login and interactive Bash startup: valid configuration, user-installed host
  selected, both SDKs available. The existing index stayed empty.

The runtime and HTTP suites ran concurrently; these timings do not establish a
performance regression or hosted-runner duration guarantee. No template SDK pin
changed in this branch, and generated-template adoption was not rerun locally;
the already-merged SDK update has its own hosted evidence.

After this branch passes hosted CI and is merged, refresh the two Dependabot PRs
so they account for the updates already integrated. Their old CI runs do not prove
this branch. Neither PR was closed or changed remotely here.

No new reusable mechanism was proven. This is dependency/toolchain maintenance
and native CI wiring. Module/library ownership, template composition and the
frozen archive remain unchanged.
