# Aspire dependency update: DCP watch failure

Date: 2026-10-09.

## Outcome

Reviewed [PR #10](https://github.com/oxface/modulith-foundry/pull/10) at
`cca0fb2c9d35259926b273217d0062dc27b7cdd2` in an isolated worktree. Hosted CI passed
its library-family, consumer, architecture, formatting and template checks, but
failed the Wholesale runtime test step. The owner supplied a DCP watch exception
with a one-minute `Polly.Timeout.TimeoutRejectedException`, alongside unsuccessful
readiness checks.

A local diagnostic reproduced the one-minute watch exception on Aspire 13.6.0.
The same diagnostic passed on 13.6.1. This is the upstream regression addressed by
[Aspire 13.6.1](https://github.com/microsoft/aspire/releases/tag/v13.6.1) and
[the watch retry/restart fix](https://github.com/microsoft/aspire/pull/20466).
The local reproduction reported the `ContainerExec` watch; the owner's excerpt
reported the `Executable` watch. Both use the changed Kubernetes watch machinery,
but this is not a claim that the entire hosted readiness failure was reproduced.

The repair upgrades the stable Aspire packages and AppHost SDK to 13.6.1. CI derives
the matching CLI version from the native `AspireHostingSDKVersion` property.
No Gloaming interface, application behavior, health-check requirement,
test timeout or test assertion changes. The existing Keycloak preview integration,
PostgreSQL/Keycloak images and Playwright version remain unchanged. The initial
Aspire-only candidate retained the OpenTelemetry versions; the owner-requested
combined branch also contains the approved OpenTelemetry PR updates.

## File and behavior map

| File | Change |
| --- | --- |
| `Directory.Packages.props` | Align AppHost, PostgreSQL and Testing at 13.6.1. |
| `samples/Wholesale/AppHost/Wholesale.AppHost.csproj` | Use AppHost SDK 13.6.1. |
| `.github/workflows/sample-runtime.yml` | Read the AppHost SDK version through MSBuild and install the matching CLI. |
| `samples/Wholesale/AppHost/README.md` | Update installation instructions and record the coordinated upgrade obligation. |
| `docs/development.md` | Update the runtime install command and document per-user .NET SDK installation. |
| This report | Record local evidence, hosted evidence and the remaining CI confirmation. |

Dependabot's NuGet group does not update the SDK attribute. Keep it aligned with the
stable hosting/testing packages during upgrade review. The workflow no longer has
a separate CLI version literal to maintain.
Version 13.6.1 was released on October 7: selecting it now is a targeted repair for
an observed vendor regression, rather than changing the seven-day cooldown for
routine Dependabot proposals.

## Reproduction and verification

All local runs used .NET SDK 10.0.401 from an isolated `/tmp` installation, real
PostgreSQL and Keycloak through Podman, native HTTPS certificate trust and the
matching Chromium browser. The user's existing running containers were untouched.

The unchanged PR runtime suite passed all four tests in 2m 18s locally. That does
not contradict the hosted failure: all individual test AppHosts completed before
one minute, and the normal assertions do not check background watch exceptions.

For a specific red/green signal, a temporary variant of the first browser test:

1. Registered a logging provider recording DCP watch termination diagnostics.
2. Waited 75 seconds after starting the AppHost.
3. Asserted no watch termination was recorded, then exercised normal setup/login.

```bash
dotnet test \
  --project samples/Wholesale/RuntimeComposition.Tests/RuntimeComposition.Tests.csproj \
  --filter-method '*AlphaLogsInSelectsTwoTenantsAndCommitsProtectedProfileEdit' \
  --no-progress --no-ansi --output Detailed \
  --show-stdout Failed --show-stderr Failed
```

The 13.6.0 diagnostic failed with the exact one-minute `TimeoutRejectedException`:
one test failed, runner exit code 2. The identical diagnostic passed on the matched
13.6.1 toolchain: one test passed in 2m 02s. This probe cannot be shortened to a
seconds-long test without changing the vendor's actual timeout path. The temporary
delay, logger and extra assertion were removed; no slow diagnostic test is added
to CI. The upstream project owns focused watch timeout regression tests. An existing
vendor fix and this red/green signal made speculative application changes unnecessary.

The first candidate run passed four tests while still using the old shared DCP
bundle; that was insufficient to verify a matched toolchain. Subsequent verification
uses an isolated Aspire home. Native MSBuild property inspection confirmed:

- `AspireHostingSDKVersion`: 13.6.1.
- Selected CLI: the temporary Aspire.Cli 13.6.1 installation.
- `DcpDir`: the isolated 13.6.1 bundle's DCP directory.
- `AspireDashboardPath`: the isolated 13.6.1 native dashboard.

After removing the diagnostic edits, verification of the initial Aspire-only
candidate on that matched toolchain:

- Active solution build: passed, no warnings or errors.
- Original Wholesale runtime suite: four tests passed in 2m 02s, including explicit
  setup, database outage and all three real OIDC/browser journeys.
- Architecture tests: 78 passed.
- CSharpier: all 528 active files passed.
- Frozen archive verification: all 800 original files passed.
- `git diff --check`: passed; every inspected index remained empty.

No temporary diagnostic source changes remain. The clean temporary investigation
worktree was removed. The repair was subsequently combined with the OpenTelemetry
update at the owner's request.

## Integration and limits

Changes now remain unstaged on `fix/dependency-update-integration`, based on fetched
`origin/main` at `919909f`, in the main repository worktree. See
[the combined verification](dependency-update-integration.md). No commit, push,
PR comment or remote branch modification was performed. The staged index stayed
unchanged. Archive source and fixtures were preserved.

This candidate still needs the hosted Wholesale runtime check on its new revision.
Startup readiness failures by themselves are not a diagnosis; transient unhealthy
checks occur before the provider starts and before explicit database setup. The
watch repair is locally demonstrated, but a separate hosted readiness problem may
remain. Do not bypass the runtime check to merge it. After integration, refresh
the Dependabot branch so its proposal incorporates or drops the already-updated
Aspire dependencies.

No new reusable mechanism was proven. This is sample/toolchain dependency maintenance;
Gloaming libraries and consumer ownership boundaries are unchanged. The OpenTelemetry
formatting diagnosis is recorded in [its report](dependabot-otel-formatting.md).
