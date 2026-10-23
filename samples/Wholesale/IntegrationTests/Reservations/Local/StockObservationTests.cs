using System.Data.Common;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Samples.Wholesale.Inventory;
using Gloaming.Samples.Wholesale.Sales;
using Gloaming.Samples.Wholesale.Worker.Local;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Gloaming.Samples.Wholesale.IntegrationTests.Reservations.Local;

public sealed class StockObservationTests(PostgreSqlFixture postgres)
    : IClassFixture<PostgreSqlFixture>
{
    [Fact]
    public async Task ObservationKeepsOneSnapshotWhenAWorkerCommitsBetweenStockReads()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        var token = deadline.Token;
        var barrier = new BetweenStockReads();
        await using var proof = await LocalWorkflowProof.CreateAsync(postgres, token, barrier);
        Guid first = await proof.SeedAsync(token),
            second = await proof.SeedAsync(token);
        await proof.StartAsync(LocalWorkflowProof.Request(first, second), token);
        Assert.Equal(
            OutboxDispatchResult.Published,
            await proof.DispatchAsync<SalesDbContext>(token)
        );
        var before = await proof.StockAsync(first, token);

        barrier.Arm();
        var observation = proof.StockAsync(first, token);
        try
        {
            await barrier.Entered.WaitAsync(token);
            Assert.Equal(
                InboxProcessingResult.Processed,
                await proof.ProcessAsync<InventoryDbContext>(token)
            );
        }
        finally
        {
            barrier.Release();
        }

        Assert.Equal(before, await observation);
        var after = await proof.StockAsync(first, token);
        Assert.Equal(before.Version + 1, after.Version);
        Assert.Equal(2, after.Reserved);
    }

    private sealed class BetweenStockReads : DbCommandInterceptor
    {
        private readonly TaskCompletionSource entered = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private readonly TaskCompletionSource released = new(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        private int armed;

        internal Task Entered => entered.Task;

        internal void Arm() => Interlocked.Exchange(ref armed, 1);

        internal void Release() => released.TrySetResult();

        public override async ValueTask<DbDataReader> ReaderExecutedAsync(
            DbCommand command,
            CommandExecutedEventData eventData,
            DbDataReader result,
            CancellationToken cancellationToken = default
        )
        {
            if (
                command.CommandText.Contains(
                    "FROM inventory.event_streams",
                    StringComparison.Ordinal
                )
                && Interlocked.CompareExchange(ref armed, 0, 1) == 1
            )
            {
                entered.TrySetResult();
                await released.Task.WaitAsync(cancellationToken);
            }

            return result;
        }
    }
}
