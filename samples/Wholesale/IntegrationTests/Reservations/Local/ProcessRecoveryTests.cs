using ModulithFoundry.Samples.MessagingWorkerDemo.Tests;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;
using ModulithFoundry.Samples.Wholesale.Worker.Local;
using ModulithFoundry.Tests.Infrastructure;

namespace ModulithFoundry.Samples.Wholesale.IntegrationTests.Reservations.Local;

public sealed class ProcessRecoveryTests(PostgreSqlFixture postgres)
    : IClassFixture<PostgreSqlFixture>
{
    public enum CrashCut
    {
        BeforeInventoryIntakeCommit = 1,
        AfterInventoryEffectBeforeCommandAcknowledgment = 2,
        AfterSalesReplyIntakeBeforeReplyAcknowledgment = 3,
    }

    [Fact]
    public async Task StartupRequiresFiniteSetupAndNeverCreatesOrSeedsBusinessStorage()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        var token = deadline.Token;
        string sales = await postgres.CreateDatabaseAsync(token),
            inventory = await postgres.CreateDatabaseAsync(token);
        var environment = new Dictionary<string, string>
        {
            ["Transport"] = "local",
            ["ConnectionStrings__Sales"] = sales,
            ["ConnectionStrings__Inventory"] = inventory,
        };
        await using (
            var missing = new ChildHost(
                typeof(LocalWorkflowComposition).Assembly,
                environment,
                "--role",
                "run"
            )
        )
        {
            Assert.NotEqual(0, await missing.WaitForExitAsync(token));
            Assert.Contains("Run --role setup", missing.Output, StringComparison.Ordinal);
        }

        Assert.Equal(
            0,
            await LocalWorkflowProof.CountAsync(
                sales,
                "SELECT count(*) FROM information_schema.tables WHERE table_schema = 'sales'",
                token
            )
        );
        await using (
            var setup = new ChildHost(
                typeof(LocalWorkflowComposition).Assembly,
                environment,
                "--role",
                "setup"
            )
        )
            Assert.Equal(0, await setup.WaitForExitAsync(token));

        Assert.Equal(
            0,
            await LocalWorkflowProof.CountAsync(
                inventory,
                "SELECT count(*) FROM inventory.stock_reservations",
                token
            )
        );
        await using var ready = new ChildHost(
            typeof(LocalWorkflowComposition).Assembly,
            environment,
            "--role",
            "run"
        );
        await ready.WaitForOutputAsync("Application started.", token);
        await ready.StopAsync(token);
    }

    [Theory]
    [InlineData(CrashCut.BeforeInventoryIntakeCommit)]
    [InlineData(CrashCut.AfterInventoryEffectBeforeCommandAcknowledgment)]
    [InlineData(CrashCut.AfterSalesReplyIntakeBeforeReplyAcknowledgment)]
    public async Task KilledMonolithRecoversRetainedWorkWithoutRepeatingBusinessEffects(
        CrashCut cut
    )
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        deadline.CancelAfter(TimeSpan.FromSeconds(90));
        var token = deadline.Token;
        await using var proof = await LocalWorkflowProof.CreateAsync(postgres, token);
        Guid first = await proof.SeedAsync(token),
            second = await proof.SeedAsync(token);
        var started = await proof.StartAsync(LocalWorkflowProof.Request(first, second), token);
        await using var barrier = await LocalDeliveryBarrier.HoldAsync(proof, cut, token);
        await using var original = new ChildHost(
            typeof(LocalWorkflowComposition).Assembly,
            proof.Environment,
            "--role",
            "run"
        );
        await original.WaitForOutputAsync("Application started.", token);
        await barrier.WaitAsync(blocked: true, token);
        if (cut == CrashCut.BeforeInventoryIntakeCommit)
        {
            Assert.Equal(
                0,
                await LocalWorkflowProof.CountAsync(
                    proof.Inventory,
                    "SELECT count(*) FROM inventory.inbox_messages",
                    token
                )
            );
            Assert.Equal(0, (await proof.StockAsync(first, token)).Reserved);
        }
        else
        {
            await LocalWorkflowProof.EventuallyAsync(
                async () => (await proof.StockAsync(first, token)).Reserved == 2,
                token
            );
        }

        await original.KillAsync(token);
        await barrier.ReleaseAsync(token);
        await barrier.WaitAsync(blocked: false, token);
        if (cut == CrashCut.BeforeInventoryIntakeCommit)
            Assert.Equal(
                0,
                await LocalWorkflowProof.CountAsync(
                    proof.Inventory,
                    "SELECT count(*) FROM inventory.inbox_messages",
                    token
                )
            );

        await using var replacement = new ChildHost(
            typeof(LocalWorkflowComposition).Assembly,
            proof.Environment,
            "--role",
            "run"
        );
        await replacement.WaitForOutputAsync("Application started.", token);
        await LocalWorkflowProof.EventuallyAsync(
            async () =>
                (await proof.ReadAsync(started.OrderId, token)).Status
                == StockReservationOrderStatus.Reserved,
            token
        );
        // Wait for the real lease clock and surviving relays to acknowledge every retained delivery.
        await LocalWorkflowProof.EventuallyAsync(
            async () =>
                await LocalWorkflowProof.CountAsync(
                    proof.Sales,
                    "SELECT count(*) FROM sales.outbox_messages WHERE dispatched_at IS NULL",
                    token
                ) == 0
                && await LocalWorkflowProof.CountAsync(
                    proof.Inventory,
                    "SELECT count(*) FROM inventory.outbox_messages WHERE dispatched_at IS NULL",
                    token
                ) == 0,
            token
        );
        await replacement.StopAsync(token);

        Assert.Equal(3, (await proof.ReadAsync(started.OrderId, token)).Version);
        foreach (Guid stock in new[] { first, second })
        {
            var state = await proof.StockAsync(stock, token);
            Assert.Equal(5, state.OnHand);
            Assert.Equal(2, state.Reserved);
            Assert.Equal(3, state.Version);
        }

        Assert.Equal(
            2,
            await LocalWorkflowProof.CountAsync(
                proof.Inventory,
                "SELECT count(*) FROM inventory.stock_reservations",
                token
            )
        );
        Assert.Equal(
            2,
            await LocalWorkflowProof.CountAsync(
                proof.Inventory,
                "SELECT count(*) FROM inventory.inbox_messages",
                token
            )
        );
        Assert.Equal(
            2,
            await LocalWorkflowProof.CountAsync(
                proof.Sales,
                "SELECT count(*) FROM sales.inbox_messages",
                token
            )
        );
        bool replyCut = cut == CrashCut.AfterSalesReplyIntakeBeforeReplyAcknowledgment;
        Assert.True(
            await LocalWorkflowProof.CountAsync(
                replyCut ? proof.Inventory : proof.Sales,
                $"SELECT count(*) FROM {(replyCut ? "inventory" : "sales")}.outbox_messages WHERE attempts >= 2",
                token
            ) > 0
        );
    }
}
