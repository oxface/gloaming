using System.Diagnostics;
using System.Net;
using Gloaming.Samples.Wholesale.Api;
using Gloaming.Samples.Wholesale.ServiceDefaults;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Gloaming.Samples.Wholesale.IntegrationTests.Api;

public sealed partial class CompositionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeHostTelemetryCorrelatesCatalogRequestDatabaseAndFailure(
        bool databaseFault
    )
    {
        var activities = new List<Activity>();
        var logs = new List<LogRecord>();
        var metrics = new List<Metric>();
        await using var app = await StartAsync(configureHost: builder =>
        {
            builder.AddServiceDefaults();
            builder.Services.ConfigureOpenTelemetryTracerProvider(tracing =>
                tracing.AddInMemoryExporter(activities)
            );
            builder.Services.ConfigureOpenTelemetryMeterProvider(meter =>
                meter.AddInMemoryExporter(metrics)
            );
            builder.Logging.AddOpenTelemetry(logging => logging.AddInMemoryExporter(logs));
        });
        if (databaseFault)
            await ExecuteAsync(
                app,
                "ALTER TABLE inventory.stock_availability RENAME TO unavailable_stock"
            );
        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/organizations/north-supply/catalog", Token);
        Assert.Equal(
            databaseFault ? HttpStatusCode.InternalServerError : HttpStatusCode.OK,
            response.StatusCode
        );
        // Stop request producers before inspecting native exporter collections.
        await app.StopAsync(Token);
        Assert.True(app.Services.GetRequiredService<TracerProvider>().ForceFlush());
        Assert.True(app.Services.GetRequiredService<MeterProvider>().ForceFlush());
        Activity request = Assert.Single(
            activities,
            activity =>
                activity.Kind == ActivityKind.Server
                && Equals(
                    activity.GetTagItem("http.route"),
                    "/organizations/{organization}/catalog"
                )
        );
        Assert.Contains(
            activities,
            activity =>
                activity.Source.Name == "Npgsql"
                && activity.Kind == ActivityKind.Client
                && activity.TraceId == request.TraceId
                && activity.ParentSpanId == request.SpanId
        );
        Assert.Contains(metrics, metric => metric.Name == "http.server.request.duration");
        if (databaseFault)
        {
            Assert.Equal(ActivityStatusCode.Error, request.Status);
            Assert.Contains(
                activities,
                activity =>
                    activity.Source.Name == "Npgsql"
                    && activity.TraceId == request.TraceId
                    && activity.Status == ActivityStatusCode.Error
            );
            Assert.Contains(
                logs,
                log => log.TraceId == request.TraceId && log.LogLevel == LogLevel.Error
            );
        }
    }
}
