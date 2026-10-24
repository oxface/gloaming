using System.Diagnostics;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.TestSupport;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Gloaming.Messaging.InboxTests;

[CollectionDefinition("Inbox telemetry", DisableParallelization = true)]
public sealed class InboxTelemetryProofs;

[Collection("Inbox telemetry")]
public sealed class TelemetryTests(PostgreSqlFixture postgres)
{
    private const string Parent = "00-12345678901234567890123456789012-1234567890123456-01";

    private static IncomingMessage Message(
        Guid? id = null,
        string? parent = Parent,
        string json = "{\"value\":1}"
    )
    {
        var original = InboxProof.Message(id, json);
        return new(
            original.MessageId,
            original.ProducerKey,
            original.MessageName,
            original.SchemaVersion,
            original.Payload,
            original.TenantKey,
            original.CorrelationId,
            original.CausationId,
            parent,
            "proof=first"
        );
    }

    [Fact]
    public async Task CompetingIntakeIgnoresChangedDiagnosticsAndRetainsCommittedWinner()
    {
        string connection = await InboxProof.DatabaseAsync(postgres);
        await using var provider = InboxProof.Provider(connection);
        var first = Message();
        await using var scope = provider.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<InboxConsumer>();
        await using var transaction = await database.Database.BeginTransactionAsync(
            InboxProof.Token
        );
        Assert.Equal(
            InboxReceiveResult.Queued,
            await scope
                .ServiceProvider.GetRequiredService<IInbox<InboxConsumer>>()
                .ReceiveAsync("render", first, InboxProof.Token)
        );

        var duplicate = InboxProof.ReceiveAsync(
            provider,
            Message(first.MessageId, "changed-attempt")
        );
        await InboxProof.WaitAsync(async () =>
        {
            // Poll outside the intake transaction so PostgreSQL does not retain an old statistics snapshot.
            await using var observer = InboxProof.Context(connection);
            return await observer
                    .Database.SqlQueryRaw<long>(
                        "SELECT count(*) AS \"Value\" FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'"
                    )
                    .SingleAsync(InboxProof.Token) > 0;
        });
        await transaction.CommitAsync(InboxProof.Token);
        Assert.Equal(InboxReceiveResult.AlreadyReceived, await duplicate);

        await using var fresh = InboxProof.Context(connection);
        var retained = await fresh.Set<InboxMessageRecord>().SingleAsync(InboxProof.Token);
        Assert.Equal(Parent, retained.TraceParent);
        Assert.Equal("proof=first", retained.TraceState);
        await Assert.ThrowsAsync<InboxMessageConflictException>(() =>
            InboxProof.ReceiveAsync(
                provider,
                Message(first.MessageId, "changed-attempt", "{\"value\":2}")
            )
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FailedOrCancelledHandlingRollsBackAndFreshProcessingLinksRetainedContext(
        bool cancel
    )
    {
        string connection = await InboxProof.DatabaseAsync(postgres);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(InboxProof.Token);
        var scenario = new HandlerScenario
        {
            OnHandle = async (db, message, token) =>
            {
                db.Add(new HandledItem { Id = message.MessageId, Value = 9 });
                await db.SaveChangesAsync(token);
                if (cancel)
                {
                    cancellation.Cancel();
                    token.ThrowIfCancellationRequested();
                }
                throw new InvalidOperationException("handler failed after save");
            },
        };
        var logger = new ContextLogger<object>();
        var services = InboxProof.Services(connection, scenario);
        services.AddSingleton<ILoggerFactory>(new ProbeLoggerFactory(logger));
        // Replace the fixture's NullLogger registration so callable processing uses this logger.
        services.AddSingleton(typeof(ILogger<>), typeof(Logger<>));
        await using var provider = services.BuildServiceProvider();
        var incoming = Message();
        await InboxProof.ReceiveAsync(provider, incoming);
        using var probe = new MessagingTelemetryProbe();
        if (cancel)
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                InboxProof.ProcessAsync(provider, cancellation.Token)
            );
        else
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                InboxProof.ProcessAsync(provider)
            );

        await using (var read = InboxProof.Context(connection))
        {
            Assert.Empty(await read.Set<HandledItem>().ToArrayAsync(InboxProof.Token));
            Assert.Null(
                (await read.Set<InboxMessageRecord>().SingleAsync(InboxProof.Token)).ProcessedAt
            );
        }

        var failed = Assert.Single(probe.Spans);
        Assert.Equal(
            cancel ? "cancelled" : "failed",
            failed.GetTagItem("gloaming.messaging.result")
        );
        if (!cancel)
        {
            Assert.Equal(ActivityStatusCode.Error, failed.Status);
            Assert.Equal(failed.Context, Assert.Single(logger.Errors).Context);
        }

        scenario.OnHandle = null;
        await InboxProof.ReadyAsync(connection);
        Assert.Equal(InboxProcessingResult.Processed, await InboxProof.ProcessAsync(provider));
        var success = probe.Spans.Last();
        Assert.Equal(ActivityKind.Consumer, success.Kind);
        Assert.Equal(ActivityStatusCode.Ok, success.Status);
        Assert.NotEqual(failed.TraceId, success.TraceId);
        foreach (var span in probe.Spans)
            Assert.Equal(
                ActivitySpanId.CreateFromString(Parent.AsSpan(36, 16)),
                Assert.Single(span.Links).Context.SpanId
            );

        await using var recovered = InboxProof.Context(connection);
        Assert.Single(await recovered.Set<HandledItem>().ToArrayAsync(InboxProof.Token));
        Assert.NotNull(
            (await recovered.Set<InboxMessageRecord>().SingleAsync(InboxProof.Token)).ProcessedAt
        );
        Assert.Equal(2, probe.Metrics.Count(m => m.Instrument == "gloaming.messaging.attempts"));
        Assert.All(probe.Metrics, measurement => Assert.Equal("process", measurement.Operation));
        Assert.Equal(InboxProcessingResult.NoWork, await InboxProof.ProcessAsync(provider));
        Assert.Equal(2, probe.Spans.Count);
        Assert.Equal(4, probe.Metrics.Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("malformed")]
    [InlineData("00-12345678901234567890123456789012-1234567890123456-00")]
    public async Task DiagnosticsNeverDecideBusinessProcessing(string? parent)
    {
        string connection = await InboxProof.DatabaseAsync(postgres);
        await using var provider = InboxProof.Provider(connection);
        await InboxProof.ReceiveAsync(provider, Message(parent: parent));
        using var probe = new MessagingTelemetryProbe();
        Assert.Equal(InboxProcessingResult.Processed, await InboxProof.ProcessAsync(provider));
        var span = Assert.Single(probe.Spans);
        if (parent is null or "malformed")
            Assert.Empty(span.Links);
        else
            Assert.Equal(ActivityTraceFlags.None, Assert.Single(span.Links).Context.TraceFlags);
        Assert.Contains(probe.Metrics, metric => metric.Result == "processed");
    }

    private sealed class ProbeLoggerFactory(ContextLogger<object> logger) : ILoggerFactory
    {
        public ILogger CreateLogger(string categoryName) => logger;

        public void AddProvider(ILoggerProvider provider) { }

        public void Dispose() { }
    }
}
