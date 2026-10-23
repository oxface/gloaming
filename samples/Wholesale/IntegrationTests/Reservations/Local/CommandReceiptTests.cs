using System.Data.Common;
using System.Text.Json;
using Gloaming.Messaging;
using Gloaming.Messaging.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Inventory.Contracts;
using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;
using ModulithFoundry.Samples.Wholesale.Sales.ReservationOrders;
using ModulithFoundry.Tests.Infrastructure;

namespace ModulithFoundry.Samples.Wholesale.IntegrationTests.Reservations.Local;

public sealed class CommandReceiptTests(PostgreSqlFixture postgres)
    : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task EqualConcurrentRetriesAndAnOldRetryAfterAnotherPauseReturnTheOriginalResult()
    {
        await using var proof = await LocalWorkflowProof.CreateAsync(postgres, Token);
        var order = await PauseAsync(proof);
        Guid operationId = Guid.NewGuid();
        var attempts = await Task.WhenAll(
            RetryAsync(proof, order.OrderId, operationId),
            RetryAsync(proof, order.OrderId, operationId)
        );
        Assert.Equal(attempts[0], attempts[1]);
        Assert.Equal(5, attempts[0].Version);
        Assert.Equal(StockReservationOrderStatus.Releasing, attempts[0].Status);
        await AssertCountsAsync(proof, receipts: 1, requests: 4);

        await RefuseReleaseAsync(proof, order.OrderId, order.FirstReservationId);
        var current = await proof.ReadAsync(order.OrderId, Token);
        Assert.Equal(6, current.Version);
        Assert.Equal(StockReservationOrderStatus.NeedsAttention, current.Status);
        Assert.Equal(attempts[0], await RetryAsync(proof, order.OrderId, operationId));
        Assert.Equal(current, await proof.ReadAsync(order.OrderId, Token));
        await AssertCountsAsync(proof, receipts: 1, requests: 4);

        await Assert.ThrowsAsync<CommandReceiptConflictException>(() =>
            RetryAsync(proof, Guid.NewGuid(), operationId)
        );
        var newDecision = await RetryAsync(proof, order.OrderId, Guid.NewGuid());
        Assert.Equal(7, newDecision.Version);
        await AssertCountsAsync(proof, receipts: 2, requests: 5);
    }

    [Fact]
    public async Task FailedCommitRollsBackTheSignalReceiptAndOutgoingRequestTogether()
    {
        var failure = new RejectCommit();
        await using var proof = await LocalWorkflowProof.CreateAsync(
            postgres,
            Token,
            salesInterceptor: failure
        );
        var order = await PauseAsync(proof);
        Guid operationId = Guid.NewGuid();
        failure.Enabled = true;
        await Assert.ThrowsAsync<IOException>(() => RetryAsync(proof, order.OrderId, operationId));
        failure.Enabled = false;
        Assert.Equal(4, (await proof.ReadAsync(order.OrderId, Token)).Version);
        await AssertCountsAsync(proof, receipts: 0, requests: 3);
        Assert.Equal(5, (await RetryAsync(proof, order.OrderId, operationId)).Version);
        await AssertCountsAsync(proof, receipts: 1, requests: 4);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("\"UnknownStatus\"")]
    public async Task InvalidPersistedResultStatusCannotBeReturnedAsAnAcceptedRetry(string status)
    {
        await using var proof = await LocalWorkflowProof.CreateAsync(postgres, Token);
        var order = await PauseAsync(proof);
        Guid operationId = Guid.NewGuid();
        await RetryAsync(proof, order.OrderId, operationId);
        // Deliberately corrupt a retained field as a migration owner; runtime writers use typed values.
        await LocalWorkflowProof.SqlAsync(
            proof.Sales,
            "UPDATE sales.command_receipts SET result = jsonb_set(result::jsonb, '{Status}', '"
                + status
                + "'::jsonb)::text",
            Token
        );
        await Assert.ThrowsAsync<JsonException>(() =>
            RetryAsync(proof, order.OrderId, operationId)
        );
        Assert.Equal(5, (await proof.ReadAsync(order.OrderId, Token)).Version);
        await AssertCountsAsync(proof, receipts: 1, requests: 4);
    }

    private static async Task<StockReservationOrder> PauseAsync(LocalWorkflowProof proof)
    {
        var request = LocalWorkflowProof.Request(Guid.NewGuid(), Guid.NewGuid());
        var order = await proof.StartAsync(request, Token);
        // Deliberately admitted business outcomes, rather than a second Inventory/broker proof.
        await ReplyAsync(
            proof,
            order.OrderId,
            "inventory.reservation-decided",
            new StockReservationDecidedV1(
                order.FirstReservationId,
                request.First.StockPositionId,
                request.First.ExpectedVersion,
                request.First.Quantity,
                StockReservationDecision.Reserved
            )
        );
        await ReplyAsync(
            proof,
            order.OrderId,
            "inventory.reservation-decided",
            new StockReservationDecidedV1(
                order.SecondReservationId,
                request.Second.StockPositionId,
                request.Second.ExpectedVersion,
                request.Second.Quantity,
                StockReservationDecision.InsufficientStock
            )
        );
        await RefuseReleaseAsync(proof, order.OrderId, order.FirstReservationId);
        return await proof.ReadAsync(order.OrderId, Token);
    }

    private static Task RefuseReleaseAsync(
        LocalWorkflowProof proof,
        Guid order,
        Guid reservation
    ) =>
        ReplyAsync(
            proof,
            order,
            "inventory.reservation-released",
            new StockReservationReleasedV1(
                reservation,
                StockReservationReleaseDecision.ReservationNotFound
            )
        );

    private static async Task ReplyAsync<T>(
        LocalWorkflowProof proof,
        Guid order,
        string name,
        T payload
    )
    {
        await using var scope = proof.Scope();
        var database = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        var runner = scope.ServiceProvider.GetRequiredService<
            IWorkflow<StartStockReservationOrder, ReservationState>
        >();
        var key = new WorkflowKey(order, LocalWorkflowProof.Alpha);
        var current = (await runner.ReadAsync(key, Token))!;
        await runner.HandleReplyAsync(
            key,
            new IncomingMessage(
                Guid.NewGuid(),
                "inventory",
                name,
                1,
                JsonSerializer.SerializeToElement(payload, JsonSerializerOptions.Web),
                LocalWorkflowProof.Alpha,
                order.ToString("D"),
                current.PendingCommandId!.Value.ToString("D")
            ),
            Token
        );
        await database.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
    }

    private static async Task<StockReservationOrder> RetryAsync(
        LocalWorkflowProof proof,
        Guid order,
        Guid operationId
    )
    {
        await using var scope = proof.Scope(actor: true);
        return await scope
            .ServiceProvider.GetRequiredService<IStockReservationOrders>()
            .RetryCompensationAsync(order, operationId, Token);
    }

    private static async Task AssertCountsAsync(
        LocalWorkflowProof proof,
        int receipts,
        int requests
    )
    {
        Assert.Equal(
            receipts,
            await LocalWorkflowProof.CountAsync(
                proof.Sales,
                "SELECT count(*) FROM sales.command_receipts",
                Token
            )
        );
        Assert.Equal(
            requests,
            await LocalWorkflowProof.CountAsync(
                proof.Sales,
                "SELECT count(*) FROM sales.outbox_messages",
                Token
            )
        );
    }

    private sealed class RejectCommit : DbTransactionInterceptor
    {
        internal bool Enabled { get; set; }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        ) =>
            Enabled
                ? throw new IOException("The signal attempt cannot commit.")
                : ValueTask.FromResult(result);
    }
}
