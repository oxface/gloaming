# Native OpenTelemetry subscriptions

Optional .NET 10 convenience extensions for collecting Gloaming messaging traces and
metrics. The sole package dependency is `OpenTelemetry.Api`; no SDK, exporter, hosting,
EF, PostgreSQL, RabbitMQ or other Gloaming project is required by this adapter.
The technical messaging packages do not depend on it.

Reference this project/package in the collecting host. With the host's existing native
OTel setup, including editable Aspire-style ServiceDefaults:

```csharp
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

builder.AddServiceDefaults();
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddGloamingMessaging())
    .WithMetrics(metrics => metrics.AddGloamingMessaging());
```

The overload on TracerProviderBuilder subscribes to activity source `Gloaming.Messaging`;
the overload on MeterProviderBuilder subscribes to meter `Gloaming.Messaging`. Each returns
the native builder for chaining. They may be enabled independently. A null builder throws
ArgumentNullException. The host still owns provider lifetime, resources, sampling, metric
aggregation, logging and exporter configuration. No provider is built by these extensions.

Enable other instrumentations explicitly; for example, a RabbitMQ publishing/intake host
can chain `.AddSource("RabbitMQ.Client.Publisher", "RabbitMQ.Client.Subscriber")` on its
tracer builder. The helper does not select a transport, database provider or ServiceDefaults
implementation. Without this adapter, native AddSource/AddMeter with the same names remain
equivalent supported setup.

See [the observability contract](../docs/observability.md) for span/metric meanings, retained
links and limitations. [Producer](../samples/MessagingProducerDemo/Program.cs) and
[worker](../samples/MessagingWorkerDemo/Program.cs) hosts exercise both extensions;
their process suite verifies actual OTLP export with real PostgreSQL/RabbitMQ and a Collector.
