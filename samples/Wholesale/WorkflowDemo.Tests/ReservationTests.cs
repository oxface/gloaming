using System.Text.Json;
using Gloaming.Messaging.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Inventory;
using ModulithFoundry.Samples.Wholesale.Inventory.Contracts;
using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;
using ModulithFoundry.Tests.Infrastructure;

namespace ModulithFoundry.Samples.Wholesale.WorkflowDemo.Tests;

public sealed class ReservationTests(PostgreSqlFixture postgres, RabbitMqFixture rabbit)
    : IClassFixture<PostgreSqlFixture>,
        IClassFixture<RabbitMqFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<ReservationProof> ProofAsync() =>
        new(await WorkflowProof.CreateAsync(postgres, rabbit, Token));

    internal static StartStockReservationOrder Request(
        Guid first,
        Guid second,
        decimal secondQuantity = 2,
        long secondVersion = 2
    ) => new(Guid.NewGuid(), new(first, 2, 2), new(second, secondVersion, secondQuantity));

    [Fact]
    public async Task BothLinesReserveWithoutChangingPhysicalStockAndNativeFiltersUseAvailability()
    {
        await using var proof = await ProofAsync();
        Guid first = await proof.Workflow.SeedAsync(Token);
        Guid second = await proof.Workflow.SeedAsync(Token);
        var started = await proof.StartAsync(Request(first, second), Token);
        await proof.StepAsync(Token);
        Assert.Equal(
            StockReservationOrderStatus.AwaitingSecond,
            (await proof.ReadAsync(started.OrderId, Token))!.Status
        );
        await proof.StepAsync(Token);
        var final = await proof.ReadAsync(started.OrderId, Token);
        Assert.Equal(StockReservationOrderStatus.Reserved, final!.Status);
        Assert.Equal(3, final.Version);
        foreach (Guid stock in new[] { first, second })
        {
            var state = await proof.StockAsync(stock, Token);
            Assert.Equal(5, state.OnHand);
            Assert.Equal(2, state.Reserved);
            Assert.Equal(3, state.Available);
            Assert.Equal(state, await proof.StockAsync(stock, Token, history: true));
        }

        await using var scope = proof.Workflow.Scope();
        var queries = scope.ServiceProvider.GetRequiredService<IStockPositionQueries>();
        Assert.Empty(await queries.ReadAvailableAsync(4, Token));
        Assert.Equal(2, (await queries.ReadAvailableAsync(3, Token)).Count);
        var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        var denied = await scope
            .ServiceProvider.GetRequiredService<IStockPositionCommands>()
            .IssueAsync(new(first, 3, [new StockIssue(4)]), Token);
        Assert.Equal(
            3,
            Assert.IsType<StockPositionChangeResult.InsufficientStock>(denied).Available
        );
    }

    [Theory]
    [InlineData(6, 2, false, "InsufficientStock")]
    [InlineData(2, 1, false, "Conflict")]
    [InlineData(2, 2, true, "NotFound")]
    public async Task SecondRefusalRequiresAcknowledgedReleaseBeforeCompensationCompletes(
        int quantity,
        long version,
        bool missing,
        string refusal
    )
    {
        await using var proof = await ProofAsync();
        Guid first = await proof.Workflow.SeedAsync(Token);
        Guid second = missing ? Guid.NewGuid() : await proof.Workflow.SeedAsync(Token);
        var started = await proof.StartAsync(Request(first, second, quantity, version), Token);
        await proof.StepAsync(Token);
        await proof.StepAsync(Token);
        Assert.Equal(
            StockReservationOrderStatus.Releasing,
            (await proof.ReadAsync(started.OrderId, Token))!.Status
        );
        Assert.Equal(2, (await proof.StockAsync(first, Token)).Reserved);
        await proof.SendCommandAsync(Token, release: true);
        Assert.Equal(
            StockReservationOrderStatus.Releasing,
            (await proof.ReadAsync(started.OrderId, Token))!.Status
        );
        Assert.Equal(0, (await proof.StockAsync(first, Token)).Reserved);
        await proof.ReplyAsync(Token);
        var final = await proof.ReadAsync(started.OrderId, Token);
        Assert.Equal(StockReservationOrderStatus.Compensated, final!.Status);
        Assert.Equal(refusal, final.Refusal);
        Assert.Equal(4, final.Version);
        var stock = await proof.StockAsync(first, Token);
        Assert.Equal(5, stock.OnHand);
        Assert.Equal(4, stock.Version);
        Assert.Equal(stock, await proof.StockAsync(first, Token, history: true));
    }

    [Fact]
    public async Task FirstRefusalDoesNotRequestSecondLineOrCompensation()
    {
        await using var proof = await ProofAsync();
        var request = Request(Guid.NewGuid(), Guid.NewGuid());
        await proof.StartAsync(request, Token);
        await proof.StepAsync(Token);
        Assert.Equal(
            StockReservationOrderStatus.Declined,
            (await proof.ReadAsync(request.OrderId, Token))!.Status
        );
        Assert.Equal(
            1,
            await WorkflowProof.CountAsync(
                proof.Workflow.SalesConnection,
                "SELECT count(*) FROM sales.outbox_messages",
                Token
            )
        );
        Assert.Equal(
            0,
            await WorkflowProof.CountAsync(
                proof.Workflow.SalesConnection,
                "SELECT count(*) FROM sales.workflow_instances, LATERAL jsonb_array_elements(\"Operations\") attempt WHERE attempt->>'requestName' = 'inventory.release-reservation'",
                Token
            )
        );
    }

    [Fact]
    public async Task SameStartIsStableAndChangedInputsOrCrossOrganizationDoNotReuseProgress()
    {
        await using var proof = await ProofAsync();
        var request = Request(Guid.NewGuid(), Guid.NewGuid());
        var first = await proof.StartAsync(request, Token);
        Assert.Equal(first, await proof.StartAsync(request, Token));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            proof.StartAsync(request with { Second = request.Second with { Quantity = 3 } }, Token)
        );
        Assert.Null(await proof.ReadAsync(first.OrderId, Token, WorkflowProof.Beta));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            proof.StartAsync(request, Token, "unadmitted")
        );
        Assert.Equal(
            1,
            await WorkflowProof.CountAsync(
                proof.Workflow.SalesConnection,
                "SELECT count(*) FROM sales.outbox_messages",
                Token
            )
        );
    }

    [Fact]
    public async Task DuplicateRepliesDoNotEnqueueMoreStepsAndConflictingRepliesRollBack()
    {
        await using var proof = await ProofAsync();
        Guid first = await proof.Workflow.SeedAsync(Token);
        Guid second = await proof.Workflow.SeedAsync(Token);
        var order = await proof.StartAsync(Request(first, second, 6), Token);
        var accepted = await proof.StepAsync(Token);
        var refused = await proof.StepAsync(Token);
        var released = await proof.StepAsync(Token, release: true);
        foreach (var reply in new[] { accepted, refused, released, refused, accepted })
        {
            await proof.IntakeAsync<SalesDbContext>(
                StockReservationReplyAdmission.Subscription,
                ReservationProof.Redelivery(reply),
                Token
            );
            Assert.Equal(
                InboxProcessingResult.Processed,
                await proof.Workflow.ProcessAsync<SalesDbContext>(
                    StockReservationReplyAdmission.Subscription,
                    Token
                )
            );
        }

        Assert.Equal(4, (await proof.ReadAsync(order.OrderId, Token))!.Version);
        Assert.Equal(
            3,
            await WorkflowProof.CountAsync(
                proof.Workflow.SalesConnection,
                "SELECT count(*) FROM sales.outbox_messages",
                Token
            )
        );
        var conflicting = new StockReservationDecidedV1(
            order.FirstReservationId,
            first,
            2,
            2,
            StockReservationDecision.NotFound
        );
        await proof.IntakeAsync<SalesDbContext>(
            StockReservationReplyAdmission.Subscription,
            ReservationProof.Redelivery(accepted, payload: conflicting),
            Token
        );
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            proof.Workflow.ProcessAsync<SalesDbContext>(
                StockReservationReplyAdmission.Subscription,
                Token
            )
        );
        Assert.Equal(
            StockReservationOrderStatus.Compensated,
            (await proof.ReadAsync(order.OrderId, Token))!.Status
        );
    }

    [Fact]
    public async Task ReserveAndReleaseBusinessRetriesAcrossDeliveryIdsCannotResurrectReleasedStock()
    {
        await using var proof = await ProofAsync();
        Guid first = await proof.Workflow.SeedAsync(Token);
        Guid second = await proof.Workflow.SeedAsync(Token);
        var order = await proof.StartAsync(Request(first, second, 6), Token);
        var original = await proof.PendingReserveAsync(Token);
        await proof.StepAsync(Token);
        await proof.StepAsync(Token);
        await proof.StepAsync(Token, release: true);

        await proof.EnqueueAsync("inventory.reserve-stock", original, order.OrderId, Token);
        await proof.SendCommandAsync(Token);
        Assert.Equal(
            StockReservationDecision.Reserved,
            (await proof.PendingReplyAsync(Token))
                .Payload.GetProperty("decision")
                .Deserialize<StockReservationDecision>()
        );
        // The command was a semantic retry with a new cause. This focused Inventory proof
        // inspects its outcome without presenting it as an issued Sales step.
        Assert.Equal(4, (await proof.StockAsync(first, Token)).Version);
        Assert.Equal(0, (await proof.StockAsync(first, Token)).Reserved);
        await proof.EnqueueAsync(
            "inventory.release-reservation",
            new ReleaseStockReservationV1(order.FirstReservationId),
            order.OrderId,
            Token
        );
        await proof.SendCommandAsync(Token, release: true);
        Assert.Equal(4, (await proof.StockAsync(first, Token)).Version);

        await proof.EnqueueAsync(
            "inventory.reserve-stock",
            original with
            {
                Quantity = 3,
            },
            order.OrderId,
            Token
        );
        Assert.Equal(
            OutboxDispatchResult.Published,
            await proof.Workflow.DispatchAsync<SalesDbContext>(Token)
        );
        Assert.True(
            await proof.Workflow.Broker.ReceiveReservationAsync(
                proof.Workflow.Services.GetRequiredService<IServiceScopeFactory>(),
                Token
            )
        );
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            proof.Workflow.ProcessAsync<InventoryDbContext>(
                StockReservationAdmission.Subscription,
                Token
            )
        );
        Assert.Equal(0, (await proof.StockAsync(first, Token)).Reserved);
    }

    [Fact]
    public async Task CompensationUsesCurrentStockAndPreservesUnrelatedReceiptAndIssue()
    {
        await using var proof = await ProofAsync();
        Guid first = await proof.Workflow.SeedAsync(Token);
        Guid second = await proof.Workflow.SeedAsync(Token);
        await proof.StartAsync(Request(first, second, 6), Token);
        await proof.StepAsync(Token);
        await using (var scope = proof.Workflow.Scope())
        {
            var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.IsType<StockPositionChangeResult.Changed>(
                await scope
                    .ServiceProvider.GetRequiredService<IStockPositionCommands>()
                    .ReceiveAsync(new(first, 3, [new StockReceipt(7)]), Token)
            );
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }

        // Use the existing direct decision; discard its unrelated integration outcome only
        // through a separate test database so it cannot be confused with saga routing.
        await using (var scope = proof.Workflow.Scope())
        {
            var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.IsType<StockPositionChangeResult.Changed>(
                await scope
                    .ServiceProvider.GetRequiredService<IStockPositionCommands>()
                    .IssueAsync(new(first, 4, [new StockIssue(1)]), Token)
            );
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }

        await WorkflowProof.SqlAsync(
            proof.Workflow.InventoryConnection,
            "DELETE FROM inventory.outbox_messages WHERE message_name = 'inventory.stock-issue-recorded'",
            Token
        );
        await proof.StepAsync(Token);
        await proof.StepAsync(Token, release: true);
        var stock = await proof.StockAsync(first, Token);
        Assert.Equal(11, stock.OnHand);
        Assert.Equal(0, stock.Reserved);
        Assert.Equal(6, stock.Version);
        Assert.Equal(stock, await proof.StockAsync(first, Token, history: true));
    }

    [Fact]
    public async Task RefusedReleaseIsVisibleAndExplicitRetryIgnoresOldReplyAfterSuccess()
    {
        await using var proof = await ProofAsync();
        Guid first = await proof.Workflow.SeedAsync(Token);
        Guid second = await proof.Workflow.SeedAsync(Token);
        var order = await proof.StartAsync(Request(first, second, 6), Token);
        await proof.StepAsync(Token);
        await proof.StepAsync(Token);
        await proof.SendCommandAsync(Token, release: true);
        var actual = await proof.PendingReplyAsync(Token);
        // Focused consumer-policy proof: simulate a business refusal for the first attempt.
        var refused = ReservationProof.Redelivery(
            actual,
            payload: new StockReservationReleasedV1(
                order.FirstReservationId,
                StockReservationReleaseDecision.ReservationNotFound
            )
        );
        await proof.IntakeAsync<SalesDbContext>(
            StockReservationReplyAdmission.Subscription,
            refused,
            Token
        );
        Assert.Equal(
            InboxProcessingResult.Processed,
            await proof.Workflow.ProcessAsync<SalesDbContext>(
                StockReservationReplyAdmission.Subscription,
                Token
            )
        );
        Assert.Equal(
            StockReservationOrderStatus.NeedsAttention,
            (await proof.ReadAsync(order.OrderId, Token))!.Status
        );
        Assert.Equal(
            "ReservationNotFound",
            (await proof.ReadAsync(order.OrderId, Token))!.CompensationRefusal
        );
        await proof.RetryAsync(order.OrderId, Token);
        // Keep the real first-attempt outcome out of this deliberately simulated policy case.
        await WorkflowProof.SqlAsync(
            proof.Workflow.InventoryConnection,
            "DELETE FROM inventory.outbox_messages WHERE dispatched_at IS NULL",
            Token
        );
        await proof.StepAsync(Token, release: true);
        await proof.IntakeAsync<SalesDbContext>(
            StockReservationReplyAdmission.Subscription,
            ReservationProof.Redelivery(refused),
            Token
        );
        Assert.Equal(
            InboxProcessingResult.Processed,
            await proof.Workflow.ProcessAsync<SalesDbContext>(
                StockReservationReplyAdmission.Subscription,
                Token
            )
        );
        var final = await proof.ReadAsync(order.OrderId, Token);
        Assert.Equal(StockReservationOrderStatus.Compensated, final!.Status);
        Assert.Equal(6, final.Version);
        Assert.Equal(4, (await proof.StockAsync(first, Token)).Version);
    }

    [Fact]
    public async Task HistoricalInlineJsonWithoutReservedStillLoadsFiltersAndReserves()
    {
        await using var proof = await ProofAsync();
        Guid first = await proof.Workflow.SeedAsync(Token);
        Guid second = await proof.Workflow.SeedAsync(Token);
        await WorkflowProof.SqlAsync(
            proof.Workflow.InventoryConnection,
            "UPDATE inventory.stock_position_current SET state = state - 'reserved'",
            Token
        );
        await using (var scope = proof.Workflow.Scope())
        {
            Assert.Equal(
                2,
                (
                    await scope
                        .ServiceProvider.GetRequiredService<IStockPositionQueries>()
                        .ReadAvailableAsync(5, Token)
                ).Count
            );
        }

        Assert.Equal(0, (await proof.StockAsync(first, Token)).Reserved);
        await proof.StartAsync(Request(first, second), Token);
        await proof.StepAsync(Token);
        Assert.Equal(2, (await proof.StockAsync(first, Token)).Reserved);
        Assert.Equal(
            await proof.StockAsync(first, Token),
            await proof.StockAsync(first, Token, history: true)
        );
    }

    [Theory]
    [InlineData("cause")]
    [InlineData("correlation")]
    [InlineData("tenant")]
    [InlineData("producer")]
    [InlineData("reservation")]
    [InlineData("quantity")]
    public async Task UnrelatedOrInvalidReplyCannotAdvanceAnotherOrder(string mismatch)
    {
        await using var proof = await ProofAsync();
        Guid first = await proof.Workflow.SeedAsync(Token);
        Guid second = await proof.Workflow.SeedAsync(Token);
        var order = await proof.StartAsync(Request(first, second), Token);
        await proof.SendCommandAsync(Token);
        var actual = await proof.PendingReplyAsync(Token);
        var outcome = actual.Payload.Deserialize<StockReservationDecidedV1>(WorkflowProof.Json)!;
        if (mismatch == "reservation")
            outcome = outcome with { ReservationId = Guid.NewGuid() };
        if (mismatch == "quantity")
            outcome = outcome with { Quantity = 3 };

        var invalid = new Gloaming.Messaging.IncomingMessage(
            Guid.NewGuid(),
            mismatch == "producer" ? "untrusted" : "inventory",
            actual.MessageName,
            actual.SchemaVersion,
            JsonSerializer.SerializeToElement(outcome, WorkflowProof.Json),
            mismatch == "tenant" ? WorkflowProof.Beta : actual.TenantKey,
            mismatch == "correlation" ? Guid.NewGuid().ToString("D") : actual.CorrelationId,
            mismatch == "cause" ? Guid.NewGuid().ToString("D") : actual.CausationId
        );
        await proof.IntakeAsync<SalesDbContext>(
            StockReservationReplyAdmission.Subscription,
            invalid,
            Token
        );
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            proof.Workflow.ProcessAsync<SalesDbContext>(
                StockReservationReplyAdmission.Subscription,
                Token
            )
        );
        Assert.Equal(
            StockReservationOrderStatus.AwaitingFirst,
            (await proof.ReadAsync(order.OrderId, Token))!.Status
        );
        Assert.Equal(
            1,
            await WorkflowProof.CountAsync(
                proof.Workflow.SalesConnection,
                "SELECT count(*) FROM sales.outbox_messages",
                Token
            )
        );
        Assert.Equal(
            1,
            await WorkflowProof.CountAsync(
                proof.Workflow.SalesConnection,
                "SELECT count(*) FROM sales.inbox_messages WHERE processed_at IS NULL",
                Token
            )
        );
    }
}
