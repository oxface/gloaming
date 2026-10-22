using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.MessagingWorkerDemo.Tests;
using ModulithFoundry.Samples.Wholesale.Inventory;
using ModulithFoundry.Samples.Wholesale.Inventory.Contracts;
using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;
using ModulithFoundry.Samples.Wholesale.Worker.Broker;
using ModulithFoundry.Tests.Infrastructure;
using Npgsql;

namespace ModulithFoundry.Samples.Wholesale.IntegrationTests.Reservations.Broker;

public sealed class ReservationRecoveryTests(PostgreSqlFixture postgres, RabbitMqFixture rabbit)
    : IClassFixture<PostgreSqlFixture>,
        IClassFixture<RabbitMqFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CompetingSemanticCommandsChangeStockOnceAndFreshRetryReturnsRetainedOutcome(
        bool release
    )
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(Token);
        bounded.CancelAfter(TimeSpan.FromSeconds(30));
        var token = bounded.Token;
        await using var proof = new ReservationProof(
            await WorkflowProof.CreateAsync(postgres, rabbit, token)
        );
        Guid first = await proof.Workflow.SeedAsync(token);
        Guid second = await proof.Workflow.SeedAsync(token);
        var order = await proof.StartAsync(ReservationTests.Request(first, second, 6), token);
        object command = await proof.PendingReserveAsync(token);
        string name = "inventory.reserve-stock";
        if (release)
        {
            await proof.StepAsync(token);
            await proof.StepAsync(token);
            command = new ReleaseStockReservationV1(order.FirstReservationId);
            name = "inventory.release-reservation";
        }

        await proof.EnqueueAsync(name, command, order.OrderId, token);
        for (int delivery = 0; delivery < 2; delivery++)
        {
            Assert.Equal(
                OutboxDispatchResult.Published,
                await proof.Workflow.DispatchAsync<SalesDbContext>(token)
            );
            var scopes = proof.Workflow.Services.GetRequiredService<IServiceScopeFactory>();
            Assert.True(
                release
                    ? await proof.Workflow.Broker.ReceiveReleaseAsync(scopes, token)
                    : await proof.Workflow.Broker.ReceiveReservationAsync(scopes, token)
            );
        }

        await using var barrier = await WorkflowBarrier.HoldAsync(
            proof.Workflow.InventoryConnection,
            false,
            token,
            "inventory.event_streams"
        );
        var firstWriter = CaptureAsync(() =>
            proof.Workflow.ProcessAsync<InventoryDbContext>(
                StockReservationAdmission.Subscription,
                token
            )
        );
        await barrier.WaitForAdvisoryAsync(1, token);
        var secondWriter = CaptureAsync(() =>
            proof.Workflow.ProcessAsync<InventoryDbContext>(
                StockReservationAdmission.Subscription,
                token
            )
        );
        await barrier.WaitForRowWriterAsync(token);
        await barrier.ReleaseAsync(token);
        var outcomes = await Task.WhenAll(firstWriter, secondWriter);
        Assert.Single(outcomes, failure => failure is null);
        AssertWriteConflict(Assert.Single(outcomes, failure => failure is not null)!);
        await ReservationFailureTests.RetryAsync<InventoryDbContext>(
            proof,
            StockReservationAdmission.Subscription,
            token
        );
        var stock = await proof.StockAsync(first, token);
        Assert.Equal(release ? 4 : 3, stock.Version);
        Assert.Equal(release ? 0 : 2, stock.Reserved);
        Assert.Equal(
            2,
            await WorkflowProof.CountAsync(
                proof.Workflow.InventoryConnection,
                $"SELECT count(*) FROM inventory.inbox_messages WHERE processed_at IS NOT NULL AND message_name = '{name}'",
                token
            )
        );
    }

    [Fact]
    public async Task CompetingOrdersCannotReserveMoreThanAvailable()
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(Token);
        bounded.CancelAfter(TimeSpan.FromSeconds(30));
        var token = bounded.Token;
        await using var proof = new ReservationProof(
            await WorkflowProof.CreateAsync(postgres, rabbit, token)
        );
        Guid stock = await proof.Workflow.SeedAsync(token);
        for (int delivery = 0; delivery < 2; delivery++)
        {
            await proof.StartAsync(
                new(Guid.NewGuid(), new(stock, 2, 4), new(Guid.NewGuid(), 2, 1)),
                token
            );
            Assert.Equal(
                OutboxDispatchResult.Published,
                await proof.Workflow.DispatchAsync<SalesDbContext>(token)
            );
            Assert.True(
                await proof.Workflow.Broker.ReceiveReservationAsync(
                    proof.Workflow.Services.GetRequiredService<IServiceScopeFactory>(),
                    token
                )
            );
        }

        await using var barrier = await WorkflowBarrier.HoldAsync(
            proof.Workflow.InventoryConnection,
            false,
            token,
            "inventory.event_streams"
        );
        var firstWriter = CaptureAsync(() =>
            proof.Workflow.ProcessAsync<InventoryDbContext>(
                StockReservationAdmission.Subscription,
                token
            )
        );
        await barrier.WaitForAdvisoryAsync(1, token);
        var secondWriter = CaptureAsync(() =>
            proof.Workflow.ProcessAsync<InventoryDbContext>(
                StockReservationAdmission.Subscription,
                token
            )
        );
        await barrier.WaitForRowWriterAsync(token);
        await barrier.ReleaseAsync(token);
        var failures = await Task.WhenAll(firstWriter, secondWriter);
        Assert.Single(failures, failure => failure is null);
        AssertWriteConflict(Assert.Single(failures, failure => failure is not null)!);
        await ReservationFailureTests.RetryAsync<InventoryDbContext>(
            proof,
            StockReservationAdmission.Subscription,
            token
        );
        Assert.Equal(4, (await proof.StockAsync(stock, token)).Reserved);
        Assert.Equal(
            1,
            await WorkflowProof.CountAsync(
                proof.Workflow.InventoryConnection,
                "SELECT count(*) FROM inventory.stock_reservations WHERE \"Decision\" = 1",
                token
            )
        );
        Assert.Equal(
            1,
            await WorkflowProof.CountAsync(
                proof.Workflow.InventoryConnection,
                "SELECT count(*) FROM inventory.stock_reservations WHERE \"Decision\" = 3",
                token
            )
        );
    }

    [Fact]
    public async Task ConcurrentSecondRefusalsQueueOneCompensationAndCompleteBothDeliveries()
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(Token);
        bounded.CancelAfter(TimeSpan.FromSeconds(30));
        var token = bounded.Token;
        await using var proof = new ReservationProof(
            await WorkflowProof.CreateAsync(postgres, rabbit, token)
        );
        Guid first = await proof.Workflow.SeedAsync(token);
        Guid second = await proof.Workflow.SeedAsync(token);
        var order = await proof.StartAsync(ReservationTests.Request(first, second, 6), token);
        await proof.StepAsync(token);
        await proof.SendCommandAsync(token);
        var refusal = await proof.PendingReplyAsync(token);
        for (int delivery = 0; delivery < 2; delivery++)
        {
            await proof.IntakeAsync<SalesDbContext>(
                StockReservationReplyAdmission.Subscription,
                ReservationProof.Redelivery(refusal),
                token
            );
        }

        await using var barrier = await WorkflowBarrier.HoldAsync(
            proof.Workflow.SalesConnection,
            false,
            token,
            "sales.workflow_instances"
        );
        var firstWriter = CaptureAsync(() =>
            proof.Workflow.ProcessAsync<SalesDbContext>(
                StockReservationReplyAdmission.Subscription,
                token
            )
        );
        await barrier.WaitForAdvisoryAsync(1, token);
        var secondWriter = CaptureAsync(() =>
            proof.Workflow.ProcessAsync<SalesDbContext>(
                StockReservationReplyAdmission.Subscription,
                token
            )
        );
        await barrier.WaitForRowWriterAsync(token);
        await barrier.ReleaseAsync(token);
        var failures = await Task.WhenAll(firstWriter, secondWriter);
        Assert.Single(failures, failure => failure is null);
        Assert.IsType<DbUpdateConcurrencyException>(
            Assert.Single(failures, failure => failure is not null)
        );
        await ReservationFailureTests.RetryAsync<SalesDbContext>(
            proof,
            StockReservationReplyAdmission.Subscription,
            token
        );
        Assert.Equal(
            StockReservationOrderStatus.Releasing,
            (await proof.ReadAsync(order.OrderId, token))!.Status
        );
        Assert.Equal(3, (await proof.ReadAsync(order.OrderId, token))!.Version);
        Assert.Equal(
            1,
            await WorkflowProof.CountAsync(
                proof.Workflow.SalesConnection,
                "SELECT count(*) FROM sales.workflow_instances, LATERAL jsonb_array_elements(\"Operations\") attempt WHERE attempt->>'requestName' = 'inventory.release-reservation'",
                token
            )
        );
        Assert.Equal(
            1,
            await WorkflowProof.CountAsync(
                proof.Workflow.SalesConnection,
                "SELECT count(*) FROM sales.outbox_messages WHERE message_name = 'inventory.release-reservation'",
                token
            )
        );
    }

    [Fact]
    public async Task KilledHostAfterCommittedReleaseRecoversReplyWithoutRepeatingCompensation()
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(Token);
        bounded.CancelAfter(TimeSpan.FromSeconds(60));
        var token = bounded.Token;
        await using var proof = new ReservationProof(
            await WorkflowProof.CreateAsync(postgres, rabbit, token)
        );
        Guid first = await proof.Workflow.SeedAsync(token);
        Guid second = await proof.Workflow.SeedAsync(token);
        var order = await proof.StartAsync(ReservationTests.Request(first, second, 6), token);
        await using var barrier = await WorkflowBarrier.HoldAsync(
            proof.Workflow.SalesConnection,
            false,
            token,
            "sales.workflow_instances",
            "NEW.\"Version\" = 4"
        );
        await using (
            var worker = new ChildHost(
                typeof(WorkflowComposition).Assembly,
                proof.Workflow.Environment,
                "--role",
                "run"
            )
        )
        {
            await barrier.WaitForAdvisoryAsync(1, token);
            Assert.Equal(0, (await proof.StockAsync(first, token)).Reserved);
            Assert.Equal(4, (await proof.StockAsync(first, token)).Version);
            await worker.KillAsync(token);
        }

        Assert.Equal(
            StockReservationOrderStatus.Releasing,
            (await proof.ReadAsync(order.OrderId, token))!.Status
        );
        await barrier.ReleaseAsync(token);
        await using var replacement = new ChildHost(
            typeof(WorkflowComposition).Assembly,
            proof.Workflow.Environment,
            "--role",
            "run"
        );
        await WorkflowProof.EventuallyAsync(
            async () =>
                (await proof.ReadAsync(order.OrderId, token))!.Status
                == StockReservationOrderStatus.Compensated,
            token
        );
        await replacement.StopAsync(token);
        Assert.Equal(4, (await proof.StockAsync(first, token)).Version);
        Assert.Equal(
            1,
            await WorkflowProof.CountAsync(
                proof.Workflow.SalesConnection,
                "SELECT count(*) FROM sales.outbox_messages WHERE message_name = 'inventory.release-reservation'",
                token
            )
        );
    }

    [Fact]
    public async Task FiniteReservationStartAndReadCommandsComposeWithTheNativeWorker()
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(Token);
        bounded.CancelAfter(TimeSpan.FromSeconds(60));
        var token = bounded.Token;
        await using var proof = new ReservationProof(
            await WorkflowProof.CreateAsync(postgres, rabbit, token)
        );
        Guid first = await proof.Workflow.SeedAsync(token);
        Guid second = await proof.Workflow.SeedAsync(token);
        Guid id = Guid.NewGuid();
        await using (
            var start = new ChildHost(
                typeof(WorkflowComposition).Assembly,
                proof.Workflow.Environment,
                "--role",
                "reserve-order",
                "--OrderId",
                id.ToString("D"),
                "--FirstStockId",
                first.ToString("D"),
                "--SecondStockId",
                second.ToString("D"),
                "--SecondQuantity",
                "6"
            )
        )
            Assert.True(await start.WaitForExitAsync(token) == 0, start.Output);

        await using (
            var worker = new ChildHost(
                typeof(WorkflowComposition).Assembly,
                proof.Workflow.Environment,
                "--role",
                "run"
            )
        )
        {
            await WorkflowProof.EventuallyAsync(
                async () =>
                    (await proof.ReadAsync(id, token))!.Status
                    == StockReservationOrderStatus.Compensated,
                token
            );
            await worker.StopAsync(token);
        }

        await using var read = new ChildHost(
            typeof(WorkflowComposition).Assembly,
            proof.Workflow.Environment,
            "--role",
            "read-order",
            "--OrderId",
            id.ToString("D")
        );
        Assert.True(await read.WaitForExitAsync(token) == 0, read.Output);
        Assert.Contains(id.ToString("D"), read.Output, StringComparison.Ordinal);
    }

    private static async Task<Exception?> CaptureAsync(Func<Task<InboxProcessingResult>> operation)
    {
        try
        {
            Assert.Equal(InboxProcessingResult.Processed, await operation());
            return null;
        }
        catch (Exception failure)
        {
            return failure;
        }
    }

    private static void AssertWriteConflict(Exception failure)
    {
        var update = Assert.IsAssignableFrom<DbUpdateException>(failure);
        if (update is not DbUpdateConcurrencyException)
        {
            // EF may insert an event before the versioned stream UPDATE. The existing
            // per-stream position constraint also arbitrates the race inside the transaction.
            var uniqueness = Assert.IsType<PostgresException>(update.InnerException);
            Assert.Equal(PostgresErrorCodes.UniqueViolation, uniqueness.SqlState);
        }
    }
}
