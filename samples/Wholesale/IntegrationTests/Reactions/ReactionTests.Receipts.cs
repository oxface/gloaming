using System.Data;
using System.Data.Common;
using System.Text.Json;
using Gloaming.Samples.Wholesale.IntegrationTests.Reservations.Local;
using Gloaming.Samples.Wholesale.IntegrationTests.Scenarios.DomainEvents;
using Gloaming.Samples.Wholesale.Inventory;
using Gloaming.Samples.Wholesale.Sales;
using Gloaming.Samples.Wholesale.Sales.Contracts;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;

namespace Gloaming.Samples.Wholesale.IntegrationTests.Reactions;

public sealed partial class ReactionTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConcurrentReceiptAttemptsWaitForTheOwnerThenReplayOrRejectChangedInput(
        bool differentInput
    )
    {
        string connection = await SetupAsync();
        await using var provider = Provider(connection);
        Guid operationId = Guid.NewGuid();
        await using var first = DomainEventComposition.CreateScope(provider);
        await using var transaction = await Db(first).Database.BeginTransactionAsync(Token);
        var accepted = Assert.IsType<DraftOrderChangeResult.Changed>(
            await Commands(first).SubmitAsync(new(First, 1, operationId), Token)
        );
        var competing = SubmitReceiptedAsync(
            provider,
            new(First, differentInput ? 2 : 1, operationId)
        );
        await LocalWorkflowProof
            .EventuallyAsync(
                async () =>
                    await LocalWorkflowProof.CountAsync(
                        connection,
                        "SELECT count(*) FROM pg_locks WHERE locktype = 'advisory' AND NOT granted AND database = (SELECT oid FROM pg_database WHERE datname = current_database())",
                        Token
                    ) == 1,
                Token
            )
            .WaitAsync(TimeSpan.FromSeconds(15), Token);
        Assert.False(competing.IsCompleted);
        await Db(first).SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
        if (differentInput)
            await Assert.ThrowsAsync<CommandReceiptConflictException>(() => competing);
        else
            Assert.Equal(
                JsonSerializer.Serialize(accepted.Proposed),
                JsonSerializer.Serialize((await competing).Proposed)
            );

        await AssertStateAsync(provider, First, true, 40, 2);
        Assert.Equal(1, await ReceiptCountAsync(connection));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DiscardingAnAttemptBeforeOrAfterSaveLeavesNoReceiptOrBusinessEffect(bool save)
    {
        string connection = await SetupAsync();
        await using var provider = Provider(connection);
        var request = new SubmitDraftOrder(First, 1, Guid.NewGuid());
        await using (var attempt = DomainEventComposition.CreateScope(provider))
        {
            await using var transaction = await Db(attempt).Database.BeginTransactionAsync(Token);
            Assert.IsType<DraftOrderChangeResult.Changed>(
                await Commands(attempt).SubmitAsync(request, Token)
            );
            if (save)
                await Db(attempt).SaveChangesAsync(Token);

            Assert.Equal(0, await ReceiptCountAsync(connection));
            await transaction.RollbackAsync(Token);
        }

        await AssertStateAsync(provider, First, false, 0, 1);
        Assert.Equal(0, await ReceiptCountAsync(connection));
        Assert.True((await SubmitReceiptedAsync(provider, request)).Proposed.IsSubmitted);
        await AssertStateAsync(provider, First, true, 40, 2);
    }

    [Fact]
    public async Task CancelledLockWaiterCanBeDiscardedWithoutCancellingTheOwningAttempt()
    {
        string connection = await SetupAsync();
        await using var provider = Provider(connection);
        var request = new SubmitDraftOrder(First, 1, Guid.NewGuid());
        await using var owner = DomainEventComposition.CreateScope(provider);
        await using var transaction = await Db(owner).Database.BeginTransactionAsync(Token);
        Assert.IsType<DraftOrderChangeResult.Changed>(
            await Commands(owner).SubmitAsync(request, Token)
        );
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var waiting = WaitAsync();
        await LocalWorkflowProof
            .EventuallyAsync(
                async () =>
                    await LocalWorkflowProof.CountAsync(
                        connection,
                        "SELECT count(*) FROM pg_locks WHERE locktype = 'advisory' AND NOT granted AND database = (SELECT oid FROM pg_database WHERE datname = current_database())",
                        Token
                    ) == 1,
                Token
            )
            .WaitAsync(TimeSpan.FromSeconds(15), Token);
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        await Db(owner).SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
        Assert.Equal(2, (await SubmitReceiptedAsync(provider, request)).Proposed.Version);
        Assert.Equal(1, await ReceiptCountAsync(connection));
        await AssertStateAsync(provider, First, true, 40, 2);

        async Task WaitAsync()
        {
            await using var scope = DomainEventComposition.CreateScope(provider);
            await using var attempt = await Db(scope)
                .Database.BeginTransactionAsync(cancelled.Token);
            await Commands(scope).SubmitAsync(request, cancelled.Token);
        }
    }

    [Fact]
    public async Task FailedRequiredReactionDoesNotRetainAnAcceptedCommandResult()
    {
        string connection = await SetupAsync(ceiling: 20);
        await using var provider = Provider(connection);
        var request = new SubmitDraftOrder(First, 1, Guid.NewGuid());
        await Assert.ThrowsAsync<OrderAllowanceExceededException>(() =>
            SubmitReceiptedAsync(provider, request)
        );
        Assert.Equal(0, await ReceiptCountAsync(connection));
        await AssertStateAsync(provider, First, false, 0, 1);
        await LocalWorkflowProof.SqlAsync(
            connection,
            "UPDATE sales.customer_order_allowances SET ceiling = 100",
            Token
        );
        await SubmitReceiptedAsync(provider, request);
        await AssertStateAsync(provider, First, true, 40, 2);
    }

    [Fact]
    public async Task LostCommitResponseRecoversOriginalResultInAFreshScope()
    {
        string connection = await SetupAsync();
        var request = new SubmitDraftOrder(First, 1, Guid.NewGuid());
        await using (var uncertain = Provider(connection, new LoseCommitResponse()))
            await Assert.ThrowsAsync<IOException>(() => SubmitReceiptedAsync(uncertain, request));

        await using var provider = Provider(connection);
        var repeated = await SubmitReceiptedAsync(provider, request);
        Assert.Equal(2, repeated.Proposed.Version);
        Assert.True(repeated.Proposed.IsSubmitted);
        Assert.Equal(1, await ReceiptCountAsync(connection));
        await AssertStateAsync(provider, First, true, 40, 2);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AbruptProcessDeathReleasesTheLockAndRecoversTheCommittedOrRolledBackAttempt(
        bool committed
    )
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        string connection = await SetupAsync();
        Guid operationId = Guid.NewGuid();
        await using (
            var child = new ChildHost(
                typeof(ReactionTests).Assembly,
                new()
                {
                    ["WHOLESALE_SCENARIO_CONNECTION"] = connection,
                    ["WHOLESALE_RECEIPT_OPERATION_ID"] = operationId.ToString("D"),
                },
                "--scenario",
                committed ? "receipt-after-commit" : "receipt-before-commit"
            )
        )
        {
            await child.WaitForOutputAsync("receipt-cut-ready", deadline.Token);
            Assert.Equal(committed ? 1 : 0, await ReceiptCountAsync(connection));
            await child.KillAsync(deadline.Token);
        }

        await using var provider = Provider(connection);
        await AssertStateAsync(provider, First, committed, committed ? 40 : 0, committed ? 2 : 1);
        var recovered = await SubmitReceiptedAsync(provider, new(First, 1, operationId))
            .WaitAsync(TimeSpan.FromSeconds(15), deadline.Token);
        Assert.Equal(2, recovered.Proposed.Version);
        Assert.Equal(1, await ReceiptCountAsync(connection));
        await AssertStateAsync(provider, First, true, 40, 2);
    }

    [Fact]
    public async Task ReplayingAReceiptStillRequiresCurrentResourcePermission()
    {
        string connection = await SetupAsync();
        await using var provider = Provider(connection);
        var request = new SubmitDraftOrder(First, 1, Guid.NewGuid());
        await SubmitReceiptedAsync(provider, request);
        await LocalWorkflowProof.SqlAsync(
            connection,
            "DELETE FROM sales.draft_order_grants",
            Token
        );
        await using var denied = DomainEventComposition.CreateScope(provider);
        await using var transaction = await Db(denied).Database.BeginTransactionAsync(Token);
        Assert.IsType<DraftOrderChangeResult.NotFound>(
            await Commands(denied).SubmitAsync(request, Token)
        );
        Assert.Equal(1, await ReceiptCountAsync(connection));
    }

    [Fact]
    public async Task TheSameOperationIdentityCanBelongToIndependentOrganizations()
    {
        string connection = await SetupAsync();
        await using var provider = Provider(connection);
        Guid operationId = Guid.NewGuid();
        await SubmitReceiptedAsync(provider, new(First, 1, operationId));
        await using (var beta = DomainEventComposition.CreateScope(provider, "wholesale-beta"))
        {
            SalesDemoSeed.Stage(Db(beta));
            await Db(beta).SaveChangesAsync(Token);
            var inventory = beta.ServiceProvider.GetRequiredService<InventoryDbContext>();
            InventoryDemoSeed.Stage(inventory, 42);
            await inventory.SaveChangesAsync(Token);
            await using var transaction = await Db(beta).Database.BeginTransactionAsync(Token);
            await beta
                .ServiceProvider.GetRequiredService<ICustomerOrderAllowances>()
                .OpenAsync(SalesDemoSeed.BetaCustomerId, "EUR", 100, Token);
            Assert.IsType<DraftOrderChangeResult.Changed>(
                await Commands(beta)
                    .CreateAsync(
                        DomainEventJourney.Request(First, 30) with
                        {
                            CustomerId = SalesDemoSeed.BetaCustomerId,
                        },
                        Token
                    )
            );
            await Db(beta).SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }

        await using (var beta = DomainEventComposition.CreateScope(provider, "wholesale-beta"))
        {
            await using var transaction = await Db(beta).Database.BeginTransactionAsync(Token);
            var result = Assert.IsType<DraftOrderChangeResult.Changed>(
                await Commands(beta).SubmitAsync(new(First, 1, operationId), Token)
            );
            Assert.Equal(SalesDemoSeed.BetaCustomerId, result.Proposed.CustomerId);
            Assert.Equal(30, result.Proposed.Total);
            await Db(beta).SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }

        Assert.Equal(2, await ReceiptCountAsync(connection));
        await AssertStateAsync(provider, First, true, 40, 2);
    }

    [Theory]
    [InlineData("format_version = 2", false)]
    [InlineData("result = '{}'", true)]
    public async Task IncompatibleReceiptReadersFailWithoutRepeatingBusinessWork(
        string corruption,
        bool jsonFailure
    )
    {
        string connection = await SetupAsync();
        await using var provider = Provider(connection);
        var request = new SubmitDraftOrder(First, 1, Guid.NewGuid());
        await SubmitReceiptedAsync(provider, request);
        await LocalWorkflowProof.SqlAsync(
            connection,
            "UPDATE sales.command_receipts SET " + corruption,
            Token
        );
        if (jsonFailure)
            await Assert.ThrowsAsync<JsonException>(() => SubmitReceiptedAsync(provider, request));
        else
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                SubmitReceiptedAsync(provider, request)
            );

        await AssertStateAsync(provider, First, true, 40, 2);
    }

    [Fact]
    public async Task ReceiptSelectionRejectsAnEmptyIdentityAndAnOldSnapshotTransaction()
    {
        string connection = await SetupAsync();
        await using var provider = Provider(connection);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            SubmitReceiptedAsync(provider, new(First, 1, Guid.Empty))
        );
        await using var attempt = DomainEventComposition.CreateScope(provider);
        await using var transaction = await Db(attempt)
            .Database.BeginTransactionAsync(IsolationLevel.RepeatableRead, Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Commands(attempt).SubmitAsync(new(First, 1, Guid.NewGuid()), Token)
        );
        Assert.Equal(0, await ReceiptCountAsync(connection));
    }

    [Fact]
    public async Task PopulatedPrototypeTableKeepsItsResultsThroughTheLibraryMappingMigration()
    {
        string connection = await SetupAsync();
        await using var provider = Provider(connection);
        var request = new SubmitDraftOrder(First, 1, Guid.NewGuid());
        var original = await SubmitReceiptedAsync(provider, request);
        await using (var migration = DomainEventComposition.CreateScope(provider))
        {
            var migrator = Db(migration).GetService<IMigrator>();
            await migrator.MigrateAsync("20261023125654_AddSalesCommandReceipts", Token);
            await migrator.MigrateAsync(cancellationToken: Token);
        }

        var retained = await SubmitReceiptedAsync(provider, request);
        Assert.Equal(
            JsonSerializer.Serialize(original.Proposed),
            JsonSerializer.Serialize(retained.Proposed)
        );
        Assert.Equal(1, await ReceiptCountAsync(connection));
        await AssertStateAsync(provider, First, true, 40, 2);
    }

    private static async Task<DraftOrderChangeResult.Changed> SubmitReceiptedAsync(
        ServiceProvider provider,
        SubmitDraftOrder request
    )
    {
        await using var attempt = DomainEventComposition.CreateScope(provider);
        await using var transaction = await Db(attempt).Database.BeginTransactionAsync(Token);
        var result = Assert.IsType<DraftOrderChangeResult.Changed>(
            await Commands(attempt).SubmitAsync(request, Token)
        );
        await Db(attempt).SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
        return result;
    }

    private static Task<long> ReceiptCountAsync(string connection) =>
        LocalWorkflowProof.CountAsync(
            connection,
            "SELECT count(*) FROM sales.command_receipts",
            Token
        );

    private sealed class LoseCommitResponse : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default
        ) =>
            throw new IOException(
                "The database committed, but the caller did not receive the response."
            );
    }
}
