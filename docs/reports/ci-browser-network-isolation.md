# CI browser network isolation

Date: 2026-10-09.

## Failure and scope

The merged dependency repair's [Wholesale runtime job](https://github.com/oxface/modulith-foundry/actions/runs/37958811207/job/113916129763)
failed during `RealUnmappedAccountIsRejectedWithoutEmailLinkingOrProvisioning`.
The owner supplied the failing test output: Chromium's initial navigation to the API's
HTTPS `/login` threw `net::ERR_NETWORK_CHANGED`, before entering credentials or checking
the unmapped account's admission. The separate PostgreSQL health-check exception is
consistent with the suite's deliberate database-outage test; it is not the reported
browser failure.

`BrowserTests` and `RuntimeTests` were separate xUnit collections and could run
concurrently. Each owns an Aspire graph, including container startup and teardown on
the same machine. This permits infrastructure changes during another test's browser
navigation. Chromium's Linux implementation observes
[IP address and link changes](https://github.com/chromium/chromium/blob/main/net/base/network_change_notifier_linux.cc).
The overlap is an identified harness risk, not a locally reproduced causal explanation
for this particular hosted failure.

The runtime assembly now opts into xUnit 4's native
`[assembly: Parallelization(Mode = ParallelMode.None)]`. Only this four-test sample
suite is serialized. Each test retains its own disposable graph and its existing
assertions and deadline. No authentication assertion is relaxed, no failed journey is
retried, and no package or CI job timeout changes.

## Changed files

- [AssemblyInfo.cs](../../samples/Wholesale/RuntimeComposition.Tests/AssemblyInfo.cs)
  sets the runtime assembly's scheduling policy, with its infrastructure rationale.
- [Development guide](../development.md#wholesale-runtime-verification) documents
  sequential execution and independent graph ownership for local and hosted runs.
- This report distinguishes the hosted failure from the verified configuration change.

## Verification and limits

The native xUnit 4.0.1 runner's diagnostic startup output confirms the change from
`parallel mode = collections [8 threads]` on the previously built runtime assembly
to `parallel mode = none` on the new assembly. These diagnostic invocations deliberately
selected no test cases: they verify the effective scheduling configuration, not runtime
behavior or a reproduction of the browser error.

The local verification uses .NET 10.0.401, matched Aspire 13.6.1 SDK/packages/CLI/runtime
bundle, rootless Podman, real PostgreSQL and Keycloak, and the pinned Chromium build.
Fresh results:

- Active solution build: passed with zero warnings and errors.
- All four runtime tests: passed, zero skipped, in 3m 31s, including the unmapped-account
  journey that failed in CI and the deliberate database-outage test.
- CSharpier: all 505 files selected by the current root configuration passed.
- Frozen archive: all 800 original files verified against the frozen baseline.
- Changed documentation link targets and `git diff --check`: passed.
- The initially empty index remains unchanged; all three changed files are unstaged.

The previous combined dependency verification also passed these four tests locally;
that historical green result did not reproduce the subsequent hosted failure.
The exact hosted network-change error has not been reproduced locally. A new hosted
run must confirm the mitigation; sequential test execution cannot prevent unrelated
runner-wide network changes. If the error recurs, retain the failed test output and
inspect network events before broadening this fix.

No new reusable mechanism was proven. This is sample test-harness configuration;
Gloaming libraries, generated template behavior and archived source remain unchanged.

The [throughput follow-up](ci-test-throughput.md) records bounded inbox test parallelism
and the narrower runtime CI build graph delivered alongside this isolation change.
