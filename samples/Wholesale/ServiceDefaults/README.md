# Wholesale native ServiceDefaults

`builder.AddServiceDefaults()` is editable sample host composition, explicitly invoked by
the Wholesale HTTP startup branch and the independent Messaging producer/worker hosts.
The latter reuse this project without importing Wholesale business modules and add their
own messaging source/meter subscriptions and role resources in their Program files.
It registers native OpenTelemetry logging (retaining console
providers), ASP.NET Core/outgoing HTTP/runtime metrics and request/outgoing HTTP/Npgsql
traces, including Npgsql metrics. Health request traces are filtered to reduce probe noise;
health logs/metrics and database instrumentation are not globally disabled.

OTLP export is enabled only when `OTEL_EXPORTER_OTLP_ENDPOINT` is configured. Aspire supplies
it locally; standalone hosts may configure their own collector. The package carries no
sample module or Foundry core dependency. No technical library references it. Consumers
choose sampling, log levels, exporter credentials, retention and production endpoints through
native configuration/source. Native SQL instrumentation can include statement text; this
sample does not enable EF sensitive-data logging or record query parameter values deliberately.

This project registers the native `self` liveness check. The HTTP host explicitly maps
anonymous/tenantless health endpoints and adds its own database/schema readiness check.
Keeping that policy in the host avoids making ServiceDefaults depend on tenancy or database
business rules. Finite setup returns before ServiceDefaults is registered. The worker hosts
register these checks without creating an HTTP health endpoint of their own.

There is no global HTTP resilience/retry handler, automatic transaction/save/dispatch,
custom telemetry framework or mandatory Aspire runtime dependency. Standalone HTTP startup
uses the same registration without an AppHost. This is a proven template recipe based on
[native ServiceDefaults](https://aspire.dev/get-started/csharp-service-defaults/), not a new
Foundry library segment.

The [telemetry proofs](../IntegrationTests/Api/TelemetryTests.cs) use native in-memory
exporters around real HTTP/PostgreSQL operations. The
[runtime report](https://github.com/oxface/modulith-foundry/blob/bd2adb7dea58de304aa2eb3df3c587c6fe348320/docs/reports/e3-6-runtime-composition.md) separately records actual
OTLP delivery into the Aspire dashboard and remaining limits.
