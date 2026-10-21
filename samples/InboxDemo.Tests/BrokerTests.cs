using System.Data.Common;
using System.Diagnostics;
using System.Text.Json;
using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.MessagingDemo;
using ModulithFoundry.Samples.OutboxDemo;
using ModulithFoundry.Tests.Infrastructure;
using Npgsql;
using RabbitMQ.Client;
using static ModulithFoundry.Samples.InboxDemo.Tests.AdoptionTests;

namespace ModulithFoundry.Samples.InboxDemo.Tests;

[Collection("Rendering PostgreSQL")]
public sealed class BrokerTests(PostgreSqlFixture postgres, RabbitMqFixture rabbit)
    : IClassFixture<RabbitMqFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(
        3,
        "{\"ExportRequestId\":\"a1fbbfda-a359-4f29-9bc6-f8c9349f515c\",\"PageCount\":3}"
    )]
    [InlineData(2, "{}")]
    [InlineData(2, "{\"ExportRequestId\":\"a1fbbfda-a359-4f29-9bc6-f8c9349f515c\",\"Pages\":3}")]
    public async Task UnsupportedBrokerWorkIsNotAcknowledgedAndRemainsAvailableForRedelivery(
        int schema,
        string json
    )
    {
        string receiver = await ReceiverAsync();
        await using var connection = await ConnectAsync();
        await using var channel = await ChannelAsync(connection);
        string queue = await QueueAsync(channel);
        string id = Guid.NewGuid().ToString();
        var properties = new BasicProperties
        {
            Persistent = true,
            MessageId = id,
            Type = "exports.render",
            Headers = new Dictionary<string, object?>
            {
                ["producer-module"] = "exports",
                ["schema-version"] = schema,
            },
        };
        byte[] body = System.Text.Encoding.UTF8.GetBytes(json);
        await channel.BasicPublishAsync("", queue, true, properties, body, Token);
        var services = new ServiceCollection();
        InboxDemoHost.Register(services, receiver);
        await using var provider = services.BuildServiceProvider();
        var intake = new RabbitMqRenderReceiver(
            channel,
            provider.GetRequiredService<IServiceScopeFactory>()
        );
        var delivery = await DeliveryAsync(channel, queue);
        await Assert.ThrowsAsync<InvalidDataException>(() => intake.ReceiveAsync(delivery, Token));
        await using (var read = RenderDbContext.Create(receiver))
        {
            Assert.Empty(await read.Set<InboxMessageRecord>().ToArrayAsync(Token));
            Assert.Empty(await read.Set<RenderJob>().ToArrayAsync(Token));
        }

        await channel.CloseAsync(
            200,
            "proof requeue rejected schema",
            abort: false,
            cancellationToken: Token
        );
        await using var recovered = await ChannelAsync(connection);
        var repeated = await DeliveryAsync(recovered, queue);
        Assert.True(repeated.Redelivered);
        Assert.Equal(id, repeated.BasicProperties.MessageId);
        Assert.Equal(body, repeated.Body.ToArray());
        // The fixture now discards only its own rejected delivery after proving it was not acknowledged.
        await recovered.BasicAckAsync(repeated.DeliveryTag, multiple: false, Token);
        await recovered.QueueDeleteAsync(queue, ifUnused: false, ifEmpty: true, Token);
    }

    [Fact]
    public async Task NativeSendUsesTheLocalContextWithoutOldRetainedVendorState()
    {
        await using var connection = await ConnectAsync();
        await using var channel = await ChannelAsync(connection);
        string queue = await QueueAsync(channel);
        Activity? sent = null;
        using var listener = new ActivityListener
        {
            ShouldListenTo = source => source.Name == "RabbitMQ.Client.Publisher",
            Sample = (ref ActivityCreationOptions<ActivityContext> _) =>
                ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = activity => sent = activity,
        };
        ActivitySource.AddActivityListener(listener);
        using var local = new Activity("publication").SetIdFormat(ActivityIdFormat.W3C).Start();
        var outgoing = OutgoingMessage.FromPayload(
            Guid.NewGuid(),
            "exports.render",
            "exports.render",
            1,
            new RenderExportV1(Guid.NewGuid(), 3),
            new(),
            traceParent: "00-12345678901234567890123456789012-1234567890123456-01",
            traceState: "proof=old"
        );
        await new RabbitMqExportPublisher(channel, queue).PublishAsync(outgoing, Token);
        var parsed = RabbitMqRenderReceiver.Parse(await DeliveryAsync(channel, queue));
        Assert.NotNull(sent);
        Assert.Equal(local.TraceId, sent.TraceId);
        Assert.Equal(local.SpanId, sent.ParentSpanId);
        Assert.True(
            ActivityContext.TryParse(parsed.TraceParent, parsed.TraceState, out var received)
        );
        Assert.Equal(sent.TraceId, received.TraceId);
        Assert.Equal(sent.SpanId, received.SpanId);
        Assert.Null(parsed.TraceState);
        Assert.Equal("proof=old", outgoing.TraceState);
        Assert.Equal(outgoing.MessageId, parsed.MessageId);
    }

    [Fact]
    public async Task MalformedDiagnosticHeadersDoNotRejectValidNativeDelivery()
    {
        string receiver = await ReceiverAsync();
        await using var connection = await ConnectAsync();
        await using var channel = await ChannelAsync(connection);
        string queue = await QueueAsync(channel);
        Guid export = Guid.NewGuid();
        var properties = new BasicProperties
        {
            MessageId = Guid.NewGuid().ToString(),
            Type = "exports.render",
            Headers = new Dictionary<string, object?>
            {
                ["producer-module"] = "exports",
                ["schema-version"] = 1,
                ["traceparent"] = 42,
                ["tracestate"] = false,
            },
        };
        await channel.BasicPublishAsync(
            "",
            queue,
            true,
            properties,
            JsonSerializer.SerializeToUtf8Bytes(new RenderExportV1(export, 3)),
            Token
        );
        var delivery = await DeliveryAsync(channel, queue);
        var parsed = RabbitMqRenderReceiver.Parse(delivery);
        Assert.Null(parsed.TraceParent);
        Assert.Null(parsed.TraceState);
        var services = new ServiceCollection();
        InboxDemoHost.Register(services, receiver);
        await using var provider = services.BuildServiceProvider();
        Assert.Equal(
            InboxReceiveResult.Queued,
            await new RabbitMqRenderReceiver(
                channel,
                provider.GetRequiredService<IServiceScopeFactory>()
            ).ReceiveAsync(delivery, Token)
        );
        Assert.Equal(InboxProcessingResult.Processed, await ProcessAsync(provider));
        await using var read = RenderDbContext.Create(receiver);
        Assert.Equal(export, (await read.Set<RenderJob>().SingleAsync(Token)).ExportRequestId);
    }

    [Fact]
    public async Task ExecutableRoundTripUsesIndependentDatabasesAndNoReceiverOutbox()
    {
        string sender = await postgres.CreateDatabaseAsync(Token);
        string receiver = await postgres.CreateDatabaseAsync(Token);
        using var output = new StringWriter();
        await MessagingJourney.RunAsync(
            sender,
            receiver,
            new Uri(rabbit.ConnectionString),
            output,
            Token
        );
        Assert.Contains("local job=Processed", output.ToString());
        await using var exports = ExportDbContext.Create(sender);
        await using var rendering = RenderDbContext.Create(receiver);
        Assert.True((await exports.Set<ExportRequest>().SingleAsync(Token)).Submitted);
        Assert.NotNull((await exports.Set<OutboxMessageRecord>().SingleAsync(Token)).DispatchedAt);
        Assert.NotNull((await rendering.Set<InboxMessageRecord>().SingleAsync(Token)).ProcessedAt);
        Assert.Equal(3, (await rendering.Set<RenderJob>().SingleAsync(Token)).Pages);
        Assert.Null(exports.Model.FindEntityType(typeof(InboxMessageRecord)));
        Assert.Null(rendering.Model.FindEntityType(typeof(OutboxMessageRecord)));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RollbackBeforeAckAndChannelLossAfterCommittedIntakeBothRecoverByNativeRedelivery(
        bool committed
    )
    {
        string receiver = await ReceiverAsync();
        await using var connection = await ConnectAsync();
        await using var channel = await ChannelAsync(connection);
        string queue = await QueueAsync(channel);
        var outgoing = OutgoingMessage.FromPayload(
            Guid.NewGuid(),
            "exports.render",
            "exports.render",
            1,
            new RenderExportV1(Guid.NewGuid(), 3),
            new JsonSerializerOptions(),
            correlationId: "workflow",
            causationId: "parent"
        );
        await new RabbitMqExportPublisher(channel, queue).PublishAsync(outgoing, Token);
        var delivery = await DeliveryAsync(channel, queue);
        var services = new ServiceCollection();
        InboxDemoHost.Register(services, receiver);
        services.AddDbContext<RenderDbContext>(options =>
            options.AddInterceptors(new IntakeCommitFault(channel, committed))
        );
        await using (var provider = services.BuildServiceProvider())
            await Assert.ThrowsAnyAsync<Exception>(() =>
                new RabbitMqRenderReceiver(
                    channel,
                    provider.GetRequiredService<IServiceScopeFactory>()
                ).ReceiveAsync(delivery, Token)
            );
        await using (var observer = RenderDbContext.Create(receiver))
        {
            Assert.Equal(
                committed ? 1 : 0,
                await observer.Set<InboxMessageRecord>().CountAsync(Token)
            );
            Assert.Empty(await observer.Set<RenderJob>().ToArrayAsync(Token));
        }
        if (channel.IsOpen)
            await channel.CloseAsync(200, "proof requeue", abort: false, cancellationToken: Token);
        await using var recovered = await ChannelAsync(connection);
        var redelivery = await DeliveryAsync(recovered, queue);
        Assert.True(redelivery.Redelivered);
        Assert.Equal(delivery.BasicProperties.MessageId, redelivery.BasicProperties.MessageId);
        var recoveredServices = new ServiceCollection();
        InboxDemoHost.Register(recoveredServices, receiver);
        await using var recovery = recoveredServices.BuildServiceProvider();
        Assert.Equal(
            committed ? InboxReceiveResult.AlreadyReceived : InboxReceiveResult.Queued,
            await new RabbitMqRenderReceiver(
                recovered,
                recovery.GetRequiredService<IServiceScopeFactory>()
            ).ReceiveAsync(redelivery, Token)
        );
        Assert.Null(await recovered.BasicGetAsync(queue, autoAck: false, Token));
        Assert.Equal(InboxProcessingResult.Processed, await ProcessAsync(recovery));
        await using var fresh = RenderDbContext.Create(receiver);
        Assert.Single(await fresh.Set<RenderJob>().ToArrayAsync(Token));
        var row = await fresh.Set<InboxMessageRecord>().SingleAsync(Token);
        Assert.Equal("workflow", row.CorrelationId);
        Assert.Equal("parent", row.CausationId);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task SenderCompletionFailureRepeatsDeliveryButCompletedInboxDoesNotRepeatBusinessWork(
        int schema
    )
    {
        string sender = await postgres.CreateDatabaseAsync(Token);
        string receiver = await ReceiverAsync();
        await using (var database = ExportDbContext.Create(sender))
        {
            await database.Database.MigrateAsync(Token);
            Guid id = Guid.NewGuid();
            database.Add(ExportRequest.Create(id, 3));
            await database.SaveChangesAsync(Token);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.Equal(
                ExportSubmissionResult.Accepted,
                await new ExportRequestCommands(
                    database,
                    new EfOutbox<ExportDbContext>(database),
                    renderSchemaVersion: schema
                ).SubmitAsync(id, 1, Token)
            );
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }
        await SqlAsync(
            sender,
            "ALTER TABLE exports.outgoing_work ADD CONSTRAINT proof_completion CHECK (dispatched_at IS NULL)"
        );
        await using var connection = await ConnectAsync();
        await using var channel = await ChannelAsync(connection);
        string queue = await QueueAsync(channel);
        var services = new ServiceCollection();
        InboxDemoHost.Register(services, receiver);
        await using var provider = services.BuildServiceProvider();
        OutboxCompletionException failure;
        await using (var database = ExportDbContext.Create(sender))
            failure = await Assert.ThrowsAsync<OutboxCompletionException>(() =>
                Dispatcher(database, channel, queue).DispatchNextAsync(Token)
            );

        var completionError = Assert.IsType<PostgresException>(failure.InnerException);
        Assert.Equal(PostgresErrorCodes.CheckViolation, completionError.SqlState);
        Assert.Equal("proof_completion", completionError.ConstraintName);
        var first = await DeliveryAsync(channel, queue);
        Assert.Equal(failure.MessageId.ToString(), first.BasicProperties.MessageId);
        var firstEnvelope = RabbitMqRenderReceiver.Parse(first);
        Assert.Equal(schema, firstEnvelope.SchemaVersion);
        var intake = new RabbitMqRenderReceiver(
            channel,
            provider.GetRequiredService<IServiceScopeFactory>()
        );
        Assert.Equal(InboxReceiveResult.Queued, await intake.ReceiveAsync(first, Token));
        Assert.Equal(InboxProcessingResult.Processed, await ProcessAsync(provider));
        await SqlAsync(
            sender,
            "ALTER TABLE exports.outgoing_work DROP CONSTRAINT proof_completion; UPDATE exports.outgoing_work SET lease_until = clock_timestamp() - interval '1 second'"
        );
        await using (var fresh = ExportDbContext.Create(sender))
            Assert.Equal(
                OutboxDispatchResult.Published,
                await Dispatcher(fresh, channel, queue).DispatchNextAsync(Token)
            );
        var second = await DeliveryAsync(channel, queue);
        Assert.Equal(first.BasicProperties.MessageId, second.BasicProperties.MessageId);
        var secondEnvelope = RabbitMqRenderReceiver.Parse(second);
        Assert.Equal(schema, secondEnvelope.SchemaVersion);
        Assert.True(JsonElement.DeepEquals(firstEnvelope.Payload, secondEnvelope.Payload));
        Assert.Equal(InboxReceiveResult.AlreadyReceived, await intake.ReceiveAsync(second, Token));
        Assert.Equal(InboxProcessingResult.NoWork, await ProcessAsync(provider));
        await using var read = RenderDbContext.Create(receiver);
        Assert.Single(await read.Set<RenderJob>().ToArrayAsync(Token));
        Assert.Single(await read.Set<InboxMessageRecord>().ToArrayAsync(Token));
        await using var senderRead = ExportDbContext.Create(sender);
        var retained = await senderRead.Set<OutboxMessageRecord>().SingleAsync(Token);
        Assert.Equal(schema, retained.SchemaVersion);
        Assert.True(JsonElement.DeepEquals(firstEnvelope.Payload, retained.Payload));
    }

    [Fact]
    public async Task AcknowledgedIntakeRetainsFailedHandlerWorkAndFreshProcessingNeedsNoBrokerRedelivery()
    {
        string receiver = await ReceiverAsync();
        await SqlAsync(
            receiver,
            "ALTER TABLE rendering.jobs ADD CONSTRAINT proof_render CHECK (\"Pages\" > 3)"
        );
        await using var connection = await ConnectAsync();
        await using var channel = await ChannelAsync(connection);
        string queue = await QueueAsync(channel);
        await new RabbitMqExportPublisher(channel, queue).PublishAsync(
            OutgoingMessage.FromPayload(
                Guid.NewGuid(),
                "exports.render",
                "exports.render",
                1,
                new RenderExportV1(Guid.NewGuid(), 3),
                new JsonSerializerOptions()
            ),
            Token
        );
        var services = new ServiceCollection();
        InboxDemoHost.Register(services, receiver);
        services.AddPostgresInboxDeadLetters<RenderDbContext>();
        await using var provider = services.BuildServiceProvider();
        await new RabbitMqRenderReceiver(
            channel,
            provider.GetRequiredService<IServiceScopeFactory>()
        ).ReceiveAsync(await DeliveryAsync(channel, queue), Token);
        Assert.Null(await channel.BasicGetAsync(queue, autoAck: false, Token));
        await Assert.ThrowsAsync<DbUpdateException>(() => ProcessAsync(provider));
        await using (var observer = RenderDbContext.Create(receiver))
        {
            Assert.Empty(await observer.Set<RenderJob>().ToArrayAsync(Token));
            Assert.Null((await observer.Set<InboxMessageRecord>().SingleAsync(Token)).ProcessedAt);
        }
        await SqlAsync(receiver, "ALTER TABLE rendering.jobs DROP CONSTRAINT proof_render");
        await using (var recoveryScope = provider.CreateAsyncScope())
        {
            await using var observer = RenderDbContext.Create(receiver);
            var stopped = await observer.Set<InboxMessageRecord>().SingleAsync(Token);
            Assert.NotNull(stopped.DeadLetteredAt);
            Assert.Equal(1, stopped.FailureCount);
            // This trusted fixture acts as operator; HTTP authorization is exercised separately.
            Assert.Equal(
                InboxRetryResult.Retried,
                await recoveryScope
                    .ServiceProvider.GetRequiredService<IInboxDeadLetters<RenderDbContext>>()
                    .RetryAsync(
                        stopped.SubscriptionKey,
                        stopped.ProducerKey,
                        stopped.MessageId,
                        stopped.Revision,
                        Token
                    )
            );
        }

        Assert.Equal(InboxProcessingResult.Processed, await ProcessAsync(provider));
        Assert.Null(await channel.BasicGetAsync(queue, autoAck: false, Token));
        await using var read = RenderDbContext.Create(receiver);
        Assert.Single(await read.Set<RenderJob>().ToArrayAsync(Token));
    }

    private async Task<string> ReceiverAsync()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using var database = RenderDbContext.Create(connection);
        await database.Database.MigrateAsync(Token);
        return connection;
    }

    private Task<IConnection> ConnectAsync() =>
        new ConnectionFactory
        {
            Uri = new Uri(rabbit.ConnectionString),
            AutomaticRecoveryEnabled = false,
        }.CreateConnectionAsync(Token);

    private static Task<IChannel> ChannelAsync(IConnection connection) =>
        connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true
            ),
            Token
        );

    private static async Task<string> QueueAsync(IChannel channel)
    {
        string queue = "exports.proof." + Guid.NewGuid().ToString("N");
        await channel.QueueDeclareAsync(
            queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: Token
        );
        return queue;
    }

    private static async Task<BasicGetResult> DeliveryAsync(IChannel channel, string queue)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        while (true)
        {
            var delivery = await channel.BasicGetAsync(queue, autoAck: false, timeout.Token);
            if (delivery is not null)
                return delivery;
            await Task.Delay(10, timeout.Token);
        }
    }

    private static PostgresOutboxDispatcher<ExportDbContext> Dispatcher(
        ExportDbContext context,
        IChannel channel,
        string queue
    ) =>
        new(
            context,
            new RabbitMqExportPublisher(channel, queue),
            new(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(1))
        );

    private sealed class IntakeCommitFault(IChannel channel, bool afterCommit)
        : DbTransactionInterceptor
    {
        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        )
        {
            if (!afterCommit)
                throw new IOException("intake transaction failed before commit");
            return ValueTask.FromResult(result);
        }

        public override async Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default
        )
        {
            if (afterCommit)
                await channel.CloseAsync(
                    200,
                    "committed intake before ack",
                    abort: false,
                    cancellationToken: cancellationToken
                );
        }
    }
}
