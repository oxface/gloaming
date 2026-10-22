using System.Text;
using System.Text.Json;
using DotNet.Testcontainers.Builders;
using DotNet.Testcontainers.Containers;

namespace ModulithFoundry.Samples.MessagingWorkerDemo.Tests;

// A real OTLP receiver/exporter, not a replacement SDK or an in-process child-host approximation.
internal sealed class OtlpCollector : IAsyncDisposable
{
    private readonly IContainer container = new ContainerBuilder(
        "otel/opentelemetry-collector-contrib:0.162.0"
    )
        // The scratch image has no writable output directory; create only this container's proof files.
        .WithCreateParameterModifier(parameters => parameters.User = "0:0")
        .WithEnvironment(
            "PROOF_CONFIG",
            """
            receivers:
              otlp:
                protocols:
                  http:
                    endpoint: 0.0.0.0:4318
            exporters:
              file/traces:
                path: /proof/traces.json
                create_directory: true
                flush_interval: 100ms
              file/logs:
                path: /proof/logs.json
                create_directory: true
                flush_interval: 100ms
              file/metrics:
                path: /proof/metrics.json
                create_directory: true
                flush_interval: 100ms
            extensions:
              health_check:
                endpoint: 0.0.0.0:13133
            service:
              extensions: [health_check]
              pipelines:
                traces:
                  receivers: [otlp]
                  exporters: [file/traces]
                logs:
                  receivers: [otlp]
                  exporters: [file/logs]
                metrics:
                  receivers: [otlp]
                  exporters: [file/metrics]
            """
        )
        .WithCommand("--config=env:PROOF_CONFIG")
        .WithPortBinding(4318, true)
        .WithPortBinding(13133, true)
        .WithWaitStrategy(
            Wait.ForUnixContainer().UntilHttpRequestIsSucceeded(request => request.ForPort(13133))
        )
        .Build();

    internal Task StartAsync(CancellationToken cancellation) => container.StartAsync(cancellation);

    internal Dictionary<string, string> Environment =>
        new(StringComparer.Ordinal)
        {
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] =
                $"http://{container.Hostname}:{container.GetMappedPublicPort(4318)}",
            ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
            ["OTEL_BSP_SCHEDULE_DELAY"] = "100",
            ["OTEL_BLRP_SCHEDULE_DELAY"] = "100",
            ["OTEL_METRIC_EXPORT_INTERVAL"] = "100",
        };

    internal Task<List<(string Service, JsonElement Record)>> SpansAsync(CancellationToken token) =>
        ReadAsync("traces", "resourceSpans", "scopeSpans", "spans", token);

    internal Task<List<(string Service, JsonElement Record)>> LogsAsync(CancellationToken token) =>
        ReadAsync("logs", "resourceLogs", "scopeLogs", "logRecords", token);

    internal Task<List<(string Service, JsonElement Record)>> MetricsAsync(
        CancellationToken token
    ) => ReadAsync("metrics", "resourceMetrics", "scopeMetrics", "metrics", token);

    private async Task<List<(string Service, JsonElement Record)>> ReadAsync(
        string file,
        string resourceKey,
        string scopeKey,
        string recordKey,
        CancellationToken token
    )
    {
        string text = Encoding.UTF8.GetString(
            await container.ReadFileAsync($"/proof/{file}.json", token)
        );
        // Ignore an unfinished final line while the collector is still writing.
        text = text[..(text.LastIndexOf('\n') + 1)];
        var records = new List<(string Service, JsonElement Record)>();
        foreach (string line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            using var document = JsonDocument.Parse(line);
            foreach (var resource in document.RootElement.GetProperty(resourceKey).EnumerateArray())
            {
                string name = Attribute(resource.GetProperty("resource"), "service.name")!;
                foreach (var scope in resource.GetProperty(scopeKey).EnumerateArray())
                foreach (var record in scope.GetProperty(recordKey).EnumerateArray())
                    records.Add((name, record.Clone()));
            }
        }

        return records;
    }

    internal static string? Attribute(JsonElement record, string name)
    {
        if (!record.TryGetProperty("attributes", out var attributes))
            return null;

        foreach (var item in attributes.EnumerateArray())
            if (item.GetProperty("key").GetString() == name)
                return item.GetProperty("value").GetProperty("stringValue").GetString();

        return null;
    }

    public ValueTask DisposeAsync() => container.DisposeAsync();
}
