using Gloaming.Messaging.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ModulithFoundry.Samples.MessagingWorkerDemo.Tests;
using ModulithFoundry.Samples.Wholesale.Inventory;
using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;
using ModulithFoundry.Samples.Wholesale.Worker.Broker;
using ModulithFoundry.Tests.Infrastructure;

namespace ModulithFoundry.Samples.Wholesale.IntegrationTests.Reservations.Broker;

public sealed class WorkflowRecoveryTests(PostgreSqlFixture postgres, RabbitMqFixture rabbit)
    : IClassFixture<PostgreSqlFixture>,
        IClassFixture<RabbitMqFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static StartStockIssueRequest Request(Guid stock, bool expired = false) =>
        new(Guid.NewGuid(), stock, 2, 1, DateTimeOffset.UtcNow.AddMinutes(expired ? -5 : 5));

    [Fact]
    public async Task FiniteSetupSeedAndStartCommandsComposeWithTheWorkerHost()
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(Token);
        bounded.CancelAfter(TimeSpan.FromSeconds(60));
        var token = bounded.Token;
        await using var proof = await WorkflowProof.CreateAsync(postgres, rabbit, token);
        foreach (string role in new[] { "setup", "seed" })
        {
            await using var operation = new ChildHost(
                typeof(WorkflowComposition).Assembly,
                proof.Environment,
                "--role",
                role,
                "--StockId",
                "8c6e282e-d9ec-455a-8fab-d114c176a7cd"
            );
            Assert.True(await operation.WaitForExitAsync(token) == 0, operation.Output);
        }

        Guid request = Guid.NewGuid();
        await using (
            var start = new ChildHost(
                typeof(WorkflowComposition).Assembly,
                proof.Environment,
                "--role",
                "start",
                "--RequestId",
                request.ToString("D"),
                "--StockId",
                "8c6e282e-d9ec-455a-8fab-d114c176a7cd",
                "--ReplyDeadline",
                "2030-01-01T00:00:00Z"
            )
        )
        {
            Assert.True(await start.WaitForExitAsync(token) == 0, start.Output);
        }

        Assert.Equal(
            StockIssueRequestStatus.AwaitingReply,
            (await proof.ReadAsync(request, token))!.Status
        );
        await using var worker = new ChildHost(
            typeof(WorkflowComposition).Assembly,
            proof.Environment,
            "--role",
            "run"
        );
        await WorkflowProof.EventuallyAsync(
            async () =>
                (await proof.ReadAsync(request, token))!.Status == StockIssueRequestStatus.Issued,
            token
        );
        await worker.StopAsync(token);
        await using var read = new ChildHost(
            typeof(WorkflowComposition).Assembly,
            proof.Environment,
            "--role",
            "read",
            "--RequestId",
            request.ToString("D")
        );
        Assert.True(await read.WaitForExitAsync(token) == 0, read.Output);
        Assert.Contains(request.ToString("D"), read.Output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CompetingStartsCommitOneCommandAndFreshRetryReturnsTheWinner()
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(Token);
        bounded.CancelAfter(TimeSpan.FromSeconds(30));
        var token = bounded.Token;
        await using var proof = await WorkflowProof.CreateAsync(postgres, rabbit, token);
        var input = Request(Guid.NewGuid());
        await using var barrier = await WorkflowBarrier.HoldAsync(
            proof.SalesConnection,
            insert: true,
            token
        );
        Task<Exception?> first = CaptureAsync(async () => _ = await proof.StartAsync(input, token));
        Task<Exception?> second = CaptureAsync(async () =>
            _ = await proof.StartAsync(input, token)
        );
        await barrier.WaitForAdvisoryAsync(2, token);
        await barrier.ReleaseAsync(token);
        var attempts = await Task.WhenAll(first, second);
        Assert.Single(attempts, failure => failure is null);
        Assert.IsType<DbUpdateException>(Assert.Single(attempts, failure => failure is not null));
        var winner = await proof.ReadAsync(input.RequestId, token);
        Assert.Equal(winner, await proof.StartAsync(input, token));
        Assert.Equal(
            1,
            await WorkflowProof.CountAsync(
                proof.SalesConnection,
                "SELECT count(*) FROM sales.outbox_messages",
                token
            )
        );
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task CompetingDeadlineAndReplyRetryWithoutOverwritingActualOutcome(
        bool deadlineFirst
    )
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(Token);
        bounded.CancelAfter(TimeSpan.FromSeconds(30));
        var token = bounded.Token;
        await using var proof = await WorkflowProof.CreateAsync(postgres, rabbit, token);
        var started = await proof.StartAsync(Request(Guid.NewGuid(), expired: true), token);
        await proof.IntakeAsync(WorkflowProof.Recorded(started), token);
        await using var barrier = await WorkflowBarrier.HoldAsync(
            proof.SalesConnection,
            insert: false,
            token
        );
        Task<Exception?> first = deadlineFirst
            ? CaptureAsync(async () => _ = await proof.ExpireAsync(token))
            : CaptureAsync(async () =>
                _ = await proof.ProcessAsync<SalesDbContext>(
                    StockIssueReplyAdmission.Subscription,
                    token
                )
            );
        await barrier.WaitForAdvisoryAsync(1, token);
        Task<Exception?> second = deadlineFirst
            ? CaptureAsync(async () =>
                _ = await proof.ProcessAsync<SalesDbContext>(
                    StockIssueReplyAdmission.Subscription,
                    token
                )
            )
            : CaptureAsync(async () => _ = await proof.ExpireAsync(token));
        await barrier.WaitForRowWriterAsync(token);
        await barrier.ReleaseAsync(token);
        Assert.Null(await first);
        Assert.IsType<DbUpdateConcurrencyException>(await second);
        if (deadlineFirst)
        {
            Assert.Equal(
                StockIssueRequestStatus.NeedsAttention,
                (await proof.ReadAsync(started.RequestId, token))!.Status
            );
            await WorkflowProof.SqlAsync(
                proof.SalesConnection,
                "UPDATE sales.inbox_messages SET available_at = clock_timestamp()",
                token
            );
            Assert.Equal(
                InboxProcessingResult.Processed,
                await proof.ProcessAsync<SalesDbContext>(
                    StockIssueReplyAdmission.Subscription,
                    token
                )
            );
        }

        Assert.Equal(0, await proof.ExpireAsync(token));
        Assert.Equal(
            StockIssueRequestStatus.Issued,
            (await proof.ReadAsync(started.RequestId, token))!.Status
        );
        Assert.Equal(
            1,
            await WorkflowProof.CountAsync(
                proof.SalesConnection,
                "SELECT count(*) FROM sales.inbox_messages WHERE processed_at IS NOT NULL",
                token
            )
        );
    }

    [Fact]
    public async Task CompetingSemanticRepliesLockDifferentInboxRowsButResolveProgressOnce()
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(Token);
        bounded.CancelAfter(TimeSpan.FromSeconds(30));
        var token = bounded.Token;
        await using var proof = await WorkflowProof.CreateAsync(postgres, rabbit, token);
        var started = await proof.StartAsync(Request(Guid.NewGuid()), token);
        await proof.IntakeAsync(WorkflowProof.Recorded(started), token);
        await proof.IntakeAsync(WorkflowProof.Recorded(started), token);
        await using var barrier = await WorkflowBarrier.HoldAsync(
            proof.SalesConnection,
            insert: false,
            token
        );
        Task<Exception?> first = CaptureAsync(async () =>
            _ = await proof.ProcessAsync<SalesDbContext>(
                StockIssueReplyAdmission.Subscription,
                token
            )
        );
        await barrier.WaitForAdvisoryAsync(1, token);
        Task<Exception?> second = CaptureAsync(async () =>
            _ = await proof.ProcessAsync<SalesDbContext>(
                StockIssueReplyAdmission.Subscription,
                token
            )
        );
        await barrier.WaitForRowWriterAsync(token);
        await barrier.ReleaseAsync(token);
        Assert.Null(await first);
        Assert.IsType<DbUpdateConcurrencyException>(await second);
        await WorkflowProof.SqlAsync(
            proof.SalesConnection,
            "UPDATE sales.inbox_messages SET available_at = clock_timestamp()",
            token
        );
        Assert.Equal(
            InboxProcessingResult.Processed,
            await proof.ProcessAsync<SalesDbContext>(StockIssueReplyAdmission.Subscription, token)
        );
        Assert.Equal(2, (await proof.ReadAsync(started.RequestId, token))!.Version);
        Assert.Equal(
            2,
            await WorkflowProof.CountAsync(
                proof.SalesConnection,
                "SELECT count(*) FROM sales.inbox_messages WHERE processed_at IS NOT NULL",
                token
            )
        );
    }

    [Fact]
    public async Task KilledHostAfterInventoryCommitRecoversRetainedReplyWithoutAnotherStockIssue()
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(Token);
        bounded.CancelAfter(TimeSpan.FromSeconds(60));
        var token = bounded.Token;
        await using var proof = await WorkflowProof.CreateAsync(postgres, rabbit, token);
        Guid stock = await proof.SeedAsync(token);
        var started = await proof.StartAsync(Request(stock), token);
        await using var barrier = await WorkflowBarrier.HoldAsync(
            proof.SalesConnection,
            insert: false,
            token
        );
        await using (
            var worker = new ChildHost(
                typeof(WorkflowComposition).Assembly,
                proof.Environment,
                "--role",
                "run"
            )
        )
        {
            await barrier.WaitForAdvisoryAsync(1, token);
            Assert.Equal(
                3,
                await WorkflowProof.CountAsync(
                    proof.InventoryConnection,
                    "SELECT version FROM inventory.event_streams",
                    token
                )
            );
            await worker.KillAsync(token);
        }

        Assert.Equal(
            StockIssueRequestStatus.AwaitingReply,
            (await proof.ReadAsync(started.RequestId, token))!.Status
        );
        Assert.Equal(
            0,
            await WorkflowProof.CountAsync(
                proof.SalesConnection,
                "SELECT count(*) FROM sales.inbox_messages WHERE processed_at IS NOT NULL",
                token
            )
        );
        await barrier.ReleaseAsync(token);
        await using var recovered = new ChildHost(
            typeof(WorkflowComposition).Assembly,
            proof.Environment,
            "--role",
            "run"
        );
        await WorkflowProof.EventuallyAsync(
            async () =>
                (await proof.ReadAsync(started.RequestId, token))!.Status
                == StockIssueRequestStatus.Issued,
            token
        );
        await recovered.StopAsync(token);
        Assert.Equal(
            3,
            await WorkflowProof.CountAsync(
                proof.InventoryConnection,
                "SELECT version FROM inventory.event_streams",
                token
            )
        );
        Assert.Equal(
            3,
            await WorkflowProof.CountAsync(
                proof.InventoryConnection,
                "SELECT count(*) FROM inventory.events",
                token
            )
        );
        Assert.Equal(
            1,
            await WorkflowProof.CountAsync(
                proof.SalesConnection,
                "SELECT count(*) FROM sales.outbox_messages",
                token
            )
        );
    }

    [Fact]
    public async Task RestartedHostFindsPersistedExpiredDeadlineAndLateRefusalStillResolves()
    {
        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(Token);
        bounded.CancelAfter(TimeSpan.FromSeconds(60));
        var token = bounded.Token;
        await using var proof = await WorkflowProof.CreateAsync(postgres, rabbit, token);
        var started = await proof.StartAsync(Request(Guid.NewGuid(), expired: true), token);
        // Hold Inventory's command inbox so the first process can only flag the overdue state.
        await using var owner = new Npgsql.NpgsqlConnection(proof.InventoryConnection);
        await owner.OpenAsync(token);
        await using var blocking = await owner.BeginTransactionAsync(token);
        await using (
            var command = new Npgsql.NpgsqlCommand(
                "LOCK TABLE inventory.inbox_messages IN SHARE ROW EXCLUSIVE MODE",
                owner,
                blocking
            )
        )
            await command.ExecuteNonQueryAsync(token);

        await using var barrier = await WorkflowBarrier.HoldAsync(
            proof.SalesConnection,
            insert: false,
            token
        );
        await using (
            var worker = new ChildHost(
                typeof(WorkflowComposition).Assembly,
                proof.Environment,
                "--role",
                "run"
            )
        )
        {
            await barrier.WaitForAdvisoryAsync(1, token);
            await worker.KillAsync(token);
        }

        Assert.Equal(
            StockIssueRequestStatus.AwaitingReply,
            (await proof.ReadAsync(started.RequestId, token))!.Status
        );
        await barrier.ReleaseAsync(token);
        await using var recovered = new ChildHost(
            typeof(WorkflowComposition).Assembly,
            proof.Environment,
            "--role",
            "run"
        );
        await WorkflowProof.EventuallyAsync(
            async () =>
                (await proof.ReadAsync(started.RequestId, token))!.Status
                == StockIssueRequestStatus.NeedsAttention,
            token
        );
        await blocking.RollbackAsync(token);
        await WorkflowProof.EventuallyAsync(
            async () =>
                (await proof.ReadAsync(started.RequestId, token))!.Status
                == StockIssueRequestStatus.Declined,
            token
        );
        await recovered.StopAsync(token);
        Assert.Equal(
            StockIssueRefusal.NotFound,
            (await proof.ReadAsync(started.RequestId, token))!.Refusal
        );
        Assert.Equal(
            1,
            await WorkflowProof.CountAsync(
                proof.SalesConnection,
                "SELECT count(*) FROM sales.outbox_messages",
                token
            )
        );
    }

    private static async Task<Exception?> CaptureAsync(Func<Task> operation)
    {
        try
        {
            await operation();
            return null;
        }
        catch (Exception failure)
        {
            return failure;
        }
    }
}
