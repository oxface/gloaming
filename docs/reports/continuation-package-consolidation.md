# One bounded continuation package

2026-10-22 UTC. Base: `ffc6ec2`; work remains on `docs/gloaming-library-audit` in
`/home/oxface/repos/modulith-foundry`.

## Outcome and ownership

Retire the standalone `Gloaming.Messaging.Workflows` package and move its eight definition,
turn, identity and snapshot source files into
[Gloaming.Messaging.Workflows.EntityFrameworkCore](../../src/Gloaming.Messaging/Gloaming.Messaging.Workflows.EntityFrameworkCore/README.md).
Keep both existing namespaces and the EF package/assembly identity. The definition package's
friend grant disappears; its internal execution data stays internal in the combined assembly.
No new public execution/inspection abstraction is introduced. The inventory becomes 21 libraries.

This preserves an existing reusable mechanism and simplifies its package boundary.
**No new reusable runtime mechanism is introduced or proven.** Definitions could previously
execute only through the friend EF assembly. Consumers still select the mechanism explicitly;
plain Messaging, persistence and optional Hosting remain independent.

The trade-off is an EF dependency for definition-only adopters. Pure business decisions and
Contracts can remain consumer code outside those bindings. If a real adopter needs definitions
in an EF-free assembly, revisit the seam with an executable public-interface proof. That need
is not established by the current adopters, and no replacement runtime is proposed here.

## Consumer and compatibility changes

Existing EF consumers keep their references and imports. Definition tests and other direct
definition-package consumers select the combined EF package instead. Definition CLR namespaces
are unchanged while their assembly identity changes. There are no external consumers requiring
binary compatibility; no forwarding package is added.

The eight relocated files are preserved byte for byte. Existing runner, model, save guards,
row CLR identity, consumer migrations, fixtures, JSON contracts and definition/continuation
names remain unchanged. Native transaction/save/commit ownership and admission are retained.
One outgoing request per turn, one pending continuation, explicit input pauses and duplicate
outcome retention remain the bounded protocol. No human workflow engine or scheduler is added.

The package-only [continuation consumer](../../tools/release/consumers/Workflows.cs) owns its
PostgreSQL context, transaction, outbox, definition and save guards. It exercises rollback,
committed start/reply, fresh-context recovery and duplicate outcome handling from real archives.
The archive verifier checks the selected package closure without a standalone definition
package, project references, Hosting or transport dependencies.

The separately compiled earlier/current deployment fixtures are retained. They exercise
application definition and durable-wire compatibility against this composition; they do not
prove binary compatibility with an older Gloaming assembly. Historical source links use the
immutable pre-consolidation checkpoint; current setup points to the combined package.

## Verification

Fresh results for this working tree:

| Check | Result |
| --- | --- |
| Architecture consumer build and solution restore | Passed; build has zero warnings/errors. |
| Definition validation | All eight existing public-interface cases passed against the combined package. |
| Continuation PostgreSQL suite | All 38 execution, serialization, rollback, competing-turn and atomic inbox/outbox composition cases passed. |
| Separately compiled deployment fixtures | All seven retained-wire, definition-version, duplicate and process-recovery cases passed. |
| Architecture | All 113 cases passed with one combined assembly, updated declarations and independent callable/provider/hosting packages. |
| Wholesale durable local delivery | All ten reservation/admission/compensation cases passed without changing module behavior. |
| Package archives and adopters | All 21 archives and six fresh-cache package-only consumers passed. Gloaming closures: core two, events two, PostgreSQL seven, hosting four, continuations five, all-packages 21. All consumers had zero project references. |
| Source and tooling | Eight relocated files match their original bytes. Release-tool TypeScript/Prettier, CSharpier, standalone C# consumer formatting, full-solution style/analyzers and whitespace checks passed. |

These are **176 fresh test cases**, plus archive and executable package-adoption checks.
The continuation package consumer proved definitions and execution come from the same
assembly, executed native rollback/commit and fresh-context recovery, and confirmed a
retained duplicate outcome does not rerun its continuation. Its assets omit the retired
definition package, Hosting abstractions and the RabbitMQ client.

The former architecture assertion that definitions have no EF dependency is deliberately
retired with that dependency promise. The replacement checks preserve optionality from
ordinary Messaging, provider and Hosting packages and exclude transport/module coupling.
No runtime behavior case is removed. Existing source, fixtures and migrations are retained.

Package proof version: `1.0.0-local.1792707457436.g2225e755`. Local evidence:
`/home/oxface/.cache/tmp/gloaming-package-proof-HFJXku/manifest.json`, SHA-256
`f863158727dbdb9270481c77584aa02193b1c7f3402a19a20961c58c006c72f3`.
Detailed logs use `/tmp/gloaming-continuations-*.log`. The disposable package-proof
PostgreSQL server was stopped and auto-removed after verification. Active project references
and current documentation's local file links resolve; the staged snapshot is unchanged.

Earlier worker-hosting, OTLP collection, live broker-outage and restricted-RLS results remain
historical evidence; they were not rerun for this consolidation. Existing family,
architecture and package CI selectors already cover these paths. No workflow/filter edit is
needed, and local verification does not claim a new remote CI run or complete publication
sign-off.

## Review and remaining work

Review the [combined project](../../src/Gloaming.Messaging/Gloaming.Messaging.Workflows.EntityFrameworkCore/Gloaming.Messaging.Workflows.EntityFrameworkCore.csproj),
relocated public definition types, dependency assertions, consumer references and package-only
adopter together. This slice remains unstaged and uncommitted pending owner review. The
approved worker-hosting slice is committed as `ffc6ec2`; that approval does not cover this
new change set. The initially empty staged snapshot is preserved.

The [audit's remaining slices](gloaming-library-audit.md#remaining-cleanup-slices) estimate
8–10 cleanup increments after this slice. Messaging/other family examples, Wholesale
reduction, generator retirement, documentation pruning, test/CI cleanup and publication
review remain open. Umbrine implementation is separate. No new runtime guarantee, provider,
deployment supervision or alternate workflow executor is selected.
