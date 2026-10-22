# Local Gloaming package development

Date: 2026-10-23. Branch: `feat/local-package-development`.
Base: `1582e77849088e243881c82a37c0a42b2a11a322`.

Subsequently consolidated into `docs/gloaming-library-audit` in the main checkout, together
with [thin adapter retirement](thin-adapter-retirement.md). The current verifier expects
21 packages. The original 23-package executions below remain dated evidence; verification
of the combined result is recorded in the [audit progress](gloaming-library-audit.md#implementation-progress).

## Outcome

`npm --prefix tools/release run pack` creates an independently consumable local snapshot
without requiring PostgreSQL, npm dependencies or remote Gloaming publication. The default
feed is `.artifacts/local-feed`; `--output` selects another directory. Paths resolve from
the checkout root even when the command is invoked elsewhere.

Every invocation derives the base version from `src/Directory.Build.props`, adds a unique
timestamp/random prerelease suffix and passes the same Version/PackageVersion to all library
packs. A leading letter keeps the random identifier valid when its bytes happen to form a
numeric string with leading zeroes. Transitive Gloaming dependencies therefore resolve at
the same snapshot version. Ordinary restore can still need nuget.org for third-party
packages and vulnerability information.

The command builds every library into temporary output before copying archives to the
persistent feed. Copying refuses existing files. A completed snapshot includes its version,
feed and package inventory in `gloaming.<version>.json`. Build failures publish nothing;
ordinary copy failures remove files copied by that attempt. Interrupted copying can leave
an incomplete version, so the reported completion remains the consumer handoff.

The [consumer guide](../../tools/release/README.md#consume-from-another-repository) describes
repository-local source mapping and exact version selection. The command does not edit
consumer files, global NuGet settings, Git staging or application data.

## Review files

- [pack.ts](../../tools/release/pack.ts): command arguments, version generation, native
  pack calls and feed handoff.
- [packages.ts](../../tools/release/packages.ts): shared library discovery, used by both
  ordinary packing and the adoption proof. Sample/template projects are excluded.
- [verify.ts](../../tools/release/verify.ts): invokes the real pack command, checks its
  completed snapshot and retains independent archive/dependency/consumer assertions.
- [Tool guide](../../tools/release/README.md) and
  [development entry](../development.md#local-package-development): everyday invocation,
  feed configuration and the separate full verification command.

The existing Package adoption CI workflow already selects `tools/release/**` and invokes
the full verifier, so it now exercises this command without another pack job. The verifier
retains its reviewed 23-package inventory check. No dependency or CI YAML change was needed.

## Fresh verification

All executions below used Node 24.21.0 and .NET SDK 10.0.401. PostgreSQL was a disposable
18.6 container with no persistent volume or application database; it was stopped and removed
after verification.

| Check | Result |
| --- | --- |
| TypeScript and formatting | `npm --prefix tools/release run check` passed. |
| Default pack without a database setting | All 23 library packages and one completed manifest produced at `1.0.0-local.1792703922135.g5c0fc8f6`. |
| Repeated pack from outside the checkout | Explicit relative output resolved to the same feed and produced `1.0.0-local.1792703994764.g1176da32`; all first-snapshot archive/manifest SHA-256 values remained unchanged. The feed contains 46 archives across two versions. |
| Argument handling | Help succeeded; unknown options, positional project input, blank output and missing output value failed. |
| Failed build handoff | A deliberately failing dotnet executable returned failure, preserved existing feed bytes and advertised no completed snapshot. |
| Full artifact/adoption proof | Passed through the new command using temporary paths containing spaces: 23 actual archives and four clean package-only consumers. |

The full proof used `1.0.0-local.1792704070790.g8b785709` and independently checked package
metadata, README rendering, XML documentation, assemblies, internal dependency versions and
selected dependency closure. Core and Events consumers each restored two Gloaming packages;
the PostgreSQL consumer restored eight; the all-packages consumer restored all 23. All
consumers had zero ProjectReferences and used a fresh package cache and local source mapping.
The PostgreSQL consumer exercised the existing ownership/outbox/audit commit, rollback,
failed-later-save and fresh-context recovery assertions.

Evidence retained locally:

- Default and repeat pack logs: `/tmp/gloaming-local-pack-default.log` and
  `/tmp/gloaming-local-pack-update.log`.
- Full proof log: `/tmp/gloaming-local-adoption-proof.log`.
- Full manifest: `/tmp/gloaming local verification/gloaming-package-proof-AohDWY/manifest.json`.
  SHA-256: `8e4b6ff704cdc1caa31b885b8bded3eb4af73788b51d71e0746d082c49bf534f`.

The initial restricted npm install failed DNS resolution, and the initial restricted pack
stopped during restore after building two libraries. No snapshot was published by that pack.
Normal network/native-process access resolved the environment failures; vulnerability checks
were not disabled. Only the later complete executions above count as successful verification.

## Scope and remaining work

**No new reusable runtime mechanism was proven.** This increment establishes developer
distribution tooling and fresh package-adoption evidence for unchanged Gloaming mechanisms.
No library interface, implementation, schema, fixture, template or sample policy changed.

Umbrine source/configuration remains untouched. Its actual foundation behavior and package
selection still need implementation in that repository. Package simplification, sample/test
reduction and broader documentation cleanup remain separate increments. Symbol/debugger
setup and remote release publication are not established by this package-consumption proof.

The original isolated pass preserved the architecture audit unchanged. The work is now
combined there at the owner's request, with its initially empty staged snapshot preserved.
Changes remain unstaged and have no commit, push or pull request.
