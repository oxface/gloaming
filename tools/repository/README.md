# Repository tooling

Requires Node.js 24.21 or newer within the 24.x line; CI pins 24.21.0.
Install the locked tools and run the lightweight integrity checks from any checkout:

```sh
npm ci --prefix tools/repository --ignore-scripts
npm --prefix tools/repository run check
```

`check` verifies TypeScript types, tooling formatting, reference/CI-selection regression cases and
local references. CI runs it for every change, including documentation-only PRs. The
pre-commit hook selects it for documentation, project and tooling inputs.

## Local commit checks

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

`npm --prefix tools/repository run references` discovers the repository from the working
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

## CI selection

The [reusable scope workflow](../../.github/workflows/changes.yml) uses SHA-pinned
[`dorny/paths-filter`](https://github.com/dorny/paths-filter) with
[one explicit path map](../../.github/ci-paths.yml). PR detection uses the action's paginated
GitHub API path; push detection uses Git. Only read permissions are granted. Reference
validation does not infer dependencies or select test lanes.

The action's `some-with-excludes` mode applies each filter's exclusions across every positive
pattern. Shared build/project/CI inputs and paths outside mapped roots request full coverage,
except for the central package file described below. Manual dispatch also requests full
coverage. Detection failures fail required Repository integrity. Changes inside mapped roots
follow their explicit filters.

For version-only changes to `Directory.Packages.props`,
[`ci-packages.ts`](ci-packages.ts) compares the central file with the PR base commit or the
pre-push commit. It finds tracked projects with direct `PackageReference` declarations for the
changed IDs and selects their lanes through the same folder map. Package IDs compare without
case sensitivity. This supplements every other changed-path selection; it cannot suppress a
lane selected by another file. Repository .NET checks still run for central package edits.
New/removed declarations, structural edits, unknown ownership, shared package imports,
unsupported expressions and missing comparison commits conservatively select all lanes.
The selector reads literal declarations; it does not evaluate MSBuild or restore dependencies.

Kafka has its own path/output selection for the four libraries in its native messaging
closure, its proof tooling/recipe and the DI package used by its generated project. Wholesale
integration follows its actual library closure. Source changes in unconsumed agent/evaluation
libraries no longer select these consumers. The ordinary package adoption job remains a complete package suite
whenever selected; individual adoption profiles are not independently scheduled. External
package inputs of generated adoption projects are listed explicitly in the selector and
checked against their owning proof scripts.

Repository integrity always runs lightweight tooling/reference checks and commitlint;
its .NET checks run for relevant code/build changes. Update the path map when project
dependencies, linked fixtures, consumers or adoption proofs change. Wholesale runtime includes
the library implementations in its actual project-reference closure. Family-only tests and
unconsumed optional libraries retain their focused lanes. Documentation-only changes do not
select runtime unless dispatched manually.

`npm --prefix tools/repository test` checks representative path/package selections and walks
the CI consumers' literal ProjectReferences to verify that every consumed project selects its
lane for both source and package updates. It also checks generated adoption package inputs,
fallbacks and CLI output. The production selector and tests share the pinned action's picomatch
major version with `dot: true` and `some-with-excludes` matching. These are local policy checks,
not execution of GitHub's expression engine or a general MSBuild evaluator.

To inspect central package selection locally, set `CI_BASE_SHA` to a full comparison commit SHA
and run `node tools/repository/ci-packages.ts` with the locked repository tools installed.
It prints the selected lanes and fallback reason. CI also receives the individual booleans
through `GITHUB_OUTPUT`.

Expensive jobs keep stable check names and use job conditions. The reusable selection workflow
still has lightweight setup overhead for documentation-only changes. Host CI remains the
authority for scheduling; local checks do not establish runner timings.
