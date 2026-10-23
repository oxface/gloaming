using System.Diagnostics;
using System.Text.Json;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Gloaming.Messaging.TestSupport;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gloaming.Messaging.Tests;

[Collection("Messaging PostgreSQL")]
public sealed class TelemetryTests(PostgreSqlFixture postgres)
{
    private const string Parent = "00-12345678901234567890123456789012-1234567890123456-01";

    private static OutgoingMessage Message(string? trace = Parent) =>
        new(
            Guid.NewGuid(),
            "exports",
            "render",
            1,
            JsonSerializer.SerializeToElement(new { pages = 1 }),
            correlationId: "conversation",
            causationId: "cause",
            traceParent: trace,
            traceState: "proof=retained"
        );

    [Fact]
    public async Task FreshDispatchLinksEachAttemptAndLogsFailuresBeforeEndingItsActivity()
    {
        string connection = await OutboxProof.DatabaseAsync(postgres);
        var message = await OutboxProof.EnqueueAsync(connection, Message());
        using var probe = new MessagingTelemetryProbe();
        var logger = new ContextLogger<PostgresOutboxDispatcher<OutboxConsumer>>();
        var publisher = new RecordingPublisher
        {
            OnPublish = (_, _) => throw new InvalidOperationException("offline"),
        };
        await using (var failed = OutboxProof.Context(connection))
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                new PostgresOutboxDispatcher<OutboxConsumer>(
                    failed,
                    publisher,
                    new(TimeSpan.FromSeconds(30), TimeSpan.Zero),
                    logger
                ).DispatchNextAsync(OutboxProof.Token)
            );

        await using (var read = OutboxProof.Context(connection))
        {
            var row = await read.Set<OutboxMessageRecord>().SingleAsync(OutboxProof.Token);
            Assert.Equal(Parent, row.TraceParent);
            Assert.Equal("proof=retained", row.TraceState);
            Assert.Null(row.DispatchedAt);
        }

        publisher.OnPublish = null;
        await using (var recovered = OutboxProof.Context(connection))
            Assert.Equal(
                OutboxDispatchResult.Published,
                await OutboxProof
                    .Dispatcher(recovered, publisher)
                    .DispatchNextAsync(OutboxProof.Token)
            );

        var spans = probe.Spans.ToArray();
        Assert.Equal(2, spans.Length);
        Assert.Equal(2, probe.SamplingLinks.Count);
        Assert.NotEqual(spans[0].SpanId, spans[1].SpanId);
        Assert.NotEqual(spans[0].TraceId, spans[1].TraceId);
        foreach (var span in spans)
        {
            Assert.Equal(ActivityKind.Internal, span.Kind);
            Assert.Equal(message.MessageId.ToString(), span.GetTagItem("messaging.message.id"));
            Assert.Equal("conversation", span.GetTagItem("gloaming.messaging.correlation_id"));
            var link = Assert.Single(span.Links).Context;
            Assert.Equal(ActivityTraceId.CreateFromString(Parent.AsSpan(3, 32)), link.TraceId);
            Assert.Equal(ActivitySpanId.CreateFromString(Parent.AsSpan(36, 16)), link.SpanId);
            Assert.Equal("proof=retained", link.TraceState);
            Assert.True(link.IsRemote);
        }

        Assert.Equal(ActivityStatusCode.Error, spans[0].Status);
        Assert.Equal("failed", spans[0].GetTagItem("gloaming.messaging.result"));
        Assert.Equal(ActivityStatusCode.Ok, spans[1].Status);
        Assert.Equal(spans[0].Context, Assert.Single(logger.Errors).Context);
        Assert.All(publisher.Messages, sent => Assert.Equal(Parent, sent.TraceParent));
        Assert.Equal(2, probe.Metrics.Count(m => m.Instrument == "gloaming.messaging.attempts"));
        Assert.Contains(probe.Metrics, m => m.Result == "published" && m.Value > 0);

