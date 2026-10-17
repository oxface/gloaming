using Gloaming.Messaging.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.MessagingWorkerDemo.Tests;
using ModulithFoundry.Samples.Wholesale.Inventory;
using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;
using Npgsql;

namespace ModulithFoundry.Samples.Wholesale.WorkflowDemo.Tests;

public sealed partial class SalesReplyTests
{
    [Fact(Timeout = 90_000)]
    public async Task RealBrokerRepliesAndRestrictedDispatcherCompleteReservationThroughTheRlsWorker()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using var proof = await SalesReplyProof.CreateAsync(postgres, rabbit, token);
        Guid first = await proof.Workflow.SeedAsync(token);
        Guid second = await proof.Workflow.SeedAsync(token);
        var started = await proof.Reservations.StartAsync(
            ReservationTests.Request(first, second),
            token
        );
        string dispatch = new NpgsqlConnectionStringBuilder(proof.Workflow.SalesConnection)
        {
            Username = "dispatch_" + Guid.NewGuid().ToString("N"),
            Password = "a29-dispatch",
        }.ConnectionString;
        await using (
            var setup = new ChildHost(
                typeof(WorkflowComposition).Assembly,
                new()
                {
                    ["ConnectionStrings__SalesDispatchSetup"] = proof.Workflow.SalesConnection,
                    ["ConnectionStrings__SalesDispatch"] = dispatch,
                },
                "--role",
                "setup-sales-dispatch"
            )
        )
            Assert.True(await setup.WaitForExitAsync(token) == 0, setup.Output);

        await using var dispatcher = new ChildHost(
            typeof(WorkflowComposition).Assembly,
            new()
            {
                ["ConnectionStrings__SalesDispatch"] = dispatch,
                ["ConnectionStrings__RabbitMq"] = proof.Workflow.Environment[
                    "ConnectionStrings__RabbitMq"
                ],
                ["QueuePrefix"] = proof.Workflow.Environment["QueuePrefix"],
                ["Parameters__postgres-password"] = "",
            },
            "--role",
            "dispatch-sales"
        );
        await using var processor = proof.StartWorker();
        await dispatcher.WaitForOutputAsync("Application started", token);
        await processor.WaitForOutputAsync("Application started", token);
        var scopes = proof.Workflow.Services.GetRequiredService<IServiceScopeFactory>();
        foreach (
            var expected in new[]
            {
                StockReservationOrderStatus.AwaitingSecond,
                StockReservationOrderStatus.Reserved,
            }
        )
        {
            await WorkflowProof.EventuallyAsync(
                () => proof.Workflow.Broker.ReceiveReservationAsync(scopes, token),
                token
            );
            Assert.Equal(
                InboxProcessingResult.Processed,
                await proof.Workflow.ProcessAsync<InventoryDbContext>(
                    StockReservationAdmission.Subscription,
                    token
                )
            );
            Assert.Equal(
                OutboxDispatchResult.Published,
                await proof.Workflow.DispatchAsync<InventoryDbContext>(token)
            );
            Assert.True(await proof.Workflow.Broker.ReceiveReservationReplyAsync(scopes, token));
            await WorkflowProof.EventuallyAsync(
                async () =>
                    (await proof.Reservations.ReadAsync(started.OrderId, token))!.Status
                    == expected,
                token
            );
        }

