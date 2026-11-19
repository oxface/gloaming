# Repository tooling

Requires Node.js 24.21 or newer within the 24.x line; CI pins 24.21.0.
Install the locked tools and run the lightweight integrity checks from any checkout:

```sh
npm ci --prefix eng/repository --ignore-scripts
npm --prefix eng/repository run check
```

`check` verifies TypeScript types, tooling formatting, reference/CI-selection regression cases and
local references. Repository integrity includes it in repository validation, including for
documentation-only PRs. The pre-commit hook selects it for documentation, project and tooling inputs.

## Local commit checks

After installing the repository tooling, install or refresh the Git hooks from the repository root.

```sh
npm exec --offline --prefix eng/repository -- lefthook install
```

The pre-commit hook runs the lightweight repository checks above and CSharpier for relevant
files. Commit-message validation runs separately through commitlint. These checks require the
pinned Node/SDK and restored tools, with no database or broker needed.

Native tests, full-solution style/analyzer verification and package adoption run in the selected
PR workflows. Run relevant checks before review using the
[development commands](../../docs/development.md#repository-checks) and
[test map](../../tests/README.md); committing does not execute those suites.

## Dependency updates

Dependabot groups EF Core, dotnet-ef and Microsoft.Extensions/ASP.NET Core servicing updates
together because EF patch releases can raise their minimum dependency versions. Central package
pins, the Npgsql driver/provider and the EF tool version travel in that update. SDK, Aspire,
telemetry and Testcontainers updates retain their own groups.

Microsoft Agent Framework packages update together so Workflows cannot raise its Agents
dependency above a consumer's central pin. The AI Evaluation packages also update together,
in a group declared before the broader Microsoft.Extensions servicing rule. These groups cover
minor and patch updates; major updates retain individual review.

Library project declarations describe the packages their code uses. Review a bot-added
PackageReference against that contract rather than widening architecture assertions to accept
it automatically. Restore, architecture tests and package-only consumers verify the resulting
graph. Release consumers read their direct DI/provider versions from the central package file.

## Local references

`npm --prefix eng/repository run references` discovers the repository from the working
directory. It checks Git-tracked files and non-ignored new files, using their working-tree
contents without staging anything. Deleted indexed files are absent targets. Ignored build
output and empty directories cannot satisfy links to retired source.

The supported checks are deliberately bounded:

- Markdown inline links/images and reference definitions: resolve relative file/directory
  targets, URI encoding and escaped parentheses. Ignore fenced examples, inline code,
  comments, URI schemes and fragment-only links. Paths stay portable inside the repository.
- Native solution/project/props/targets files: check literal ProjectReference, Project Path,
  Import Project and Compile/None/Content/EmbeddedResource/AdditionalFiles Include targets,
  including semicolon lists and linked fixtures. Normalize native backslashes.
- Report file and line for each missing/nonportable/unowned target, and exit unsuccessfully.

This is an existence check for the supported syntax, not a Markdown renderer or MSBuild
interpreter. It does not validate heading anchors, external URLs, HTML links, autolinks,
indented code blocks or arbitrary nested Markdown constructs. MSBuild properties, item
expressions, metadata and globs are counted as skipped; conditions and imports are not
executed. Native restore/build and package README adoption remain separate checks. Historical
Git links are verified against local objects when introduced, not fetched during each check.

Regression tests create disposable Git repositories under the system temporary directory and
exercise both valid consumers and actionable failures, including deletion, untracked docs,
ignored output, linked fixtures and command exit status. They remove their fixtures on exit.

## Commit messages

Commitlint uses `commitlint.config.mjs` and Conventional Commits: a recognized lowercase type,
optional scope and nonempty subject. Subject capitalization is unrestricted, so both
`chore: bump dependencies` and `chore: Bump dependencies` pass. Other rules from
`@commitlint/config-conventional` remain enabled. CI and the commit hook use the same
configuration; dependency bots receive no validation bypass.

## Nx task graph and CI selection

The root [Nx configuration](../../nx.json) pins the first-party `@nx/dotnet` plugin
through the root [manifest](../../package.json) and lockfile. MSBuild infers project
references, build dependencies and native `test-dotnet` targets. The native test target
name keeps .NET CI separate from npm packages' own `test` scripts. The [repository tooling](project.json) and [package tooling](../release/project.json)
also expose Nx targets.
The shared [suite entry points](../../docs/development.md#build) use the same
targets locally and in CI. From a capability directory, `npm test` and
`npm run perf` derive their scope from Nx project roots.

Install and restore before using the inferred graph:

```bash
npm ci --ignore-scripts
dotnet restore Gloaming.slnx
npm run nx -- show projects
npm run graph
npm run test:affected -- --base=origin/main
npm test -- -p InboxPostgresTests
npm run nx -- run repository-tools:check
```

Local affected commands include staged and unstaged changes when `--head` is omitted.
CI supplies explicit comparison SHAs.

The ordinary test commands exclude `RuntimeComposition.Tests`, which needs Aspire,
certificate trust and browser setup, and `ArchitectureTests`, which Repository integrity
runs for relevant .NET changes. Wholesale integration and native provider/process
tests remain ordinary inferred tasks and require a Docker-compatible runtime. Nx runs up
to two tasks concurrently by default. CI runs up to three ordinary test projects at a time.
Restore is explicit; inferred builds run without restore and Nx orders their project
dependencies. Tests always execute freshly (`cache: false`);
inferred build outputs may be reused from the local `.nx` cache. Nx Cloud connections are
disabled. No remote cache or distributed execution is configured.

The pinned plugin includes linked C# inputs but omits linked `None` items, including
retained JSON payloads used by deployment and codec consumers. `retainedFixtures` therefore
adds the repository's `Fixtures`/`fixtures` trees and project declarations (also linked as
`None` by architecture tests) as conservative inputs to every native project. A fixture edit can select unrelated tests. This intentionally trades precision
for coverage without maintaining fixture-to-family closures. A deleted fixture path is
also covered by these patterns. New external inputs outside those trees need declared
Nx inputs; custom targets reading files outside their project need their own inputs.
The root minimatch override keeps Nx's glob dependency on its patched release; verify the
graph and affected-selection proofs when updating it.

The [main workflow](../../.github/workflows/ci.yml) shares one Git comparison range between
Nx and path filters. PRs compare their base SHA with the checked-out merge SHA. Pushes to
`main` compare against the last successful push run of this workflow so a failed or canceled
run does not drop pending changes from scope. If the base is unavailable, equal to the
checked-out commit, or outside its history, all checks eligible for that event run.
Manual dispatch, changes to shared build or CI inputs, and paths outside the
configured source and tooling roots also request full coverage.
The scope job summary records the comparison range, selection or fallback reason, and which
checks will run.

The scope job uses SHA-pinned [`dorny/paths-filter`](https://github.com/dorny/paths-filter)
and the [environment scope map](../../.github/ci-paths.yml) for Repository integrity's .NET
steps, package adoption, and Kafka adoption. Package and Kafka proof scripts own the inputs
for consumers they generate outside the checkout.

The Kafka path filter and full-run fallbacks control the Kafka job. The scope summary
includes the [Kafka Nx target](../release/project.json) as an advisory comparison.
That target models the PostgreSQL messaging project, its transitive references and the
Kafka guide.

Wholesale runtime remains eligible on PRs and manual dispatch. Scoped PRs use Nx to detect
changes that affect `RuntimeComposition.Tests`, including its AppHost and API references.
Its [Nx target inputs](../../samples/Wholesale/RuntimeComposition.Tests/project.json) also
cover the entire Wholesale sample and root Aspire configuration, including files Aspire
reads at runtime. Scope needs the SDK and pinned Nx to evaluate the graph. The runtime job
owns the restore, browser installation, certificate trust, and Aspire test execution. The
`wholesale-runtime-diagnostics-<attempt>` artifact retains the test log, native xUnit TRX
and Markdown reports, bounded AppHost resource diagnostics and Playwright traces when
available. Repository integrity publishes its native xUnit reports and verifies release
tooling once whenever either package proof is selected. The two package jobs retain their
logs and available manifests, with per-case results and cleanup evidence for Kafka. These
artifacts are retained for seven days.

`Directory.Packages.props` is a shared input and selects full coverage. Packaged README
and recipe changes select the package proofs whose inputs include them.

`npm --prefix eng/repository run check` verifies environment scopes, the scope summary, gate
handling of check results and local references. After installing root Nx and restoring the solution,
`npm run check:nx` verifies that Nx discovers every declared native test project and that
each is in `Gloaming.slnx`. It exercises affected selection for a leaf library, downstream
application consumers, shared C# fixtures, retained/deleted JSON fixture paths and central
build inputs. It checks the native references of the Wholesale runtime project and its
runtime-only inputs. It also checks that provider test targets disable caching.
These graph proofs do not execute providers or GitHub's expression engine. Affected
selection of a removed project/topology change is covered in CI by the shared `*.csproj`
full-run fallback. New ordinary test projects do not require a family lane entry.

**Repository integrity** is the aggregate gate. It requires successful selection and successful
results from every selected environment check. It accepts a skipped result when scope or event
policy excludes that check, such as Wholesale runtime on pushes. Once those results are verified,
the gate runs repository validation. When .NET checks are selected, it builds the solution once
and uses those outputs for architecture checks and Nx-selected tests. The
[codec adoption test](../../src/Gloaming.Events/tests/CodecAdoptionTests/CompatibilityTests.cs)
starts the actual sample and checks its output. The three
environment checks have individual results in **CI**.
