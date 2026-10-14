# CI test throughput

Date: 2026-10-09.

## Outcome and scope

The follow-up to [browser network isolation](ci-browser-network-isolation.md) keeps
the four-test Aspire runtime suite sequential, allows bounded parallelism in the inbox
PostgreSQL suite, and narrows runtime CI restore/build to its actual consumer graph.
Family jobs already run concurrently and retain their existing path selection and names.

Changes:

- [Inbox AssemblyInfo.cs](../../src/Gloaming.Messaging/tests/InboxPostgresTests/AssemblyInfo.cs)
  registers `PostgreSqlFixture` as a native xUnit assembly fixture and explicitly selects
  `ParallelMode.All` with `MaxThreads = 2`.
- The inbox suite's `InboxConsumer.cs` removes its collection definition; `HostingTests.cs`,
  `IntakeTests.cs`, `ProcessingTests.cs` and `RegistrationTests.cs` remove their collection
  annotations. Their assertions and test bodies are unchanged.
- [Sample runtime workflow](../../.github/workflows/sample-runtime.yml) restores/builds
  `RuntimeComposition.Tests.csproj` and its native references, including AppHost and API.
  It no longer restores/builds the complete solution in this job. Repository integrity
  retains the full solution checks.
- [Development guide](../development.md#ci-lanes) and the
  [Messaging guide](../../src/Gloaming.Messaging/README.md) describe the scheduling policy.

No runtime library interface or implementation changes. The shared PostgreSQL fixture,
module migrations, template files, fixtures and frozen archive remain unchanged. All
changes, including the preceding browser isolation patch, remain unstaged for owner review.

## Fixture ownership and safety

The fixture shares only the container. Every database operation creates its own connection,
and each test creates a randomly named database. No shared DbContext, message row, service
provider, mutable handler scenario or worker lifecycle crosses test boundaries. Existing
pooling behavior remains disabled for these per-test databases.

Native [assembly fixtures](https://xunit.net/docs/shared-context#assembly-fixtures)
allow sharing the container without serializing the entire suite in one collection.
At most two test cases run concurrently, including cases within a class. This is an explicit
opt-in for the audited inbox suite, not a repository-wide change. The existing competing
writers and lock tests still exercise concurrency inside each test independently of
test-runner scheduling.

## Measurements and rejected candidates

These are local measurements with .NET 10.0.401, xUnit 4.0.1, real PostgreSQL 18.6 via
rootless Podman and `DOTNET_PROCESSOR_COUNT=2`. Each invocation runs independently and
includes container setup/teardown. Both suites have 28 test cases, with no skips.

| Suite | Existing collection fixture | Two concurrent classes | Two concurrent tests |
| --- | --- | --- | --- |
| Inbox | 9.772s, 10.998s | 8.680s | 7.910s, 8.372s, 8.324s |
| Outbox | 9.603s | 9.922s | 13.902s, 10.283s |

All candidate runs passed. Inbox's per-test candidate shortened all three measured runs;
outbox did not establish an improvement, so its experiment was removed and its original
collection fixture restored. Container startup and machine load vary: these samples do
not establish a fixed percentage improvement on GitHub-hosted runners.
The second inbox baseline used the previously built, unchanged suite in the earlier
dependency-verification checkout, after the runtime and analyzer jobs had finished.

The earlier hosted run spent 4m 9s in Wholesale runtime and 2m 54s in repository integrity.
Runtime restore/build took 14s/24s; repository restore/style/analyzers/build took
31s/28s/37s/52s. These are historical hosted timings, not results from this patch.
Selected family jobs were already independent: their durations overlap rather than sum.

## Verification and remaining work

A clean detached checkout from merged `origin/main` received only the runtime assembly's
sequential scheduling attribute. Restoring/building the runtime test project succeeded
without outputs from unrelated projects and built 24 projects instead of the solution's
57. This proves dependency coverage; its local build duration is not a hosted speed claim.

Native runner diagnostics confirm `parallel mode = all [2 threads]` for inbox and the
original collection scheduling for outbox. Diagnostic invocations selected zero test
cases; the real 28-case runs above provide the behavioral verification.

Final verification:

- Clean runtime consumer graph: all four real PostgreSQL/Keycloak/Chromium tests passed,
  zero skipped, in 3m 32s. No unrelated project outputs existed in that checkout.
- Final inbox scheduling: all 28 cases passed in each of three measured runs, zero skipped.
- Active solution build: passed with zero warnings/errors.
- Whole-solution style and analyzers: passed.
- Architecture: all 78 tests passed.
- CSharpier, including generated files as in CI: all 530 files passed.
- Frozen archive: all 800 original files verified against its frozen baseline.
- Workflow YAML/project scoping, changed documentation targets and `git diff --check`: passed.
- The originally empty index remains unchanged; nothing was committed or pushed.

Hosted CI still needs to establish duration and stability. The `.github` change is a
shared input, so the existing selector deliberately requests all checks for this patch.
Cross-project parallel execution is deferred: safely doing that also needs a separate
build phase to avoid concurrent writes to shared outputs. Dependency caching and further
changes to repository integrity should be measured separately; no additional cache action,
custom orchestration script or build artifact pipeline is introduced here.

No new reusable library mechanism was proven. This is native test-harness and CI
configuration; business rules, consumer transaction/concurrency tests and template
composition remain consumer-owned and unchanged.
