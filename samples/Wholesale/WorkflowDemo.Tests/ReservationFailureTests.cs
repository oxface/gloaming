using Gloaming.Messaging.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Inventory;
using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;
using ModulithFoundry.Tests.Infrastructure;
using Npgsql;

namespace ModulithFoundry.Samples.Wholesale.WorkflowDemo.Tests;

public sealed class ReservationFailureTests(PostgreSqlFixture postgres, RabbitMqFixture rabbit)
    : IClassFixture<PostgreSqlFixture>,
        IClassFixture<RabbitMqFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false, "receipt")]
    [InlineData(false, "event")]
    [InlineData(false, "outcome")]
    [InlineData(false, "completion")]
    [InlineData(true, "receipt")]
    [InlineData(true, "event")]
    [InlineData(true, "outcome")]
    [InlineData(true, "completion")]
    public async Task InventoryFailureRollsBackReservationEventOutcomeAndCompletionThenFreshRetryRecovers(
        bool release,
        string fault
    )
    {
        await using var proof = new ReservationProof(
            await WorkflowProof.CreateAsync(postgres, rabbit, Token)
        );
        Guid first = await proof.Workflow.SeedAsync(Token);
        Guid second = await proof.Workflow.SeedAsync(Token);
        var order = await proof.StartAsync(ReservationTests.Request(first, second, 6), Token);
        if (release)
        {
            await proof.StepAsync(Token);
            await proof.StepAsync(Token);
        }

        Assert.Equal(
            OutboxDispatchResult.Published,
            await proof.Workflow.DispatchAsync<SalesDbContext>(Token)
        );
        var scopes = proof.Workflow.Services.GetRequiredService<IServiceScopeFactory>();
        Assert.True(
            release
                ? await proof.Workflow.Broker.ReceiveReleaseAsync(scopes, Token)
                : await proof.Workflow.Broker.ReceiveReservationAsync(scopes, Token)
        );
        string table = fault switch
        {
            "receipt" => "stock_reservations",
            "event" => "events",
            "outcome" => "outbox_messages",
            _ => "inbox_messages",
        };
        string predicate = fault switch
        {
            "receipt" => release ? "NOT \"Released\"" : "\"Quantity\" < 0",
            "completion" => "processed_at IS NULL",
            _ => "false",
        };
        await WorkflowProof.SqlAsync(
            proof.Workflow.InventoryConnection,
            $"ALTER TABLE inventory.{table} ADD CONSTRAINT wf2_failure CHECK ({predicate}) NOT VALID",
            Token
        );
        if (fault == "completion")
        {
            var failure = await Assert.ThrowsAsync<PostgresException>(() =>
                proof.Workflow.ProcessAsync<InventoryDbContext>(
                    StockReservationAdmission.Subscription,
                    Token
                )
            );
            Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState);
        }
        else
            await Assert.ThrowsAnyAsync<DbUpdateException>(() =>
                proof.Workflow.ProcessAsync<InventoryDbContext>(
                    StockReservationAdmission.Subscription,
                    Token
                )
            );
        var state = await proof.StockAsync(first, Token);
        Assert.Equal(release ? 3 : 2, state.Version);
        Assert.Equal(release ? 2 : 0, state.Reserved);
        Assert.Equal(
            release ? 2 : 0,
            await WorkflowProof.CountAsync(
                proof.Workflow.InventoryConnection,
                "SELECT count(*) FROM inventory.stock_reservations",
                Token
            )
        );
        Assert.Equal(
            release ? 2 : 0,
            await WorkflowProof.CountAsync(
                proof.Workflow.InventoryConnection,
                "SELECT count(*) FROM inventory.outbox_messages",
                Token
            )
        );
        Assert.Equal(
            1,
            await WorkflowProof.CountAsync(
                proof.Workflow.InventoryConnection,
                "SELECT count(*) FROM inventory.inbox_messages WHERE processed_at IS NULL",
                Token
            )
        );
        Assert.Equal(
            release
                ? StockReservationOrderStatus.Releasing
                : StockReservationOrderStatus.AwaitingFirst,
            (await proof.ReadAsync(order.OrderId, Token))!.Status
        );
        await WorkflowProof.SqlAsync(
            proof.Workflow.InventoryConnection,
            $"ALTER TABLE inventory.{table} DROP CONSTRAINT wf2_failure",
            Token
        );
        await RetryAsync<InventoryDbContext>(proof, StockReservationAdmission.Subscription, Token);
        await proof.ReplyAsync(Token);
        Assert.Equal(
            release
                ? StockReservationOrderStatus.Compensated
                : StockReservationOrderStatus.AwaitingSecond,
            (await proof.ReadAsync(order.OrderId, Token))!.Status
        );
        Assert.Equal(release ? 0 : 2, (await proof.StockAsync(first, Token)).Reserved);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SalesFailureRollsBackTransitionNextCommandAndCompletionThenFreshRetryRecovers(
        bool compensation
    )
    {
        await using var proof = new ReservationProof(
            await WorkflowProof.CreateAsync(postgres, rabbit, Token)
        );
        Guid first = await proof.Workflow.SeedAsync(Token);
        Guid second = await proof.Workflow.SeedAsync(Token);
        var order = await proof.StartAsync(ReservationTests.Request(first, second, 6), Token);
        if (compensation)
            await proof.StepAsync(Token);

        await proof.SendCommandAsync(Token);
        Assert.Equal(
            OutboxDispatchResult.Published,
            await proof.Workflow.DispatchAsync<InventoryDbContext>(Token)
        );
        Assert.True(
            await proof.Workflow.Broker.ReceiveReservationReplyAsync(
                proof.Workflow.Services.GetRequiredService<IServiceScopeFactory>(),
                Token
            )
        );
        await WorkflowProof.SqlAsync(
            proof.Workflow.SalesConnection,
            "ALTER TABLE sales.outbox_messages ADD CONSTRAINT wf2_failure CHECK (false) NOT VALID",
            Token
        );
        await Assert.ThrowsAnyAsync<DbUpdateException>(() =>
            proof.Workflow.ProcessAsync<SalesDbContext>(
                StockReservationReplyAdmission.Subscription,
                Token
            )
        );
        var unchanged = await proof.ReadAsync(order.OrderId, Token);
        Assert.Equal(
            compensation
                ? StockReservationOrderStatus.AwaitingSecond
                : StockReservationOrderStatus.AwaitingFirst,
            unchanged!.Status
        );
        Assert.Equal(compensation ? 2 : 1, unchanged.Version);
        Assert.Equal(
            compensation ? 2 : 1,
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
        Assert.Equal(
            1,
            await WorkflowProof.CountAsync(
                proof.Workflow.SalesConnection,
                "SELECT count(*) FROM sales.inbox_messages WHERE processed_at IS NULL",
                Token
            )
        );
        await WorkflowProof.SqlAsync(
            proof.Workflow.SalesConnection,
            "ALTER TABLE sales.outbox_messages DROP CONSTRAINT wf2_failure",
            Token
        );
        await RetryAsync<SalesDbContext>(proof, StockReservationReplyAdmission.Subscription, Token);
        Assert.Equal(
            compensation
                ? StockReservationOrderStatus.Releasing
                : StockReservationOrderStatus.AwaitingSecond,
            (await proof.ReadAsync(order.OrderId, Token))!.Status
        );
        Assert.Equal(
            compensation ? 1 : 0,
            await WorkflowProof.CountAsync(
                proof.Workflow.SalesConnection,
                "SELECT count(*) FROM sales.workflow_instances, LATERAL jsonb_array_elements(\"Operations\") attempt WHERE attempt->>'requestName' = 'inventory.release-reservation'",
                Token
            )
        );
    }

    internal static async Task RetryAsync<TDatabase>(
        ReservationProof proof,
        string subscription,
        CancellationToken token
    )
        where TDatabase : DbContext
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(token);
        bounded.CancelAfter(TimeSpan.FromSeconds(20));
        await WorkflowProof.EventuallyAsync(
            async () =>
                await proof.Workflow.ProcessAsync<TDatabase>(subscription, bounded.Token)
                == InboxProcessingResult.Processed,
            bounded.Token
        );
    }
}