        await using var empty = OutboxProof.Context(connection);
        Assert.Equal(
            OutboxDispatchResult.NoWork,
            await OutboxProof.Dispatcher(empty, publisher).DispatchNextAsync(OutboxProof.Token)
        );
        Assert.Equal(2, probe.Spans.Count);
        Assert.Equal(4, probe.Metrics.Count);
    }

    [Theory]
    [InlineData("claim_lost")]
    [InlineData("cancelled")]
    public async Task AmbiguousAcceptanceDoesNotReportPublished(string outcome)
    {
        string connection = await OutboxProof.DatabaseAsync(postgres);
        await OutboxProof.EnqueueAsync(connection, Message());
        using var probe = new MessagingTelemetryProbe();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(OutboxProof.Token);
        var publisher = new RecordingPublisher
        {
            OnPublish = async (_, token) =>
            {
                if (outcome == "claim_lost")
                    await OutboxProof.ExpireAsync(connection);
                else
                {
                    cancellation.Cancel();
                    token.ThrowIfCancellationRequested();
                }
            },
        };
        await using var database = OutboxProof.Context(connection);
        var dispatcher = OutboxProof.Dispatcher(database, publisher);
        if (outcome == "claim_lost")
            Assert.Equal(
                OutboxDispatchResult.ClaimLost,
                await dispatcher.DispatchNextAsync(cancellation.Token)
            );
        else
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                dispatcher.DispatchNextAsync(cancellation.Token)
            );

        Assert.Equal(outcome, Assert.Single(probe.Spans).GetTagItem("gloaming.messaging.result"));
        Assert.Null(
            (await database.Set<OutboxMessageRecord>().SingleAsync(OutboxProof.Token)).DispatchedAt
        );
        Assert.All(probe.Metrics, metric => Assert.Equal(outcome, metric.Result));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("invalid-context")]
    [InlineData("00-12345678901234567890123456789012-1234567890123456-00")]
    public async Task OptionalOrUnrecordedContextDoesNotControlPublication(string? parent)
    {
        string connection = await OutboxProof.DatabaseAsync(postgres);
        await OutboxProof.EnqueueAsync(connection, Message(parent));
        using var probe = new MessagingTelemetryProbe(ActivitySamplingResult.PropagationData);
        await using var database = OutboxProof.Context(connection);
        Assert.Equal(
            OutboxDispatchResult.Published,
            await OutboxProof.Dispatcher(database, new()).DispatchNextAsync(OutboxProof.Token)
        );
        Assert.False(Assert.Single(probe.Spans).Recorded);
        Assert.Contains(probe.Metrics, metric => metric.Result == "published");
    }

    [Fact]
    public async Task DroppedAttemptStillPublishesAndRecordsMetrics()
    {
        string connection = await OutboxProof.DatabaseAsync(postgres);
        await OutboxProof.EnqueueAsync(connection, Message());
        using var probe = new MessagingTelemetryProbe(ActivitySamplingResult.None);
        var publisher = new RecordingPublisher
        {
            OnPublish = (_, _) =>
            {
                Assert.Null(Activity.Current);
                return Task.CompletedTask;
            },
        };
        await using var database = OutboxProof.Context(connection);
        Assert.Equal(
            OutboxDispatchResult.Published,
            await OutboxProof.Dispatcher(database, publisher).DispatchNextAsync(OutboxProof.Token)
        );
        Assert.Empty(probe.Spans);
        Assert.Single(probe.SamplingLinks);
        Assert.Equal(2, probe.Metrics.Count);
        Assert.All(probe.Metrics, metric => Assert.Equal("published", metric.Result));
    }

    [Fact]
    public async Task CapturedDiagnosticMetadataCannotBeReplacedBeforeSave()
    {
        string connection = await OutboxProof.DatabaseAsync(postgres);
        await using var database = OutboxProof.Context(connection);
        await using var transaction = await database.Database.BeginTransactionAsync(
            OutboxProof.Token
        );
        new EfOutbox<OutboxConsumer>(database).Enqueue(Message());
        var entry = database.ChangeTracker.Entries<OutboxMessageRecord>().Single();
        entry.Property(row => row.TraceParent).CurrentValue = "different";
        Assert.Throws<InvalidOperationException>(database.ValidateOutboxChanges);
    }
}
