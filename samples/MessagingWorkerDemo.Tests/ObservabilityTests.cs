using System.Diagnostics;
using System.Text.Json;
using Gloaming.Messaging.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ModulithFoundry.Samples.InboxDemo;
using ModulithFoundry.Samples.OutboxDemo;
using ModulithFoundry.Tests.Infrastructure;

namespace ModulithFoundry.Samples.MessagingWorkerDemo.Tests;

public sealed class ObservabilityTests(PostgreSqlFixture postgres, RabbitMqFixture rabbit)
    : IClassFixture<PostgreSqlFixture>,
        IClassFixture<RabbitMqFixture>
{
    [Fact]
    public async Task RealOtlpRetainsRelationshipsAcrossProcessExitFailuresAndDuplicatePublication()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(120));
        var token = deadline.Token;
        await using var collector = new OtlpCollector();
        await collector.StartAsync(token);
        await using var topology = await WorkerTopology.CreateAsync(
            postgres,
            rabbit,
            token,
            collector.Environment
        );
        await topology.SetupAsync(token);
        var (api, client) = await topology.StartApiAsync(token);
        Guid export;
        using (client)
            export = await WorkerTopology.SubmitAsync(client, 3, token);
        await api.StopAsync(token);

        Guid message = await topology.MessageIdAsync(token);
        string producerContext;
        await using (var read = ExportDbContext.Create(topology.Exports))
            producerContext = (
                await read.Set<OutboxMessageRecord>().SingleAsync(token)
            ).TraceParent!;
        Assert.True(ActivityContext.TryParse(producerContext, null, out var creation));

        // Fail after real transport acceptance. Completion failure retains the lease and
        // permits a later duplicate with a new native send span, not a new delivery identity.
        await SqlAsync(
            topology.Exports,
            """
            CREATE FUNCTION exports.reject_completion() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN IF NEW.dispatched_at IS NOT NULL THEN RAISE EXCEPTION 'proof completion failure'; END IF; RETURN NEW; END; $$;
            CREATE TRIGGER proof_completion BEFORE UPDATE ON exports.outgoing_work
            FOR EACH ROW EXECUTE FUNCTION exports.reject_completion();
            """,
            token
        );
        var firstDispatch = await topology.StartWorkerAsync(
            "dispatch",
            "proof-dispatch-failed",
            token,
            leaseSeconds: 2
        );
        await firstDispatch.WaitForOutputAsync($"Message {message} dispatch attempt failed", token);
        await firstDispatch.WaitForExitAsync(token);
        Assert.Contains(
            "Outbox completion was not confirmed",
            firstDispatch.Output,
            StringComparison.Ordinal
        );
        var intake = await topology.StartWorkerAsync("receive", "proof-intake", token);
        await intake.WaitForOutputAsync(
            $"Delivery {message} retained and acknowledged: Queued",
            token
        );

        string firstIntakeContext;
        await using (var read = RenderDbContext.Create(topology.Rendering))
            firstIntakeContext = (
                await read.Set<InboxMessageRecord>().SingleAsync(token)
            ).TraceParent!;
        Assert.True(ActivityContext.TryParse(firstIntakeContext, null, out var received));
        await SqlAsync(
            topology.Exports,
            "DROP TRIGGER proof_completion ON exports.outgoing_work",
            token
        );
        await WorkerTopology.EventuallyAsync(
            async () =>
                await WorkerTopology.SqlCountAsync(
                    topology.Exports,
                    "SELECT count(*) FROM exports.outgoing_work WHERE lease_until <= clock_timestamp()",
                    token
                ) == 1,
            token
        );
        var secondDispatch = await topology.StartWorkerAsync(
            "dispatch",
            "proof-dispatch-recovered",
            token
        );
        await intake.WaitForOutputAsync(
            $"Delivery {message} retained and acknowledged: AlreadyReceived",
            token
        );
        await secondDispatch.StopAsync(token);
        await intake.StopAsync(token);

        await using (var read = RenderDbContext.Create(topology.Rendering))
            Assert.Equal(
                firstIntakeContext,
                (await read.Set<InboxMessageRecord>().SingleAsync(token)).TraceParent
            );

        // Fail inside actual PostgreSQL handling, after intake has exited; fresh processing
        // must use retained context and recover without an in-memory producer/receiver.
        await SqlAsync(
            topology.Rendering,
            """
            CREATE FUNCTION rendering.reject_job() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'proof processing failure'; END; $$;
            CREATE TRIGGER proof_job BEFORE INSERT ON rendering.jobs
            FOR EACH ROW EXECUTE FUNCTION rendering.reject_job();
            """,
            token
        );
        var firstProcess = await topology.StartWorkerAsync(
            "process",
            "proof-process-failed",
            token
        );
        await firstProcess.WaitForOutputAsync($"Message {message} process attempt failed", token);
        await firstProcess.StopAsync(token);
        await topology.AssertCountsAsync(1, 0, 0, token);
        await SqlAsync(topology.Rendering, "DROP TRIGGER proof_job ON rendering.jobs", token);
        await topology.RetryRenderingAsync(token);
        var secondProcess = await topology.StartWorkerAsync(
            "process",
            "proof-process-recovered",
            token
        );
        await topology.WaitForJobsAsync(1, token);
        await secondProcess.StopAsync(token);
        await topology.AssertCountsAsync(1, 1, 1, token);
        await topology.AssertJobAsync(export, 3, token);
        await topology.AssertQueueEmptyAsync(token);

        await WorkerTopology.EventuallyAsync(
            async () =>
                (await collector.SpansAsync(token)).Any(record =>
                    record.Service == "proof-process-recovered"
                    && Name(record.Record) == "gloaming.inbox.process"
                ),
            token
        );
        var spans = await collector.SpansAsync(token);
        Assert.Contains(
            spans,
            record =>
                record.Service == "proof-api"
                && Id(record.Record, "traceId") == creation.TraceId.ToString()
                && Id(record.Record, "spanId") == creation.SpanId.ToString()
        );

        var failedDispatch = Single(spans, "proof-dispatch-failed", "gloaming.outbox.dispatch");
        var recoveredDispatch = Single(
            spans,
            "proof-dispatch-recovered",
            "gloaming.outbox.dispatch"
        );
        var failedProcess = Single(spans, "proof-process-failed", "gloaming.inbox.process");
        var recoveredProcess = Single(spans, "proof-process-recovered", "gloaming.inbox.process");
        AssertLink(failedDispatch, creation);
        AssertLink(recoveredDispatch, creation);
        AssertLink(failedProcess, received);
        AssertLink(recoveredProcess, received);
        Assert.NotEqual(Id(failedDispatch, "traceId"), Id(recoveredDispatch, "traceId"));
        Assert.NotEqual(Id(failedProcess, "traceId"), Id(recoveredProcess, "traceId"));
        AssertOutcome(failedDispatch, message, "failed", 2);
        AssertOutcome(recoveredDispatch, message, "published", 1);
        AssertOutcome(failedProcess, message, "failed", 2);
        AssertOutcome(recoveredProcess, message, "processed", 1);

        var send = Assert
            .Single(
                spans,
                record =>
                    record.Service == "proof-dispatch-failed"
                    && Id(record.Record, "spanId") == received.SpanId.ToString()
            )
            .Record;
        Assert.Equal(Id(failedDispatch, "spanId"), Id(send, "parentSpanId"));
        Assert.Equal(Id(failedDispatch, "traceId"), Id(send, "traceId"));
        Assert.Contains(
            spans,
            record =>
                record.Service == "proof-intake"
                && record.Record.TryGetProperty("links", out var links)
                && links
                    .EnumerateArray()
                    .Any(link => Id(link, "spanId") == received.SpanId.ToString())
        );

        var logs = await collector.LogsAsync(token);
        foreach (
            var (service, span) in new[]
            {
                ("proof-dispatch-failed", failedDispatch),
                ("proof-process-failed", failedProcess),
            }
        )
            Assert.Contains(
                logs,
                record =>
                    record.Service == service
                    && Id(record.Record, "traceId") == Id(span, "traceId")
                    && Id(record.Record, "spanId") == Id(span, "spanId")
                    && record
                        .Record.GetProperty("body")
                        .GetProperty("stringValue")
                        .GetString()!
                        .Contains("attempt failed", StringComparison.Ordinal)
            );

        var metrics = await collector.MetricsAsync(token);
        foreach (
            var (service, outcome) in new[]
            {
                ("proof-dispatch-failed", "failed"),
                ("proof-dispatch-recovered", "published"),
                ("proof-process-failed", "failed"),
                ("proof-process-recovered", "processed"),
            }
        )
        {
            Assert.Contains(
                metrics,
                record =>
                    record.Service == service
                    && Name(record.Record) == "gloaming.messaging.attempts"
                    && record
                        .Record.GetProperty("sum")
                        .GetProperty("dataPoints")
                        .EnumerateArray()
                        .Any(point =>
                            OtlpCollector.Attribute(point, "result") == outcome
                            && long.Parse(
                                point.GetProperty("asInt").GetString()!,
                                System.Globalization.CultureInfo.InvariantCulture
                            ) >= 1
                        )
            );
            Assert.Contains(
                metrics,
                record =>
                    record.Service == service
                    && Name(record.Record) == "gloaming.messaging.attempt.duration"
            );
        }
    }

    [Fact]
    public async Task UnreachableCollectorDoesNotPreventIndependentBusinessCommitAndProcessing()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        var token = deadline.Token;
        await using var topology = await WorkerTopology.CreateAsync(
            postgres,
            rabbit,
            token,
            new(StringComparer.Ordinal)
            {
                ["OTEL_EXPORTER_OTLP_ENDPOINT"] = "http://127.0.0.1:1",
                ["OTEL_EXPORTER_OTLP_PROTOCOL"] = "http/protobuf",
                ["OTEL_EXPORTER_OTLP_TIMEOUT"] = "1000",
            }
        );
        await topology.SetupAsync(token);
        var (api, client) = await topology.StartApiAsync(token);
        Guid export;
        using (client)
            export = await WorkerTopology.SubmitAsync(client, 2, token);
        await api.StopAsync(token);
        var dispatch = await topology.StartWorkerAsync("dispatch", "no-collector-dispatch", token);
        var intake = await topology.StartWorkerAsync("receive", "no-collector-intake", token);
        var process = await topology.StartWorkerAsync("process", "no-collector-process", token);
        await topology.WaitForJobsAsync(1, token);
        await dispatch.StopAsync(token);
        await intake.StopAsync(token);
        await process.StopAsync(token);
        await topology.AssertJobAsync(export, 2, token);
        await topology.AssertCountsAsync(1, 1, 1, token);
    }

    private static string? Name(JsonElement record) => record.GetProperty("name").GetString();

    private static string? Id(JsonElement record, string field) =>
        record.TryGetProperty(field, out var value) ? value.GetString()?.ToLowerInvariant() : null;

    private static JsonElement Single(
        List<(string Service, JsonElement Record)> spans,
        string service,
        string name
    ) => Assert.Single(spans, span => span.Service == service && Name(span.Record) == name).Record;

    private static void AssertLink(JsonElement span, ActivityContext upstream)
    {
        var link = Assert.Single(span.GetProperty("links").EnumerateArray());
        Assert.Equal(upstream.TraceId.ToString(), Id(link, "traceId"));
        Assert.Equal(upstream.SpanId.ToString(), Id(link, "spanId"));
    }

    private static void AssertOutcome(JsonElement span, Guid message, string outcome, int status)
    {
        Assert.Equal(message.ToString(), OtlpCollector.Attribute(span, "messaging.message.id"));
        Assert.Equal(outcome, OtlpCollector.Attribute(span, "gloaming.messaging.result"));
        Assert.Equal(status, span.GetProperty("status").GetProperty("code").GetInt32());
    }

    private static async Task SqlAsync(string connection, string sql, CancellationToken token)
    {
        await using var database = ExportDbContext.Create(connection);
        await database.Database.ExecuteSqlRawAsync(sql, token);
    }
}
