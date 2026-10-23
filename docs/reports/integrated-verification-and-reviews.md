# Integrated verification and independent reviews

Date: 2026-10-23. Audit base: `1582e77849088e243881c82a37c0a42b2a11a322`.
Reviewed checkpoint: `3c4f9a9a09565be936d0b4c05caf9a1a75298de3`.
Status: local verification and independent reviews passed; report recorded in owner-approved
checkpoint `c5acb8e`. The reviewed implementation remains `3c4f9a9`; later changes have their
own evidence. No push or publication is performed.

The owner approved evidence/proof consolidation, then requested continuing in this branch.
The next test cleanup pass must distinguish Gloaming public-contract proofs from adopter
wiring and Wholesale business assertions, identify actual library gaps, and assess test
discoverability/layout. The reviewed checkpoint's native inventory had 26 family-local projects, two cross-family
projects and two Wholesale projects; no family project directly references Wholesale. This
inventory is not a proof of complete public-contract coverage. Test locations remain unchanged.

This batch runs the complete retained graph and separate Standards, Spec and architecture
reviews over the audit diff. It is not the later owner-selected extra documentation/test pass
or the separate-session fresh-context sweep. No implementation fixes were needed.
No new reusable mechanism is established by a passing verification run.

The fixed review range was
`git diff 1582e77849088e243881c82a37c0a42b2a11a322...3c4f9a9`.
The base is an ancestor; the diff contains 748 changed files. The originating conversation,
accepted boundary/compatibility clarifications and current audit supplied the specification.

## Standards

Independent read-only review of the fixed audit diff found no actionable documented-standard
violations and no worthwhile new smell concerns. It inspected package/hosting changes, repository
and release tooling, sample adjustments and proof ownership against AGENTS, conventions,
dependency guidance and build configuration. Purposeful native composition in independent
adopters did not justify extracting another shared abstraction. Mechanically pinned links,
authorized retirements and unchanged renderer implementation were outside the introduced scope.
The reviewer ran no builds/provider tests and made no full-correctness claim.

## Spec

Independent read-only review found no actionable missing/partial requirement, scope creep or
incorrectly implemented requirement. The inspected changes preserve worker execution and
continuation definitions, independent native context composition and relevant event-store,
route-selection and populated Sales migration proofs. Purchasing/Dispatch business policy and
generated-template promises are deliberately retired. Explicit bounded transaction/handler
execution is authorized. The extra documentation/test pass and later fresh-context sweep
remain open. The reviewer inspected implementation and relevant proof transfers, not every
retained assertion, and ran no fresh provider/runtime checks.

Standards: **0 findings**; Spec: **0 findings**. Neither axis establishes publication sign-off.

## Architecture and isolation

A separate read-only reviewer found no actionable architecture/isolation breach. The optional
hosting package preserves callable persistence without Hosting dependencies and leaves host
supervision/transport policy explicit. Combining continuation definitions with their sole runner
removes an unproven seam, with the EF dependency documented. Native JSONB and source/meter
configuration replace thin adapters without moving substantial technical complexity to consumers.
Wholesale's local relays use public Contracts and independently committed retained intake;
they do not imply shared-transaction atomicity. Inspected proof transfers retain current owners.

This was a targeted architectural review, not an exhaustive line-by-line implementation audit
or fresh execution. No additional reusable mechanism was justified. Provider-free definitions,
generic delivery adapters, automatic tenant admission and workflow scheduling need concrete
consumer evidence before another interface is selected.

## Fresh execution

The complete solution restore/build passed with **zero warnings/errors**. All 30 native test
projects passed from that build, with **1,671 cases and zero failures/skips**:

| Owner | Projects | Passed cases |
| --- | --- | --- |
| Family-local Gloaming contracts/adopters | 26 | 947 |
| Root architecture/native context composition | 2 | 96 |
| Wholesale integration | 1 | 622 |
| Wholesale Aspire/HTTPS/OIDC/browser composition | 1 | 6 |

The test inventory came from the native solution and `IsTestProject` declarations, with no
project omitted. Each project ran serially using `dotnet test --project <path> --no-build
--no-restore --no-progress --no-ansi --output Normal --show-stdout Failed --show-stderr Failed`.
Real PostgreSQL/RabbitMQ used the rootless Podman socket, with Ryuk disabled and native local
HTTPS trust enabled as documented in the [development guide](../development.md). Browser
tests used the installed Chromium. Runtime graphs ran last to avoid interfering container
network teardown. Every result required a successful exit and explicit runner case counts.

The [package verifier](../../tools/release/README.md#full-package-adoption-proof) then passed
all **21 archives and six isolated executable consumers** against its own disposable
PostgreSQL server. Native README rendering, archive metadata/bytes, exact package dependency
closures, fresh-cache restoration and zero ProjectReferences all passed. Its version was
`1.0.0-local.1792752290731.g95360ea7`; manifest SHA-256:
`b6c561cfe214f6d46fea131fcb711eea2c021f3f2ec46c3eca26d251138d6135`.

Repository tooling types/formatting, six reference-checker regression cases and release-tool
types/formatting passed. The final local-reference and whitespace checks passed as well.
Normal checkpoint hooks had already passed CSharpier, .NET style/analyzers and their selected
suites on this same source tree; those hook results are distinct from the complete rerun above.

Local execution evidence, retained for inspection rather than committed as generated output:

- `/tmp/gloaming-final-build.log` and `/tmp/gloaming-final-certificate-check.log`.
- `/tmp/gloaming-final-native-results.json`, `/tmp/gloaming-final-native-progress.log` and
  per-project logs under `/tmp/gloaming-final-native/`.
- `/tmp/gloaming-final-packages.log` and
  `/home/oxface/.cache/tmp/gloaming-package-proof-hJ5fA2/manifest.json` with its sibling archives,
  consumers and command logs.
- `/tmp/gloaming-final-tooling.log`, `/tmp/gloaming-final-release-tools.log` and
  `/tmp/gloaming-final-references.log`; checkpoint hooks: `/tmp/gloaming-evidence-commit.log`.

At completion of this verification run, all **868 owned source/project/fixture hashes** under
`src`, `samples` and `tests` matched the starting checkpoint; the source diff and staged snapshot
were unchanged. The verification batch changed only this report, the
[active queue](gloaming-library-audit.md) and the previous
[slice status](evidence-and-proof-consolidation.md). Subsequent documentation/test cleanup is
recorded separately and is not retroactively certified by this run.

## Disposition and remaining gaps

The integrated local verification/review gate is satisfied for the reviewed checkpoint.
Library mechanisms remain independently selectable; no template runtime or new extraction
candidate was proven. Native application wiring, Wholesale Contracts/business rules and
operational/deployment policy remain consumer-owned. Existing limits and owner-approved
deferrals remain in the owning family contracts; no new deferral is introduced.

Passing all retained cases does not establish complete supported-contract coverage. The next
owner-selected documentation/test pass must assess assertion quality, omissions, duplication
and navigation, including family-local versus root placement. No test relocation is selected.
The owner's separate-session fresh-context sweep remains outstanding. These local results do
not establish hosted CI scheduling, production capacity, Umbrine adoption or publication sign-off.
