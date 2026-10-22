# Native audit mapping and messaging subscriptions

2026-10-22 UTC. Working-tree increment from fetched main `1582e77`, on
`refactor/remove-thin-adapters`, subsequently consolidated into `docs/gloaming-library-audit`
in the main checkout. No external consumer compatibility is required.

## Outcome and ownership

Retire `Gloaming.Auditing.EntityFrameworkCore.Postgres` and
`Gloaming.Messaging.OpenTelemetry`. Their complete implementations delegated to native
configuration already available to their consumers. The supported package inventory becomes
21 libraries. No new reusable mechanism is introduced or proven by this increment.

Audit consumers reference `Gloaming.Auditing.EntityFrameworkCore` and configure their
PostgreSQL JSON storage in the owning context:

```csharp
var audit = modelBuilder.ConfigureAudit("operations", "accepted_log");
audit.Property(row => row.Details).HasColumnType("jsonb");
```

`ConfigureAudit` still supplies the row mapping and subject timeline index. Staging,
transaction association, tracked-change validation and consumer commit ownership remain in
the EF library. Sales, Inventory, Access, the independent audit test consumer and the
package-only PostgreSQL consumer use this setup. No model shape or migration changes.

Messaging hosts use their existing OpenTelemetry dependencies:

```csharp
services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddSource("Gloaming.Messaging"))
    .WithMetrics(metrics => metrics.AddMeter("Gloaming.Messaging"));
```

The source and meter names remain supported contracts. Native diagnostics, operation
boundaries, measurements and trace-context retention remain in the messaging libraries.
SDKs, exporters, sampling and other subscriptions remain consumer policy. The producer,
worker and Wholesale host use the native calls; ServiceDefaults remains editable sample
setup. No template preset or new application convention is introduced.

Remove retired projects from the solution, architecture declarations and package proof;
remove the unused direct OpenTelemetry API version entry. Keep core dependency assertions
and independent provider consumers. Retire assertions about the deleted assemblies alone;
retain the audit, callable telemetry and process/export scenarios that exercise behavior.
Current setup documentation uses native APIs. Dated plans/reports retain their original
evidence with a retirement note, and deleted source links target the immutable Git baseline.

## Verification

Fresh results from the original isolated implementation pass:

- The architecture consumer graph builds without warnings/errors; the complete reduced
  solution restores. All 112 architecture tests pass, including the retained audit core
  independence and provider-consumer dependency checks.
- All 39 independent PostgreSQL audit tests pass with consumer-owned JSONB mapping:
  envelope guards, native commit/rollback and custom row/timeline mapping remain exercised.
- All eight outbox and six inbox callable telemetry cases pass against PostgreSQL.
- Both real-process observability scenarios pass with PostgreSQL, RabbitMQ and an OTLP
  Collector: retained relationships across producer exit, failures and duplicate delivery;
  and continued business processing when the collector is unreachable.
- All 21 Release archives pass metadata, README, XML documentation and dependency-graph
  validation. All four fresh-cache package-only consumers pass: core resolves two Gloaming
  packages, events two, PostgreSQL seven and all-packages 21, each with zero project
  references. The PostgreSQL consumer executes native ownership/outbox/audit transactions.
- Release-tool TypeScript/Prettier checks pass, as do CSharpier and native .NET style/analyzer
  checks for the changed C# files. Changed documentation's local file links resolve; active
  source/sample/test project references resolve. The unrendered template's existing generated
  library paths remain template inputs.

Package proof version: `1.0.0-local.1792704674115.5ce9cef2`. Local evidence:
`/home/oxface/.cache/tmp/gloaming-package-proof-3LO7bO/manifest.json`, SHA-256
`ee6dab817a02bc8aff186ff3bd9006be4e2c3cd1021a8e7b47d0ddfd65c275dc`.
Detailed run logs use `/tmp/gloaming-thin-adapters-*.log`. The first process-test filter
matched zero tests; only the corrected fully qualified class run is counted above.
The disposable package-proof database was stopped and auto-removed after verification.

Existing CI family, architecture and package lanes cover these paths; the shared project/
solution filter already selects the affected checks. No workflow/filter edit is needed for
these two removals. This is local verification, not a new remote CI run or a complete release
sign-off. Broader broker-recovery, RLS and Wholesale business proofs are retained but were not
rerun here; their earlier results remain historical evidence.

## Review and remaining work

Review the deleted adapters, the owning [audit context](../../src/Gloaming.Auditing/tests/AuditPostgresTests/AuditConsumer.cs),
[messaging host subscriptions](../../src/Gloaming.Messaging/samples/MessagingWorkerDemo/Program.cs),
[architecture dependency checks](../../tests/ArchitectureTests/AdoptionDependencyTests.cs)
and [package consumer](../../tools/release/consumers/Postgres.cs) together. These are
breaking package/API removals; existing consumers must replace their references and calls.

The local-package developer command and this cleanup now share the main audit checkout.
The verifier changes are reconciled with the 21-package inventory. The earlier 23-package
results remain historical evidence; the [audit progress](gloaming-library-audit.md#implementation-progress)
records verification of the combined result. Changes remain unstaged and uncommitted.

Optional worker hosting, workflow package boundaries, sample consolidation, template
retirement and broader documentation/test/CI cleanup remain subsequent passes. Umbrine
implementation remains separate. This increment does not claim completion of the
repository publication cleanup or its release/operational proof obligations.
