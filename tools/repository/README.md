# Repository tooling

Requires Node.js 24; CI pins 24.21.0. Install the existing locked commit/hook tools with:

```sh
npm ci --prefix tools/repository --ignore-scripts
```

Commitlint uses `commitlint.config.mjs`; the existing archive verifier remains Python and
checks the frozen original manifest.

Commit messages follow Conventional Commits: a recognized lowercase type, optional
scope and nonempty subject. Subject capitalization is unrestricted, so both
`chore: bump dependencies` and `chore: Bump dependencies` pass. Other rules from
`@commitlint/config-conventional` remain enabled. CI and the local commit hook use
the same configuration; dependency bots receive no validation bypass.

## CI selection

The [reusable scope workflow](../../.github/workflows/changes.yml) uses SHA-pinned
[`dorny/paths-filter`](https://github.com/dorny/paths-filter) with
[one explicit path map](../../.github/ci-paths.yml). There is no repository-owned selector,
project parser or selection test suite. PR detection uses the action's paginated GitHub API
path; push detection uses Git. Only read permissions are granted.

The action's `some-with-excludes` mode excludes Markdown across every positive pattern.
Shared build/project/CI inputs and paths outside mapped roots request full coverage.
Manual dispatch also requests full coverage. Detection failures fail required Repository
integrity. Changes inside mapped roots follow their explicit filters.

Markdown is documentation, including family-local docs and sample READMEs. Archive-only
changes require checksum validation but never schedule archived tests. Repository integrity
always runs commitlint and archive verification. Its .NET checks run for relevant code/build
changes. Update the path map when project dependencies, linked fixtures, new consumers or
template source snapshots change; dependency discovery is deliberately manual. Wholesale
runtime retains sample/shared-input
and manual execution rather than running for every library-only change.

All expensive jobs keep their existing check names and use job conditions, preserving results
for PR rulesets while avoiding unnecessary runner/container startup. The reusable selection
workflow is invoked by each caller, so docs-only CI still has lightweight setup overhead.
Manual dispatch runs full coverage. Host CI remains the authority for workflow scheduling;
local filter probes validate mapping behavior, not GitHub runner timings.
