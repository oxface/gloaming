# Optional messaging worker hosting

2026-10-22 UTC. Base: `d63f567`; active branch and checkout remain
`docs/gloaming-library-audit` in `/home/oxface/repos/modulith-foundry`.

## Outcome and ownership

Move the six inbox/outbox worker, options and registration files into
[Gloaming.Messaging.EntityFrameworkCore.Hosting](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Hosting/README.md).
The EF messaging package drops its direct Hosting and Logging dependencies. Provider
implementations, callable roles, durable failure types, records, validation and transactions
stay in their existing packages. The current inventory is 22 libraries.

This is a package/interface simplification around existing execution behavior.
**No new reusable runtime mechanism is introduced or proven.** Registration remains explicit;
the old package never started workers merely by being referenced. Consumers retain the
choice to call operations directly, host their own loops or select the provided workers.

The moved types use the `Gloaming.Messaging.EntityFrameworkCore.Hosting` namespace. Existing
callers add the package reference and namespace import. Worker logger categories move with
their namespace; category-specific host filters require adjustment. Public polling options,
fresh scopes, startup validation, cancellation and fatal accounting/completion handling are
retained. No schema, fixture, persisted message or migration changes.

Existing hosts and provider tests explicitly select the hosting package. The combined
OutboxDemo and InboxDemo assemblies still contain host composition, and the producer demo
currently references OutboxDemo. That sample graph therefore selects hosting even when its
particular process only produces messages. Sample consolidation remains a separate pass;
this increment proves persistence-only adoption with an independent package consumer.

## Consumer and proof changes

- [Hosting package](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Hosting/README.md):
  provider-independent sequential worker policy and native hosting obligations.
- [Architecture checks](../../tests/ArchitectureTests/AdoptionDependencyTests.cs): exact
  dependency declarations and optionality relative to callable/provider/workflow packages.
- [Package-only hosting consumer](../../tools/release/consumers/Hosting.cs): supply callable
  roles, start both workers and observe fresh per-attempt scopes without a database provider.
- [Release verifier](../../tools/release/verify.ts): five fresh-cache consumers and an
  explicit Hosting dependency assertion. Core, events and PostgreSQL consumers must omit
  Hosting abstractions; hosting/all-packages consumers select them.

The PostgreSQL consumer retains actual ownership/outbox/audit transactions, rather than
merely checking a project declaration. Existing provider tests retain storage, startup,
fresh-scope, cancellation and fatal-supervision assertions. Real-process tests retain native
host recovery and shutdown evidence. No test obligation is retired by the package move.

Current setup documents point to the hosting package. Historical source links for moved
workers target the immutable `d63f567` baseline. Existing family/architecture/package CI lanes
already select the new project and affected consumers; workflow simplification remains later.

## Verification

Fresh results for this working tree:

| Check | Result |
| --- | --- |
| Architecture consumer graph build | Passed with zero warnings/errors; the updated solution also restores. |
| Architecture tests | All 113 passed, including the new Hosting optionality/provider-independence rule and updated dependency declarations. |
| Outbox PostgreSQL suite | All 108 passed, including startup, fresh-scope retry, cancellation, unconfirmed accounting/completion and native host supervision. |
| Inbox PostgreSQL suite | All 93 passed, including keyed bindings, fresh scopes, rollback and accounting-fault supervision. |
| Real worker processes | All six startup, restart, competing processing, killed-host recovery and graceful-stop cases passed against PostgreSQL/RabbitMQ. |
| Inbox demo host supervision | All three configured native-host supervision cases passed. |
| Wholesale durable local delivery | All ten cases passed with the selected workers, module-owned public intake and existing workflow protocol. |
| Package archives and consumers | All 22 archives and five fresh-cache package-only consumers passed. Core/events each resolved two Gloaming packages; PostgreSQL seven; hosting four; all-packages 22. Every consumer had zero project references. |
| Source relocation | All six moved files match the original tokens after namespace substitution; only statement spacing also changed. |
| Tooling and source checks | Release-tool TypeScript/Prettier, changed-file CSharpier, the standalone C# consumer formatter, full-solution native style/analyzers and whitespace checks passed. |

The package verifier confirmed that only hosting/all-packages consumers restore
`Microsoft.Extensions.Hosting.Abstractions`. The PostgreSQL consumer executed its existing
ownership/outbox/audit transactions without it. The hosting consumer started both workers
with caller-supplied roles and observed different scoped instances on successive attempts.

Package proof version: `1.0.0-local.1792705899837.g1b4ceb9a`. Local evidence:
`/home/oxface/.cache/tmp/gloaming-package-proof-UtWYTz/manifest.json`, SHA-256
`a083045e32540ed4cc2b068fe4311fc7a9bd1a7cf693fd60b88db7ebde913862`.
Detailed logs use `/tmp/gloaming-worker-hosting-*.log`. The disposable package-proof
PostgreSQL server was stopped and auto-removed after verification. Current documentation
links and active project references resolve; the staged snapshot remains unchanged.

These are new local results: 333 test cases plus the archive/package-consumer checks.
Earlier OTLP collection, live broker-outage, restricted RLS and retained-deployment results
remain historical evidence; they were not rerun for this package move. This does not claim
remote CI success, a new release sign-off or completion of the broader publication cleanup.

## Remaining gaps

Review the [new project dependencies](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Hosting/Gloaming.Messaging.EntityFrameworkCore.Hosting.csproj),
[outbox registration](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Hosting/OutboxWorkerServiceCollectionExtensions.cs),
[inbox registration](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Hosting/InboxWorkerServiceCollectionExtensions.cs),
the two loops/options and consumer imports together. This slice remains
unstaged and uncommitted pending review; approval for `d63f567` does not approve this change set.
The initially empty staged snapshot is preserved.

Workflow package consolidation, demo dependency cleanup, Wholesale reduction, template
retirement and broader documentation/test/CI cleanup remain open. Native worker supervision
does not supply transport reconnection, arbitrary-code shutdown deadlines, trusted operation
context or a human workflow engine. Umbrine implementation remains separate.
