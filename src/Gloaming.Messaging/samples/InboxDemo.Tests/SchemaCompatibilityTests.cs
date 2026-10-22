using System.Net;
using System.Text;
using System.Text.Json;
using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.OutboxDemo;
using ModulithFoundry.Tests.Infrastructure;
using static ModulithFoundry.Samples.InboxDemo.Tests.AdoptionTests;

namespace ModulithFoundry.Samples.InboxDemo.Tests;

[Collection("Rendering PostgreSQL")]
public sealed class SchemaCompatibilityTests(PostgreSqlFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private const string V2 = """
        {"ExportRequestId":"5fb412d3-828b-42ca-8f43-ed760517d213","PageCount":3}
        """;
    private const string V1 = """
        {"ExportRequestId":"5fb412d3-828b-42ca-8f43-ed760517d213","Pages":3}
        """;

    [Theory]
    [InlineData(3, V2)]
    [InlineData(1, V2)]
    [InlineData(2, V1)]
    [InlineData(2, "null")]
    [InlineData(2, "{}")]
    [InlineData(2, "[]")]
    [InlineData(
        2,
        "{\"ExportRequestId\":\"5fb412d3-828b-42ca-8f43-ed760517d213\",\"PageCount\":0}"
    )]
    public async Task UnsupportedOrMalformedHttpWorkIsRejectedBeforeDurableAcceptance(
        int schema,
        string json
    )
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using (var setup = RenderDbContext.Create(connection))
            await setup.Database.MigrateAsync(Token);

        await using var app = InboxDemoHost.Build(connection, "http://127.0.0.1:0");
        await app.StartAsync(Token);
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, Address(app.Services))
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Message-Id", Guid.NewGuid().ToString());
        request.Headers.Add("X-Message-Name", "exports.render");
        request.Headers.Add(
            "X-Message-Schema",
            schema.ToString(System.Globalization.CultureInfo.InvariantCulture)
        );
        using var response = await client.SendAsync(request, Token);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        await using var read = RenderDbContext.Create(connection);
        Assert.Empty(await read.Set<InboxMessageRecord>().ToArrayAsync(Token));
        Assert.Empty(await read.Set<RenderJob>().ToArrayAsync(Token));
        await app.StopAsync(Token);
    }

    [Fact]
    public async Task FutureSchemaRemainsUnsupportedAfterOperatorRetryWithoutEnvelopeEditing()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using (var setup = RenderDbContext.Create(connection))
            await setup.Database.MigrateAsync(Token);

        var outgoing = Message(Guid.NewGuid(), 3, V2);
        var incoming = new IncomingMessage(
            outgoing.MessageId,
            "exports",
            outgoing.MessageName,
            3,
            outgoing.Payload,
            correlationId: outgoing.CorrelationId,
            causationId: outgoing.CausationId
        );
        await using var reader = Reader(connection, legacy: false);
        Assert.Equal(InboxReceiveResult.Queued, await ReceiveAsync(reader, incoming));
        for (int cycle = 0; cycle < 2; cycle++)
        {
            await Assert.ThrowsAsync<InvalidDataException>(() => ProcessAsync(reader));
            await using var scope = reader.CreateAsyncScope();
            var letters = scope.ServiceProvider.GetRequiredService<
                IInboxDeadLetters<RenderDbContext>
            >();
            var stopped = await letters.GetAsync(
                RenderExportHandler.Subscription,
                "exports",
                incoming.MessageId,
                Token
            );
            Assert.NotNull(stopped);
            Assert.Equal(cycle + 1, stopped.FailureCount);
            Assert.Equal("render-command-conflict", stopped.LastFailureCode);
            if (cycle == 0)
                Assert.Equal(
                    InboxRetryResult.Retried,
                    await RetryAsync(
                        reader,
                        incoming.MessageId,
                        stopped.ReceiptId,
                        stopped.Revision
                    )
                );
        }

        await using var read = RenderDbContext.Create(connection);
        Assert.Empty(await read.Set<RenderJob>().ToArrayAsync(Token));
        var retained = await read.Set<InboxMessageRecord>().SingleAsync(Token);
        Assert.Equal(incoming.MessageId, retained.MessageId);
        Assert.Equal(3, retained.SchemaVersion);
        Assert.True(JsonElement.DeepEquals(incoming.Payload, retained.Payload));
        Assert.NotNull(retained.DeadLetteredAt);
        Assert.Equal(1, retained.RedriveCount);
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    public async Task RetainedWorkSurvivesReaderReplacementAndConditionalOperatorRetry(
        int schema,
        bool runLegacyProcessor
    )
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using (var setup = RenderDbContext.Create(connection))
            await setup.Database.MigrateAsync(Token);

        var outgoing = Message(Guid.NewGuid(), schema, schema == 1 ? V1 : V2);
        var incoming = new IncomingMessage(
            outgoing.MessageId,
            "exports",
            outgoing.MessageName,
            schema,
            outgoing.Payload,
            correlationId: outgoing.CorrelationId,
            causationId: outgoing.CausationId
        );
        await using (var legacy = Reader(connection, legacy: true))
        {
            // Privileged fixture intake models an old processor seeing work already retained by new intake.
            Assert.Equal(InboxReceiveResult.Queued, await ReceiveAsync(legacy, incoming));
            if (runLegacyProcessor)
            {
                if (schema == 1)
                    Assert.Equal(InboxProcessingResult.Processed, await ProcessAsync(legacy));
                else
                    await Assert.ThrowsAsync<InvalidDataException>(() => ProcessAsync(legacy));
            }

            await using var read = RenderDbContext.Create(connection);
            Assert.Equal(
                schema == 1 && runLegacyProcessor ? 1 : 0,
                await read.Set<RenderJob>().CountAsync(Token)
            );
        }

        await using var upgraded = Reader(connection, legacy: false);
        Assert.Equal(InboxReceiveResult.AlreadyReceived, await ReceiveAsync(upgraded, incoming));
        if (schema == 2)
        {
            await using var scope = upgraded.CreateAsyncScope();
            var letters = scope.ServiceProvider.GetRequiredService<
                IInboxDeadLetters<RenderDbContext>
            >();
            var stopped = await letters.GetAsync(
                RenderExportHandler.Subscription,
                "exports",
                incoming.MessageId,
                Token
            );
            Assert.NotNull(stopped);
            Assert.Equal(1, stopped.FailureCount);
            Assert.Equal("render-command-conflict", stopped.LastFailureCode);
            var competing = await Task.WhenAll(
                RetryAsync(upgraded, incoming.MessageId, stopped.ReceiptId, stopped.Revision),
                RetryAsync(upgraded, incoming.MessageId, stopped.ReceiptId, stopped.Revision)
            );
            Assert.Single(competing, result => result == InboxRetryResult.Retried);
            Assert.Single(competing, result => result == InboxRetryResult.NotApplied);
            Assert.Equal(
                InboxRetryResult.NotApplied,
                await RetryAsync(upgraded, incoming.MessageId, stopped.ReceiptId, stopped.Revision)
            );
        }

        Assert.Equal(
            schema == 1 && runLegacyProcessor
                ? InboxProcessingResult.NoWork
                : InboxProcessingResult.Processed,
            await ProcessAsync(upgraded)
        );
        Assert.Equal(InboxReceiveResult.AlreadyReceived, await ReceiveAsync(upgraded, incoming));
        Assert.Equal(InboxProcessingResult.NoWork, await ProcessAsync(upgraded));
        await using var fresh = RenderDbContext.Create(connection);
        Assert.Equal(3, (await fresh.Set<RenderJob>().SingleAsync(Token)).Pages);
        var retained = await fresh.Set<InboxMessageRecord>().SingleAsync(Token);
        Assert.Equal(incoming.MessageId, retained.MessageId);
        Assert.Equal(schema, retained.SchemaVersion);
        Assert.True(JsonElement.DeepEquals(incoming.Payload, retained.Payload));
        Assert.Equal("compatibility", retained.CorrelationId);
        Assert.Equal("original-command", retained.CausationId);
        Assert.NotNull(retained.ProcessedAt);
        Assert.Equal(schema == 2 ? 1 : 0, retained.FailureCount);
        Assert.Equal(schema == 2 ? 1 : 0, retained.RedriveCount);
    }

    [Fact]
    public async Task HttpPublisherPreservesOriginalDeduplicationAcrossSchemaInterpretations()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using (var setup = RenderDbContext.Create(connection))
            await setup.Database.MigrateAsync(Token);

        await using var app = InboxDemoHost.Build(connection, "http://127.0.0.1:0");
        await app.StartAsync(Token);
        using var client = new HttpClient();
        var publisher = new HttpCommandPublisher(client, Address(app.Services));
        Guid id = Guid.NewGuid();
        var original = Message(id, 1, V1);
        await publisher.PublishAsync(original, Token);
        await publisher.PublishAsync(original, Token);
        Assert.Equal(InboxProcessingResult.Processed, await ProcessAsync(app.Services));
        await publisher.PublishAsync(original, Token);
        Assert.Equal(InboxProcessingResult.NoWork, await ProcessAsync(app.Services));
        var conflict = await Assert.ThrowsAsync<HttpRequestException>(() =>
            publisher.PublishAsync(Message(id, 2, V2), Token)
        );
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);

        await publisher.PublishAsync(Message(Guid.NewGuid(), 2, V2), Token);
        Assert.Equal(InboxProcessingResult.Processed, await ProcessAsync(app.Services));
        await using var read = RenderDbContext.Create(connection);
        Assert.Single(await read.Set<RenderJob>().ToArrayAsync(Token));
        var rows = await read.Set<InboxMessageRecord>()
            .OrderBy(row => row.SchemaVersion)
            .ToArrayAsync(Token);
        Assert.Equal(2, rows.Length);
        Assert.Equal(1, rows[0].SchemaVersion);
        Assert.Equal(2, rows[1].SchemaVersion);
        using var expectedV1 = JsonDocument.Parse(V1);
        using var expectedV2 = JsonDocument.Parse(V2);
        Assert.True(JsonElement.DeepEquals(expectedV1.RootElement, rows[0].Payload));
        Assert.True(JsonElement.DeepEquals(expectedV2.RootElement, rows[1].Payload));
        await app.StopAsync(Token);
    }

    [Fact]
    public async Task HttpIntakeAndProcessingInterpretV2WithoutChangingItsRetainedEnvelope()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using (var setup = RenderDbContext.Create(connection))
            await setup.Database.MigrateAsync(Token);

        await using var app = InboxDemoHost.Build(connection, "http://127.0.0.1:0");
        await app.StartAsync(Token);
        Guid id = Guid.NewGuid();
        using var client = new HttpClient();
        using var request = new HttpRequestMessage(HttpMethod.Post, Address(app.Services))
        {
            Content = new StringContent(V2, Encoding.UTF8, "application/json"),
        };
        request.Headers.Add("X-Message-Id", id.ToString());
        request.Headers.Add("X-Message-Name", "exports.render");
        request.Headers.Add("X-Message-Schema", "2");
        request.Headers.Add("X-Correlation-Id", "compatibility");
        request.Headers.Add("X-Causation-Id", "original-command");
        using var response = await client.SendAsync(request, Token);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.Equal(InboxProcessingResult.Processed, await ProcessAsync(app.Services));
        await using var read = RenderDbContext.Create(connection);
        var job = await read.Set<RenderJob>().SingleAsync(Token);
        Assert.Equal(Guid.Parse("5fb412d3-828b-42ca-8f43-ed760517d213"), job.ExportRequestId);
        Assert.Equal(3, job.Pages);
        var retained = await read.Set<InboxMessageRecord>().SingleAsync(Token);
        Assert.Equal(id, retained.MessageId);
        Assert.Equal(2, retained.SchemaVersion);
        Assert.Equal("compatibility", retained.CorrelationId);
        Assert.Equal("original-command", retained.CausationId);
        using var expected = JsonDocument.Parse(V2);
        Assert.True(JsonElement.DeepEquals(expected.RootElement, retained.Payload));
        Assert.NotNull(retained.ProcessedAt);
        await app.StopAsync(Token);
    }

    private static OutgoingMessage Message(Guid id, int schema, string json)
    {
        using var payload = JsonDocument.Parse(json);
        return new(
            id,
            "exports.render",
            "exports.render",
            schema,
            payload.RootElement,
            correlationId: "compatibility",
            causationId: "original-command"
        );
    }

    private static ServiceProvider Reader(string connection, bool legacy)
    {
        var services = new ServiceCollection();
        if (legacy)
        {
            services.AddLogging();
            services.AddDbContext<RenderDbContext>(options =>
                RenderDbContext.Configure(options, connection)
            );
            services.AddPostgresInbox<RenderDbContext>();
            services.AddPostgresInboxProcessor<RenderDbContext>(new(TimeSpan.FromSeconds(1)));
            services.AddInboxHandler<RenderDbContext, LegacyV1Handler>(
                RenderExportHandler.Subscription
            );
            services.AddInboxFailurePolicy<RenderDbContext, RenderInboxFailurePolicy>(
                RenderExportHandler.Subscription
            );
        }
        else
            InboxDemoHost.Register(services, connection);

        services.AddPostgresInboxDeadLetters<RenderDbContext>();
        return services.BuildServiceProvider();
    }

    private static async Task<InboxReceiveResult> ReceiveAsync(
        IServiceProvider services,
        IncomingMessage message
    )
    {
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<RenderDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        var result = await scope
            .ServiceProvider.GetRequiredService<IInbox<RenderDbContext>>()
            .ReceiveAsync(RenderExportHandler.Subscription, message, Token);
        await transaction.CommitAsync(Token);
        return result;
    }

    private static async Task<InboxRetryResult> RetryAsync(
        IServiceProvider services,
        Guid message,
        Guid receiptId,
        long revision
    )
    {
        await using var scope = services.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<IInboxDeadLetters<RenderDbContext>>()
            .RetryAsync(
                RenderExportHandler.Subscription,
                "exports",
                message,
                receiptId,
                revision,
                Token
            );
    }

    // Frozen behavior of the v1-only consumer at 86331de; this is not a production reader mode.
    public sealed class LegacyV1Handler(RenderDbContext database) : IInboxHandler<RenderDbContext>
    {
        private sealed record LegacyCommand(Guid ExportRequestId, int Pages);

        public async Task HandleAsync(IncomingMessage message, CancellationToken cancellationToken)
        {
            if (
                message.ProducerKey != "exports"
                || message.MessageName != "exports.render"
                || message.SchemaVersion != 1
                || message.TenantKey is not null
            )
                throw new InvalidDataException("The v1 fixture cannot interpret this envelope.");

            var command =
                message.Payload.Deserialize<LegacyCommand>()
                ?? throw new InvalidDataException("A render command is required.");
            if (command.ExportRequestId == Guid.Empty || command.Pages <= 0)
                throw new InvalidDataException(
                    "An export identity and positive pages are required."
                );

            var existing = await database
                .Set<RenderJob>()
                .SingleOrDefaultAsync(
                    row => row.ExportRequestId == command.ExportRequestId,
                    cancellationToken
                );
            if (existing is null)
                database.Add(RenderJob.Create(command.ExportRequestId, command.Pages));
            else if (existing.Pages != command.Pages)
                throw new InvalidDataException("The export identity has conflicting requirements.");
        }
    }
}
