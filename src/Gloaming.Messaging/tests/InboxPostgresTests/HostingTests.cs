using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModulithFoundry.Tests.Infrastructure;
using static Gloaming.Messaging.InboxTests.InboxProof;

namespace Gloaming.Messaging.InboxTests;

public sealed class HostingTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task OptInWorkerRetriesInFreshScopesAndStopsWithoutHoldingAClaim()
    {
        string connection = await DatabaseAsync(fixture);
        int attempts = 0;
        var scenario = new HandlerScenario
        {
            OnHandle = (database, message, _) =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                    throw new IOException("first handling failed");
                database.Add(new HandledItem { Id = message.MessageId, Value = 7 });
                return Task.CompletedTask;
            },
        };
        var services = Services(connection, scenario);
        Assert.DoesNotContain(services, item => item.ServiceType == typeof(IHostedService));
        services.AddInboxWorker<InboxConsumer>(
            "render",
            new(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10))
        );
        await using var provider = services.BuildServiceProvider();
        await ReceiveAsync(provider, Message());
        var worker = Assert.Single(provider.GetServices<IHostedService>());
        await worker.StartAsync(Token);
        try
        {
            await WaitAsync(async () =>
            {
                await using var read = Context(connection);
                var row = await read.Set<InboxMessageRecord>().SingleAsync(Token);
                return row.AvailableAt > row.ReceivedAt.AddSeconds(5);
            });
            await ReadyAsync(connection);
            await WaitAsync(async () =>
            {
                await using var read = Context(connection);
                return await read.Set<InboxMessageRecord>()
                    .AnyAsync(row => row.ProcessedAt != null, Token);
            });
        }
        finally
        {
            await worker.StopAsync(Token).WaitAsync(TimeSpan.FromSeconds(10), Token);
        }
        Assert.Equal(2, attempts);
        Assert.Equal(2, scenario.Contexts.Distinct().Count());
    }

    [Fact]
    public async Task MissingHandlerFailsStartupAndDuplicateBindingsRejectDuringRegistration()
    {
        string connection = await DatabaseAsync(fixture);
        var services = Services(connection);
        Assert.Throws<InvalidOperationException>(() =>
            services.AddInboxHandler<InboxConsumer, LocalHandler>("render")
        );
        services.AddInboxWorker<InboxConsumer>(
            "not-registered",
            new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1))
        );
        await using var provider = services.BuildServiceProvider();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            provider.GetRequiredService<IHostedService>().StartAsync(Token)
        );
    }

    [Fact]
    public async Task ProcessingRejectsExistingTransactionsAndTrackedInboxMutationIsGuarded()
    {
        string connection = await DatabaseAsync(fixture);
        await using var provider = Provider(connection);
        await ReceiveAsync(provider, Message());
        await using var scope = provider.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<InboxConsumer>();
        var processor = scope.ServiceProvider.GetRequiredService<IInboxProcessor<InboxConsumer>>();
        await using (var transaction = await database.Database.BeginTransactionAsync(Token))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                processor.ProcessNextAsync("render", Token)
            );
            await transaction.RollbackAsync(Token);
        }
        var row = await database.Set<InboxMessageRecord>().SingleAsync(Token);
        database.Entry(row).Property(item => item.ProcessedAt).CurrentValue = DateTimeOffset.UtcNow;
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync(Token));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            processor.ProcessNextAsync("render", Token)
        );
        await using var fresh = Context(connection);
        Assert.Null((await fresh.Set<InboxMessageRecord>().SingleAsync(Token)).ProcessedAt);
    }
}
