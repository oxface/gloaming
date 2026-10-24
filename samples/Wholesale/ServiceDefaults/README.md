# Wholesale native ServiceDefaults

`builder.AddServiceDefaults()` is editable sample host composition, explicitly invoked by
the Wholesale HTTP startup branch and Worker. The independent Messaging producer/worker
hosts own their [separate native composition](../../../src/Gloaming.Messaging/samples/Delivery/ServiceDefaults/Extensions.cs),
with messaging source/meter subscriptions and role resources in their Program files.
It registers native OpenTelemetry logging (retaining console
providers), ASP.NET Core/outgoing HTTP/runtime metrics and request/outgoing HTTP/Npgsql
traces, including Npgsql metrics. Health request traces are filtered to reduce probe noise;
health logs/metrics and database instrumentation are not globally disabled.

OTLP export is enabled only when `OTEL_EXPORTER_OTLP_ENDPOINT` is configured. Aspire supplies
it locally; standalone hosts may configure their own collector. The package carries no
sample module or shared application runtime dependency. No technical library references it. Consumers
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
uses the same registration without an AppHost. This consumer-owned setup follows
[native ServiceDefaults](https://aspire.dev/get-started/csharp-service-defaults/); Gloaming
does not require this project.

The [telemetry tests](../IntegrationTests/Api/TelemetryTests.cs) use native in-memory exporters
around actual HTTP/PostgreSQL operations. [Runtime tests](../RuntimeComposition.Tests)
exercise AppHost composition; they have separate infrastructure requirements.
