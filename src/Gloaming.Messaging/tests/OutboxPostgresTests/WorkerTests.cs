using System.Collections.Concurrent;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Hosting;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ModulithFoundry.Tests.Infrastructure;
using static Gloaming.Messaging.Tests.OutboxProof;

namespace Gloaming.Messaging.Tests;

[Collection("Messaging PostgreSQL")]
public sealed class WorkerTests(PostgreSqlFixture postgres)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OptionalWorkerRecoversFromPublicationFailureOrClaimLossInFreshScopes(
        bool claimLost
    )
    {
        string connection = await DatabaseAsync(postgres);
        var message = await EnqueueAsync(connection);
        var accepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var contexts = new ConcurrentQueue<Guid>();
        int attempts = 0;
        var services = Services(connection);
        services.AddScoped(provider => new RecordingPublisher
        {
            OnPublish = async (_, _) =>
            {
                contexts.Enqueue(
                    provider.GetRequiredService<OutboxConsumer>().ContextId.InstanceId
                );
                if (Interlocked.Increment(ref attempts) == 1)
                {
                    if (claimLost)
                    {
                        await ExpireAsync(connection);
                        return;
                    }

                    throw new IOException("first publication fails");
                }

                accepted.TrySetResult();
            },
        });
        services.AddPostgresOutboxDispatcher<OutboxConsumer, RecordingPublisher>(
            new(TimeSpan.FromSeconds(30), TimeSpan.Zero)
        );
        Assert.DoesNotContain(
            services,
            descriptor => descriptor.ServiceType == typeof(IHostedService)
        );
        services.AddOutboxWorker<OutboxConsumer>(
            new(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10))
        );
        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
        );
        var worker = Assert.Single(provider.GetServices<IHostedService>());
        await worker.StartAsync(Token);
        try
        {
            await accepted.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            while (true)
            {
                await using var fresh = Context(connection);
                if (
                    (await fresh.Set<OutboxMessageRecord>().SingleAsync(timeout.Token)).DispatchedAt
                    is not null
                )
                    break;
                await Task.Delay(10, timeout.Token);
            }
        }
        finally
        {
            await worker.StopAsync(Token).WaitAsync(TimeSpan.FromSeconds(10), Token);
        }
        Assert.Equal(2, attempts);
        Assert.Equal(2, contexts.Distinct().Count());
        await using var read = Context(connection);
        Assert.Equal(
            claimLost ? 0 : 1,
            (await read.Set<OutboxMessageRecord>().SingleAsync(Token)).FailureCount
        );
        Assert.Equal(
            message.MessageId,
            (await read.Set<OutboxMessageRecord>().SingleAsync(Token)).MessageId
        );
    }

    [Fact]
    public async Task ShutdownCancelsInFlightPublicationAndLeavesItsLeaseRecoverable()
    {
        string connection = await DatabaseAsync(postgres);
        await EnqueueAsync(connection);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var services = Services(connection);
        services.AddScoped(_ => new RecordingPublisher
        {
            OnPublish = async (_, token) =>
            {
                entered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            },
        });
        services.AddPostgresOutboxDispatcher<OutboxConsumer, RecordingPublisher>(
            new(TimeSpan.FromSeconds(30), TimeSpan.Zero)
        );
        services.AddOutboxWorker<OutboxConsumer>(
            new(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10))
        );
        await using var provider = services.BuildServiceProvider();
        var worker = Assert.Single(provider.GetServices<IHostedService>());
        await worker.StartAsync(Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
        await worker.StopAsync(Token).WaitAsync(TimeSpan.FromSeconds(10), Token);
        await using var fresh = Context(connection);
        var row = await fresh.Set<OutboxMessageRecord>().SingleAsync(Token);
        Assert.Null(row.DispatchedAt);
        Assert.NotNull(row.LeaseToken);
        await ExpireAsync(connection);
        Assert.Equal(
            OutboxDispatchResult.Published,
            await Dispatcher(fresh, new()).DispatchNextAsync(Token)
        );
    }

    [Fact]
    public async Task MissingConfigurationFailsWorkerStartupInsteadOfBeingRetriedForever()
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddOutboxWorker<OutboxConsumer>(
            new(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1))
        );
        await using var provider = services.BuildServiceProvider();
        var worker = Assert.Single(provider.GetServices<IHostedService>());
        await Assert.ThrowsAsync<InvalidOperationException>(() => worker.StartAsync(Token));
    }

    private static ServiceCollection Services(string connection)
    {
        var services = new ServiceCollection();
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        services.AddDbContext<OutboxConsumer>(options => options.UseNpgsql(connection));
        return services;
    }
}
