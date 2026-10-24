using System.Data.Common;
using Gloaming.EventSourcing.EntityFrameworkCore.Postgres;
using Gloaming.EventSourcing.Postgres.Tests;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Gloaming.EventSourcing.FeedProof;

public static class Application
{
    public static async Task Main(string[] args)
    {
        string connection =
            Environment.GetEnvironmentVariable("FEED_CONNECTION")
            ?? throw new InvalidOperationException("Select a disposable proof database.");
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        string phase = args[0];
        await using var services = new ServiceCollection()
            .AddDbContext<FeedConsumer.Views>(options =>
                options
                    .UseNpgsql(connection)
                    .AddInterceptors(new SaveBarrier(phase), new CommitBarrier(phase))
            )
            .BuildServiceProvider();
        var result = await FeedConsumer
            .Feed(services)
            .ProjectNextAsync(FeedConsumer.Ledger(), new(32, 1024 * 1024), deadline.Token);
        if (result.Status != ProjectionStatus.Applied)
            throw new InvalidOperationException(
                "The independent projection did not apply.",
                result.Error
            );
    }

    private static async Task HoldAsync(string requested, string phase, CancellationToken token)
    {
        if (requested != phase)
            return;

        Console.WriteLine(phase.ToUpperInvariant());
        await Task.Delay(Timeout.InfiniteTimeSpan, token);
    }

    private sealed class SaveBarrier(string phase) : SaveChangesInterceptor
    {
        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            await HoldAsync(phase, "staged", cancellationToken);
            return result;
        }

        public override async ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        )
        {
            await HoldAsync(phase, "saved", cancellationToken);
            return result;
        }
    }

    private sealed class CommitBarrier(string phase) : DbTransactionInterceptor
    {
        public override async Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default
        )
        {
            await HoldAsync(phase, "committed", cancellationToken);
        }
    }
}