        await processor.StopAsync(token);
        await dispatcher.StopAsync(token);
        Assert.Equal(3, (await proof.Reservations.ReadAsync(started.OrderId, token))!.Version);
        Assert.Equal(
            2,
            await proof.CountAsync(
                "SELECT count(*) FROM sales.inbox_messages WHERE processed_at IS NOT NULL",
                token
            )
        );
        Assert.Equal(
            2,
            await proof.CountAsync(
                "SELECT count(*) FROM sales.outbox_messages WHERE dispatched_at IS NOT NULL",
                token
            )
        );
        Assert.Equal(
            1,
            await proof.CountAsync(
                "SELECT count(*) FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'sales' AND c.relname = 'outbox_messages' AND c.relrowsecurity",
                token
            )
        );
    }

    [Fact(Timeout = 60_000)]
    public async Task CompetingRlsWorkersSkipTheLockedDeliveryAndCommitEachContinuationOnce()
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using var proof = await SalesReplyProof.CreateAsync(postgres, rabbit, token);
        var first = await proof.Reservations.StartAsync(SalesReplyProof.Request(), token);
        var second = await proof.Reservations.StartAsync(SalesReplyProof.Request(), token);
        await proof.IntakeAsync(await proof.ReplyAsync(first.OrderId, token), token);
        await proof.IntakeAsync(await proof.ReplyAsync(second.OrderId, token), token);
        await using var barrier = await WorkflowBarrier.HoldAsync(
            proof.Workflow.SalesConnection,
            insert: false,
            token,
            table: "sales.workflow_instances",
            condition: $"NEW.\"Id\" = '{first.OrderId}'"
        );
        await using var firstWorker = proof.StartWorker();
        await barrier.WaitForAdvisoryAsync(1, token);
        await using var secondWorker = proof.StartWorker();
        await WorkflowProof.EventuallyAsync(
            async () =>
                (await proof.Reservations.ReadAsync(second.OrderId, token))!.Status
                == StockReservationOrderStatus.AwaitingSecond,
            token
        );
        Assert.Equal(
            StockReservationOrderStatus.AwaitingFirst,
            (await proof.Reservations.ReadAsync(first.OrderId, token))!.Status
        );
        Assert.Equal(
            1,
            await proof.CountAsync(
                "SELECT count(*) FROM sales.inbox_messages WHERE processed_at IS NOT NULL",
                token
            )
        );
        await barrier.ReleaseAsync(token);
        await WorkflowProof.EventuallyAsync(
            async () =>
                await proof.CountAsync(
                    "SELECT count(*) FROM sales.inbox_messages WHERE processed_at IS NOT NULL",
                    token
                ) == 2,
            token
        );
        await firstWorker.StopAsync(token);
        await secondWorker.StopAsync(token);
        Assert.Equal(2, (await proof.Reservations.ReadAsync(first.OrderId, token))!.Version);
        Assert.Equal(2, (await proof.Reservations.ReadAsync(second.OrderId, token))!.Version);
        Assert.Equal(
            4,
            await proof.CountAsync("SELECT count(*) FROM sales.outbox_messages", token)
        );
    }

    [Theory(Timeout = 60_000)]
    [InlineData(false)]
    [InlineData(true)]
    public async Task InFlightShutdownRollsBackProgressOutgoingWorkAndCompletionThenRestartSucceeds(
        bool hardDeath
    )
    {
        CancellationToken token = TestContext.Current.CancellationToken;
        await using var proof = await SalesReplyProof.CreateAsync(postgres, rabbit, token);
        var started = await proof.Reservations.StartAsync(SalesReplyProof.Request(), token);
        await proof.IntakeAsync(await proof.ReplyAsync(started.OrderId, token), token);
        await using var barrier = await WorkflowBarrier.HoldAsync(
            proof.Workflow.SalesConnection,
            insert: false,
            token,
            table: "sales.inbox_messages",
            condition: "NEW.processed_at IS NOT NULL"
        );
        await using (var worker = proof.StartWorker())
        {
            await barrier.WaitForAdvisoryAsync(1, token);
            Assert.Equal(
                StockReservationOrderStatus.AwaitingFirst,
                (await proof.Reservations.ReadAsync(started.OrderId, token))!.Status
            );
            Assert.Equal(
                1,
                await proof.CountAsync("SELECT count(*) FROM sales.outbox_messages", token)
            );
            if (hardDeath)
                await worker.KillAsync(token);
            else
                await worker.StopAsync(token);
        }

        Assert.Equal(1, (await proof.Reservations.ReadAsync(started.OrderId, token))!.Version);
        Assert.Equal(
            1,
            await proof.CountAsync("SELECT count(*) FROM sales.outbox_messages", token)
        );
        Assert.Equal(
            1,
            await proof.CountAsync(
                "SELECT count(*) FROM sales.inbox_messages WHERE processed_at IS NULL",
                token
            )
        );
        await barrier.ReleaseAsync(token);
        await using var recovered = proof.StartWorker();
        await WorkflowProof.EventuallyAsync(
            async () =>
                (await proof.Reservations.ReadAsync(started.OrderId, token))!.Status
                == StockReservationOrderStatus.AwaitingSecond,
            token
        );
        await recovered.StopAsync(token);
        Assert.Equal(
            2,
            await proof.CountAsync("SELECT count(*) FROM sales.outbox_messages", token)
        );
        Assert.Equal(
            1,
            await proof.CountAsync(
                "SELECT count(*) FROM sales.inbox_messages WHERE processed_at IS NOT NULL",
                token
            )
        );
    }
}
