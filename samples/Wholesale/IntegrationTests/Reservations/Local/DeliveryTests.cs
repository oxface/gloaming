using System.Text.Json;
using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Hosting;
using Gloaming.Samples.Wholesale.Inventory;
using Gloaming.Samples.Wholesale.Inventory.Contracts;
using Gloaming.Samples.Wholesale.Sales;
using Gloaming.Samples.Wholesale.Sales.Contracts;
using Gloaming.Samples.Wholesale.Worker.Local;
using Gloaming.Samples.Wholesale.Worker.Local.Delivery;
using Gloaming.Tests.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;

namespace Gloaming.Samples.Wholesale.IntegrationTests.Reservations.Local;

public sealed class DeliveryTests(PostgreSqlFixture postgres) : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Theory]
    [InlineData(2, StockReservationOrderStatus.Reserved, 2)]
    [InlineData(6, StockReservationOrderStatus.Compensated, 3)]
    public async Task ExistingReservationWorkflowCompletesThroughPublicDurableIntake(
        int secondQuantity,
        StockReservationOrderStatus expected,
        int turns
    )
    {
        await using var proof = await LocalWorkflowProof.CreateAsync(postgres, Token);
        Guid first = await proof.SeedAsync(Token),
            second = await proof.SeedAsync(Token);
        var started = await proof.StartAsync(
            LocalWorkflowProof.Request(first, second, secondQuantity),
            Token
        );
        for (int turn = 0; turn < turns; turn++)
            await proof.StepAsync(Token);

        Assert.Equal(expected, (await proof.ReadAsync(started.OrderId, Token)).Status);
        Assert.Equal(
            expected == StockReservationOrderStatus.Reserved ? 2 : 0,
            (await proof.StockAsync(first, Token)).Reserved
        );
        Assert.Equal(5, (await proof.StockAsync(first, Token)).OnHand);
    }

    [Fact]
    public async Task AcceptanceIsCommittedReceiptAndDoesNotExecuteTheReceiverOrReuseSenderContext()
    {
        await using var proof = await LocalWorkflowProof.CreateAsync(postgres, Token);
        Guid first = await proof.SeedAsync(Token),
            second = await proof.SeedAsync(Token);
        var started = await proof.StartAsync(LocalWorkflowProof.Request(first, second), Token);
        // The sender's scope is deliberately already initialized to another Organization and actor.
        await using var sender = proof.Scope(LocalWorkflowProof.Beta, actor: true);
        var message = await proof.PendingAsync<SalesDbContext>(Token);
        await sender
            .ServiceProvider.GetRequiredService<ReservationCommandPublisher>()
            .PublishAsync(message, Token);
        Assert.Equal(
            1,
            await LocalWorkflowProof.CountAsync(
                proof.Inventory,
                "SELECT count(*) FROM inventory.inbox_messages",
                Token
            )
        );
        Assert.Equal(0, (await proof.StockAsync(first, Token)).Reserved);
        Assert.Equal(
            StockReservationOrderStatus.AwaitingFirst,
            (await proof.ReadAsync(started.OrderId, Token)).Status
        );
        Assert.Equal(
            InboxProcessingResult.Processed,
            await proof.ProcessAsync<InventoryDbContext>(Token)
        );
        Assert.Equal(2, (await proof.StockAsync(first, Token)).Reserved);
        Assert.Equal(
            StockReservationOrderStatus.AwaitingFirst,
            (await proof.ReadAsync(started.OrderId, Token)).Status
        );
        Assert.Equal(
            OutboxDispatchResult.Published,
            await proof.DispatchAsync<InventoryDbContext>(Token)
        );
        Assert.Equal(
            InboxProcessingResult.Processed,
            await proof.ProcessAsync<SalesDbContext>(Token)
        );
        Assert.Equal(
            StockReservationOrderStatus.AwaitingSecond,
            (await proof.ReadAsync(started.OrderId, Token)).Status
        );
    }

    [Fact]
    public async Task ConcurrentIntakeDeduplicatesAndSemanticRedeliveryReturnsTheOriginalDecision()
    {
        await using var proof = await LocalWorkflowProof.CreateAsync(postgres, Token);
        Guid first = await proof.SeedAsync(Token),
            second = await proof.SeedAsync(Token);
        var started = await proof.StartAsync(LocalWorkflowProof.Request(first, second), Token);
        var message = await proof.PendingAsync<SalesDbContext>(Token);
        var command = message.Payload.Deserialize<ReserveStockV1>(Json)!;
        var delivery = LocalWorkflowProof.Delivery(message);
        var intake = proof.Services.GetRequiredService<IStockReservationIntake>();
        await Task.WhenAll(
            Enumerable.Range(0, 6).Select(_ => intake.ReceiveAsync(delivery, command, Token))
        );
        Assert.Equal(
            1,
            await LocalWorkflowProof.CountAsync(
                proof.Inventory,
                "SELECT count(*) FROM inventory.inbox_messages",
                Token
            )
        );
        Assert.Equal(
            InboxProcessingResult.Processed,
            await proof.ProcessAsync<InventoryDbContext>(Token)
        );
        var originalOutcome = await proof.PendingAsync<InventoryDbContext>(Token);
        Assert.Equal(
            OutboxDispatchResult.Published,
            await proof.DispatchAsync<InventoryDbContext>(Token)
        );
        Assert.Equal(
            InboxProcessingResult.Processed,
            await proof.ProcessAsync<SalesDbContext>(Token)
        );
        await intake.ReceiveAsync(delivery with { MessageId = Guid.NewGuid() }, command, Token);
        Assert.Equal(
            InboxProcessingResult.Processed,
            await proof.ProcessAsync<InventoryDbContext>(Token)
        );
        var repeatedOutcome = await proof.PendingAsync<InventoryDbContext>(Token);
        Assert.Equal(originalOutcome.Payload.GetRawText(), repeatedOutcome.Payload.GetRawText());
        // A new command ID is not an operation requested by the workflow. Redeliver its original
        // reply under a new delivery ID while preserving the requested command's causation instead.
        await proof
            .Services.GetRequiredService<IStockReservationOutcomeIntake>()
            .ReceiveAsync(
                LocalWorkflowProof.Delivery(originalOutcome, Guid.NewGuid()),
                originalOutcome.Payload.Deserialize<StockReservationDecidedV1>(Json)!,
                Token
            );
        Assert.Equal(
            InboxProcessingResult.Processed,
            await proof.ProcessAsync<SalesDbContext>(Token)
        );
        Assert.Equal(2, (await proof.ReadAsync(started.OrderId, Token)).Version);
        Assert.Equal(2, (await proof.StockAsync(first, Token)).Reserved);
        Assert.Equal(
            1,
            await LocalWorkflowProof.CountAsync(
                proof.Inventory,
                "SELECT count(*) FROM inventory.stock_reservations",
                Token
            )
        );
        await Assert.ThrowsAsync<InboxMessageConflictException>(() =>
            intake.ReceiveAsync(delivery, command with { Quantity = 3 }, Token)
        );
    }

    [Fact]
    public async Task FailedIntakeCommitLeavesNoReceiptAndTheProducerCanRetryTheSameDelivery()
    {
        await using var proof = await LocalWorkflowProof.CreateAsync(postgres, Token);
        Guid first = await proof.SeedAsync(Token),
            second = await proof.SeedAsync(Token);
        await proof.StartAsync(LocalWorkflowProof.Request(first, second), Token);
        await LocalWorkflowProof.SqlAsync(
            proof.Inventory,
            """
            CREATE FUNCTION public.reject_intake() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                RAISE EXCEPTION 'Injected intake commit failure';
            END;
            $$;
            CREATE CONSTRAINT TRIGGER reject_intake AFTER INSERT ON inventory.inbox_messages
            DEFERRABLE INITIALLY DEFERRED FOR EACH ROW EXECUTE FUNCTION public.reject_intake();
            """,
            Token
        );
        await Assert.ThrowsAsync<PostgresException>(() =>
            proof.DispatchAsync<SalesDbContext>(Token)
        );
        Assert.Equal(
            0,
            await LocalWorkflowProof.CountAsync(
                proof.Inventory,
                "SELECT count(*) FROM inventory.inbox_messages",
                Token
            )
        );
        Assert.Equal(
            1,
            await LocalWorkflowProof.CountAsync(
                proof.Sales,
                "SELECT count(*) FROM sales.outbox_messages WHERE dispatched_at IS NULL",
                Token
            )
        );
        await LocalWorkflowProof.SqlAsync(
            proof.Inventory,
            "DROP TRIGGER reject_intake ON inventory.inbox_messages",
            Token
        );
        await LocalWorkflowProof.EventuallyAsync(
            async () =>
                await proof.DispatchAsync<SalesDbContext>(Token) == OutboxDispatchResult.Published,
            Token
        );
        Assert.Equal(
            1,
            await LocalWorkflowProof.CountAsync(
                proof.Inventory,
                "SELECT count(*) FROM inventory.inbox_messages",
                Token
            )
        );
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("order")]
    [InlineData("payload")]
    public async Task PublicIntakeRejectsInvalidBusinessAdmissionBeforeRetainingWork(string fault)
    {
        await using var proof = await LocalWorkflowProof.CreateAsync(postgres, Token);
        var delivery = new ReservationDelivery(
            Guid.NewGuid(),
            LocalWorkflowProof.Alpha,
            Guid.NewGuid(),
            null
        );
        var command = new ReserveStockV1(Guid.NewGuid(), Guid.NewGuid(), 2, 2);
        if (fault == "tenant")
            delivery = delivery with { OrganizationKey = "unknown" };
        if (fault == "order")
            delivery = delivery with { OrderId = Guid.Empty };
        if (fault == "payload")
            command = command with { Quantity = 0 };

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            proof
                .Services.GetRequiredService<IStockReservationIntake>()
                .ReceiveAsync(delivery, command, Token)
        );
        Assert.Equal(
            0,
            await LocalWorkflowProof.CountAsync(
                proof.Inventory,
                "SELECT count(*) FROM inventory.inbox_messages",
                Token
            )
        );
    }

    [Fact]
    public async Task CompetingWorkersPreserveStockAndResolveConflictingPlacementAsBusinessRefusal()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(45));
        var token = deadline.Token;
        await using var proof = await LocalWorkflowProof.CreateAsync(postgres, token);
        Guid first = await proof.SeedAsync(token),
            second = await proof.SeedAsync(token);
        var one = await proof.StartAsync(LocalWorkflowProof.Request(first, second), token);
        var two = await proof.StartAsync(LocalWorkflowProof.Request(first, second), token);
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddLocalReservationDemo(proof.Sales, proof.Inventory);
        builder.Services.AddLocalReservationWorkers();
        builder.Services.AddInboxWorker<InventoryDbContext>(
            StockReservationAdmission.Subscription,
            new(TimeSpan.FromMilliseconds(50), TimeSpan.FromMilliseconds(100))
        );
        using var workers = builder.Build();
        await workers.StartAsync(token);
        try
        {
            await LocalWorkflowProof.EventuallyAsync(
                async () =>
                {
                    var statuses = new[]
                    {
                        (await proof.ReadAsync(one.OrderId, token)).Status,
                        (await proof.ReadAsync(two.OrderId, token)).Status,
                    };
                    return statuses.Contains(StockReservationOrderStatus.Reserved)
                        && statuses.Contains(StockReservationOrderStatus.Declined);
                },
                token
            );
        }
        finally
        {
            await workers.StopAsync(CancellationToken.None);
        }

        Assert.Equal(2, (await proof.StockAsync(first, token)).Reserved);
        Assert.Equal(3, (await proof.StockAsync(first, token)).Version);
        Assert.Equal(2, (await proof.StockAsync(second, token)).Reserved);
    }

    [Fact]
    public async Task AdmittedCompensationRefusalRequiresExplicitOperatorAttention()
    {
        await using var proof = await LocalWorkflowProof.CreateAsync(postgres, Token);
        Guid first = await proof.SeedAsync(Token),
            second = await proof.SeedAsync(Token);
        var started = await proof.StartAsync(LocalWorkflowProof.Request(first, second, 6), Token);
        await proof.StepAsync(Token);
        await proof.StepAsync(Token);
        var release = await proof.PendingAsync<SalesDbContext>(Token);
        // Deliberately simulate Inventory's business refusal through the public outcome Contract.
        await proof
            .Services.GetRequiredService<IStockReservationOutcomeIntake>()
            .ReceiveAsync(
                new ReservationDelivery(
                    Guid.NewGuid(),
                    LocalWorkflowProof.Alpha,
                    started.OrderId,
                    release.MessageId
                ),
                new StockReservationReleasedV1(
                    started.FirstReservationId,
                    StockReservationReleaseDecision.ReservationNotFound
                ),
                Token
            );
        Assert.Equal(
            InboxProcessingResult.Processed,
            await proof.ProcessAsync<SalesDbContext>(Token)
        );
        var state = await proof.ReadAsync(started.OrderId, Token);
        Assert.Equal(StockReservationOrderStatus.NeedsAttention, state.Status);
        Assert.Equal("ReservationNotFound", state.CompensationRefusal);
        Assert.Equal(2, (await proof.StockAsync(first, Token)).Reserved);
    }
}
