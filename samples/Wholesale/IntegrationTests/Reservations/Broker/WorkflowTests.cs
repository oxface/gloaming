using System.Text.Json;
using Gloaming.Events.Serialization;
using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Samples.Wholesale.Inventory;
using Gloaming.Samples.Wholesale.Inventory.Contracts;
using Gloaming.Samples.Wholesale.Sales;
using Gloaming.Samples.Wholesale.Sales.Contracts;
using Gloaming.Samples.Wholesale.Worker.Broker;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;

namespace Gloaming.Samples.Wholesale.IntegrationTests.Reservations.Broker;

public sealed class WorkflowTests(PostgreSqlFixture postgres, RabbitMqFixture rabbit)
    : IClassFixture<PostgreSqlFixture>,
        IClassFixture<RabbitMqFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private Task<WorkflowProof> ProofAsync() => WorkflowProof.CreateAsync(postgres, rabbit, Token);

    private static StartStockIssueRequest Request(
        Guid stock,
        decimal quantity = 1,
        long version = 2,
        bool expired = false
    ) =>
        new(
            Guid.NewGuid(),
            stock,
            version,
            quantity,
            DateTimeOffset.UtcNow.AddMinutes(expired ? -5 : 5)
        );

    [Theory]
    [InlineData(1, 2, StockIssueRequestStatus.Issued, null)]
    [InlineData(6, 2, StockIssueRequestStatus.Declined, StockIssueRefusal.InsufficientStock)]
    [InlineData(1, 1, StockIssueRequestStatus.Declined, StockIssueRefusal.Conflict)]
    [InlineData(1, 2, StockIssueRequestStatus.Declined, StockIssueRefusal.NotFound)]
    public async Task RealBrokerJourneyResolvesActualInventoryDecision(
        int quantity,
        long version,
        StockIssueRequestStatus expected,
        StockIssueRefusal? refusal
    )
    {
        await using var proof = await ProofAsync();
        Guid stock =
            refusal == StockIssueRefusal.NotFound ? Guid.NewGuid() : await proof.SeedAsync(Token);
        var started = await proof.StartAsync(Request(stock, quantity, version), Token);
        await proof.DeliverCommandAsync(Token);
        Assert.Equal(
            OutboxDispatchResult.Published,
            await proof.DispatchAsync<InventoryDbContext>(Token)
        );
        Assert.True(
            await proof.Broker.ReceiveReplyAsync(
                proof.Services.GetRequiredService<IServiceScopeFactory>(),
                Token
            )
        );
        Assert.Equal(
            InboxProcessingResult.Processed,
            await proof.ProcessAsync<SalesDbContext>(StockIssueReplyAdmission.Subscription, Token)
        );

        var final = await proof.ReadAsync(started.RequestId, Token);
        Assert.Equal(expected, final!.Status);
        Assert.Equal(refusal, final.Refusal);
        Assert.Equal(2, final.Version);
        if (expected == StockIssueRequestStatus.Issued)
        {
            Assert.Equal(3, final.RecordedStockVersion);
            Assert.Equal(4m, final.RemainingQuantity);
            await using var scope = proof.Scope();
            var actual = await scope
                .ServiceProvider.GetRequiredService<IStockPositionQueries>()
                .ReadCurrentAsync(stock, Token);
            Assert.Equal(3, actual!.Version);
            Assert.Equal(4m, actual.OnHand);
        }
    }

    [Fact]
    public async Task NativeMalformedIntakeIsRejectedWithoutRetainingOrRequeueingIt()
    {
        await using var proof = await ProofAsync();
        await using var connection = await new ConnectionFactory
        {
            Uri = new Uri(rabbit.ConnectionString),
        }.CreateConnectionAsync(Token);
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true
            ),
            Token
        );
        string queue = proof.Environment["QueuePrefix"] + ".inventory.stock-issues";
        var properties = new BasicProperties
        {
            MessageId = Guid.NewGuid().ToString("D"),
            Type = "unknown.reply",
            CorrelationId = Guid.NewGuid().ToString("D"),
            Headers = new Dictionary<string, object?>
            {
                ["producer-module"] = "inventory",
                ["schema-version"] = 1,
                ["owner-key"] = WorkflowProof.Alpha,
                ["causation-id"] = Guid.NewGuid().ToString("D"),
            },
        };
        await channel.BasicPublishAsync(
            "",
            queue,
            mandatory: true,
            properties,
            "{}"u8.ToArray(),
            Token
        );
        await Assert.ThrowsAsync<EventDecodingException>(() =>
            proof.Broker.ReceiveReplyAsync(
                proof.Services.GetRequiredService<IServiceScopeFactory>(),
                Token
            )
        );
        Assert.False(
            await proof.Broker.ReceiveReplyAsync(
                proof.Services.GetRequiredService<IServiceScopeFactory>(),
                Token
            )
        );
        Assert.Equal(
            0,
            await WorkflowProof.CountAsync(
                proof.SalesConnection,
                "SELECT count(*) FROM sales.inbox_messages",
                Token
            )
        );
    }

    [Fact]
    public async Task ConfirmedCommandRedeliveryAfterDecisionDoesNotIssueStockAgain()
    {
        await using var proof = await ProofAsync();
        var started = await proof.StartAsync(Request(await proof.SeedAsync(Token)), Token);
        await proof.DeliverCommandAsync(Token);
        await using var scope = proof.Scope();
        var saved = await scope
            .ServiceProvider.GetRequiredService<SalesDbContext>()
            .Set<OutboxMessageRecord>()
            .SingleAsync(Token);
        await proof.Broker.PublishCommandAsync(
            new(
                saved.MessageId,
                saved.RouteKey,
                saved.MessageName,
                saved.SchemaVersion,
                saved.Payload,
                saved.TenantKey,
                saved.CorrelationId,
                saved.CausationId
            ),
            Token
        );
        Assert.True(
            await proof.Broker.ReceiveCommandAsync(
                proof.Services.GetRequiredService<IServiceScopeFactory>(),
                Token
            )
        );
        Assert.Equal(
            InboxProcessingResult.NoWork,
            await proof.ProcessAsync<InventoryDbContext>(
                StockIssueMessageAdmission.Subscription,
                Token
            )
        );
        Assert.Equal(
            3,
            await WorkflowProof.CountAsync(
                proof.InventoryConnection,
                "SELECT version FROM inventory.event_streams",
                Token
            )
        );
        Assert.Equal(
            1,
            await WorkflowProof.CountAsync(
                proof.InventoryConnection,
                "SELECT count(*) FROM inventory.outbox_messages",
                Token
            )
        );
        Assert.Equal(started.CommandMessageId, saved.MessageId);
    }

    [Fact]
    public async Task IdenticalStartRetriesPreserveCommandWhileChangedInputsAndMissingContextAreRejected()
    {
        await using var proof = await ProofAsync();
        var input = Request(Guid.NewGuid());
        var started = await proof.StartAsync(input, Token);
        Assert.Equal(started, await proof.StartAsync(input, Token));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            proof.StartAsync(input with { Quantity = 2 }, Token)
        );
        Assert.Equal(
            1,
            await WorkflowProof.CountAsync(
                proof.SalesConnection,
                "SELECT count(*) FROM sales.outbox_messages",
                Token
            )
        );
        Assert.Null(await proof.ReadAsync(input.RequestId, Token, WorkflowProof.Beta));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            proof.StartAsync(input with { RequestId = Guid.NewGuid() }, Token, "unadmitted")
        );
        await using var missing = proof.Services.CreateAsyncScope();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            missing
                .ServiceProvider.GetRequiredService<IStockIssueRequests>()
                .StartAsync(input, Token)
        );
    }

    [Theory]
    [InlineData("outbox")]
    [InlineData("request")]
    public async Task StartFailureRollsBackProgressAndCommandThenFreshScopeCanRetry(
        string participant
    )
    {
        await using var proof = await ProofAsync();
        string table = participant == "outbox" ? "outbox_messages" : "stock_issue_requests";
        await WorkflowProof.SqlAsync(
            proof.SalesConnection,
            $"ALTER TABLE sales.{table} ADD CONSTRAINT reject_start CHECK (false)",
            Token
        );
        var input = Request(Guid.NewGuid());
        await Assert.ThrowsAsync<DbUpdateException>(() => proof.StartAsync(input, Token));
        Assert.Null(await proof.ReadAsync(input.RequestId, Token));
        Assert.Equal(
            0,
            await WorkflowProof.CountAsync(
                proof.SalesConnection,
                "SELECT count(*) FROM sales.outbox_messages",
                Token
            )
        );
        await WorkflowProof.SqlAsync(
            proof.SalesConnection,
            $"ALTER TABLE sales.{table} DROP CONSTRAINT reject_start",
            Token
        );
        Assert.Equal(
            StockIssueRequestStatus.AwaitingReply,
            (await proof.StartAsync(input, Token)).Status
        );
    }

    [Theory]
    [InlineData("state")]
    [InlineData("completion")]
    public async Task ReplyFailureRollsBackResolutionAndCompletionThenFreshProcessingRecovers(
        string participant
    )
    {
        await using var proof = await ProofAsync();
        var started = await proof.StartAsync(Request(Guid.NewGuid()), Token);
        var reply = WorkflowProof.Recorded(started);
        await proof.IntakeAsync(reply, Token);
        string constraint =
            participant == "state"
                ? "ALTER TABLE sales.stock_issue_requests ADD CONSTRAINT reject_resolution CHECK (\"Status\" = 1)"
                : "ALTER TABLE sales.inbox_messages ADD CONSTRAINT reject_resolution CHECK (processed_at IS NULL)";
        await WorkflowProof.SqlAsync(proof.SalesConnection, constraint, Token);
        await Assert.ThrowsAnyAsync<Exception>(() =>
            proof.ProcessAsync<SalesDbContext>(StockIssueReplyAdmission.Subscription, Token)
        );
        Assert.Equal(
            StockIssueRequestStatus.AwaitingReply,
            (await proof.ReadAsync(started.RequestId, Token))!.Status
        );
        Assert.Equal(
            0,
            await WorkflowProof.CountAsync(
                proof.SalesConnection,
                "SELECT count(*) FROM sales.inbox_messages WHERE processed_at IS NOT NULL",
                Token
            )
        );
        string table = participant == "state" ? "stock_issue_requests" : "inbox_messages";
        await WorkflowProof.SqlAsync(
            proof.SalesConnection,
            $"ALTER TABLE sales.{table} DROP CONSTRAINT reject_resolution; UPDATE sales.inbox_messages SET available_at = clock_timestamp()",
            Token
        );
        Assert.Equal(
            InboxProcessingResult.Processed,
            await proof.ProcessAsync<SalesDbContext>(StockIssueReplyAdmission.Subscription, Token)
        );
        Assert.Equal(
            StockIssueRequestStatus.Issued,
            (await proof.ReadAsync(started.RequestId, Token))!.Status
        );
    }

    [Fact]
    public async Task TransportAndSemanticDuplicatesDoNotRepeatResolutionButConflictingReplyFails()
    {
        await using var proof = await ProofAsync();
        var started = await proof.StartAsync(Request(Guid.NewGuid()), Token);
        var reply = WorkflowProof.Recorded(started);
        Assert.Equal(InboxReceiveResult.Queued, await proof.IntakeAsync(reply, Token));
        Assert.Equal(InboxReceiveResult.AlreadyReceived, await proof.IntakeAsync(reply, Token));
        await proof.ProcessAsync<SalesDbContext>(StockIssueReplyAdmission.Subscription, Token);
        await proof.IntakeAsync(WorkflowProof.Recorded(started), Token);
        await proof.ProcessAsync<SalesDbContext>(StockIssueReplyAdmission.Subscription, Token);
        Assert.Equal(2, (await proof.ReadAsync(started.RequestId, Token))!.Version);

        await proof.IntakeAsync(WorkflowProof.Recorded(started, remaining: 1), Token);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            proof.ProcessAsync<SalesDbContext>(StockIssueReplyAdmission.Subscription, Token)
        );
        Assert.Equal(4m, (await proof.ReadAsync(started.RequestId, Token))!.RemainingQuantity);
        Assert.Equal(
            2,
            await WorkflowProof.CountAsync(
                proof.SalesConnection,
                "SELECT count(*) FROM sales.inbox_messages WHERE processed_at IS NOT NULL",
                Token
            )
        );
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("producer")]
    [InlineData("causation")]
    [InlineData("correlation")]
    [InlineData("stock")]
    [InlineData("quantity")]
    [InlineData("version")]
    public async Task UnrelatedRepliesCannotResolveAnotherRequest(string mismatch)
    {
        await using var proof = await ProofAsync();
        var started = await proof.StartAsync(Request(Guid.NewGuid()), Token);
        var reply = WorkflowProof.Recorded(
            started,
            owner: mismatch == "tenant" ? WorkflowProof.Beta : WorkflowProof.Alpha
        );
        var payload = reply.Payload.Deserialize<StockIssueRecordedV1>(WorkflowProof.Json)!;
        payload = mismatch switch
        {
            "stock" => payload with { StockPositionId = Guid.NewGuid() },
            "quantity" => payload with { IssuedQuantity = 2 },
            "version" => payload with { Version = 20 },
            _ => payload,
        };
        reply = new IncomingMessage(
            reply.MessageId,
            mismatch == "producer" ? "intruder" : reply.ProducerKey,
            reply.MessageName,
            reply.SchemaVersion,
            System.Text.Json.JsonSerializer.SerializeToElement(payload, WorkflowProof.Json),
            reply.TenantKey,
            mismatch == "correlation" ? Guid.NewGuid().ToString("D") : reply.CorrelationId,
            mismatch == "causation" ? Guid.NewGuid().ToString("D") : reply.CausationId
        );
        await proof.IntakeAsync(reply, Token);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            proof.ProcessAsync<SalesDbContext>(StockIssueReplyAdmission.Subscription, Token)
        );
        Assert.Equal(
            StockIssueRequestStatus.AwaitingReply,
            (await proof.ReadAsync(started.RequestId, Token))!.Status
        );
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task DeadlineAndLateReplyInEitherOrderResolveSameActualOutcome(bool deadlineFirst)
    {
        await using var proof = await ProofAsync();
        var started = await proof.StartAsync(
            Request(await proof.SeedAsync(Token), expired: true),
            Token
        );
        await proof.DeliverCommandAsync(Token);
        if (deadlineFirst)
        {
            Assert.Equal(1, await proof.ExpireAsync(Token));
            Assert.Equal(
                StockIssueRequestStatus.NeedsAttention,
                (await proof.ReadAsync(started.RequestId, Token))!.Status
            );
        }

        await proof.IntakeAsync(await proof.InventoryReplyAsync(Token), Token);
        await proof.ProcessAsync<SalesDbContext>(StockIssueReplyAdmission.Subscription, Token);
        Assert.Equal(0, await proof.ExpireAsync(Token));
        var final = await proof.ReadAsync(started.RequestId, Token);
        Assert.Equal(StockIssueRequestStatus.Issued, final!.Status);
        Assert.Equal(deadlineFirst ? 3 : 2, final.Version);
        Assert.Equal(
            1,
            await WorkflowProof.CountAsync(
                proof.SalesConnection,
                "SELECT count(*) FROM sales.outbox_messages",
                Token
            )
        );
    }

    [Fact]
    public async Task BoundedTenantDeadlineScansAndReorderedRepliesKeepRequestsIndependent()
    {
        await using var proof = await ProofAsync();
        var first = await proof.StartAsync(Request(Guid.NewGuid(), expired: true), Token);
        var second = await proof.StartAsync(Request(Guid.NewGuid(), expired: true), Token);
        var beta = await proof.StartAsync(
            Request(Guid.NewGuid(), expired: true),
            Token,
            WorkflowProof.Beta
        );
        Assert.Equal(1, await proof.ExpireAsync(Token, size: 1));
        Assert.Equal(
            StockIssueRequestStatus.AwaitingReply,
            (await proof.ReadAsync(beta.RequestId, Token, WorkflowProof.Beta))!.Status
        );
        foreach (var progress in new[] { second, first })
        {
            await proof.IntakeAsync(WorkflowProof.Recorded(progress), Token);
            await proof.ProcessAsync<SalesDbContext>(StockIssueReplyAdmission.Subscription, Token);
        }

        Assert.Equal(
            StockIssueRequestStatus.Issued,
            (await proof.ReadAsync(first.RequestId, Token))!.Status
        );
        Assert.Equal(
            StockIssueRequestStatus.Issued,
            (await proof.ReadAsync(second.RequestId, Token))!.Status
        );
        Assert.Equal(1, await proof.ExpireAsync(Token, WorkflowProof.Beta));
    }
}
