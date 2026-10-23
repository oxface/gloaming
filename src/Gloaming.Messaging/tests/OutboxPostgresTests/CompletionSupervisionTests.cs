using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Hosting;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Gloaming.Messaging.TestSupport;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using static Gloaming.Messaging.Tests.OutboxProof;
using static Gloaming.Messaging.Tests.OutboxRecoveryProof;

namespace Gloaming.Messaging.Tests;

[Collection("Messaging PostgreSQL")]
public sealed class CompletionSupervisionTests(PostgreSqlFixture postgres)
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task UnconfirmedCompletionFaultsWorkerAndNativeHostSupervisionApplies(
        bool commitResponseLost,
        bool stopHost
    )
    {
        string connection = await DatabaseAsync(postgres);
        var message = await EnqueueAsync(connection);
        if (!commitResponseLost)
            await SqlAsync(
                connection,
                "ALTER TABLE messages.pending ADD CONSTRAINT completion_unavailable CHECK (dispatched_at IS NULL)"
            );

        var builder = Host.CreateApplicationBuilder();
        builder.Services.Configure<HostOptions>(options =>
            options.BackgroundServiceExceptionBehavior = stopHost
                ? BackgroundServiceExceptionBehavior.StopHost
                : BackgroundServiceExceptionBehavior.Ignore
        );
        var lostResponse = new LostCommitResponse(2);
        builder.Services.AddDbContext<OutboxConsumer>(options =>
        {
            options.UseNpgsql(connection);
            if (commitResponseLost)
                options.AddInterceptors(lostResponse);
        });
        var publisher = new RecordingPublisher();
        builder.Services.AddSingleton(publisher);
        builder.Services.AddSingleton<IOutboxFailurePolicy<OutboxConsumer>>(
            new Policy(_ =>
                throw new InvalidOperationException("Completion must not invoke policy")
            )
        );
        builder.Services.AddPostgresOutboxDispatcher<OutboxConsumer, RecordingPublisher>(
            new(TimeSpan.FromSeconds(30), TimeSpan.Zero)
        );
        builder.Services.AddOutboxWorker<OutboxConsumer>(
            new(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10))
        );
        using var host = builder.Build();
        var stopping = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var registration = host
            .Services.GetRequiredService<IHostApplicationLifetime>()
            .ApplicationStopping.Register(() => stopping.TrySetResult());
        await host.StartAsync(Token);
        try
        {
            var worker = host
                .Services.GetServices<IHostedService>()
                .OfType<BackgroundService>()
                .Single();
            var error = await Assert.ThrowsAsync<OutboxCompletionException>(() =>
                worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10), Token)
            );
            Assert.Equal(message.MessageId, error.MessageId);
            if (commitResponseLost)
                Assert.IsType<IOException>(error.InnerException);
            else
                Assert.Equal(
                    PostgresErrorCodes.CheckViolation,
                    Assert.IsType<PostgresException>(error.InnerException).SqlState
                );

            if (stopHost)
                await stopping.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
            else
                Assert.False(stopping.Task.IsCompleted);
        }
        finally
        {
            await host.StopAsync(Token);
        }

        Assert.Equal(message.MessageId, Assert.Single(publisher.Messages).MessageId);
        var row = await ReadAsync(connection, message.MessageId);
        Assert.Equal(1, row.Attempts);
        Assert.Equal(commitResponseLost ? 2 : 1, row.Revision);
        Assert.Equal(0, row.FailureCount);
        Assert.Equal(0, row.FailuresSinceRedrive);
        Assert.Null(row.LastFailedAt);
        Assert.Null(row.LastFailureCode);
        Assert.Null(row.DeadLetteredAt);
        Assert.Equal(commitResponseLost, row.DispatchedAt is not null);
        Assert.Equal(!commitResponseLost, row.LeaseToken is not null);
        Assert.Equal(
            OutboxRetryResult.NotApplied,
            await StoreAsync(
                connection,
                store => store.RetryAsync("exports", message.MessageId, row.Revision, Token)
            )
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BlockedCompletionRetainsItsStageAndOriginalErrorOnCancellationOrBackendDeath(
        bool backendDies
    )
    {
        string connection = await DatabaseAsync(postgres);
        var message = await EnqueueAsync(connection);
        await using var barrier = new NpgsqlConnection(connection);
        await barrier.OpenAsync(Token);
        NpgsqlTransaction? locked = null;
        var publisher = new RecordingPublisher
        {
            OnPublish = async (_, token) =>
            {
                locked = await barrier.BeginTransactionAsync(token);
                await using var command = new NpgsqlCommand(
                    "SELECT message_id FROM messages.pending FOR UPDATE",
                    barrier,
                    locked
                );
                await command.ExecuteScalarAsync(token);
            },
        };
        string named = new NpgsqlConnectionStringBuilder(connection)
        {
            ApplicationName = "blocked-completion",
        }.ConnectionString;
        await using var database = Context(named);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        using var telemetry = new MessagingTelemetryProbe();
        var attempt = new PostgresOutboxDispatcher<OutboxConsumer>(
            database,
            publisher,
            new(TimeSpan.FromSeconds(30), TimeSpan.Zero),
            failurePolicy: Stop
        ).DispatchNextAsync(cancellation.Token);
        try
        {
            await WaitAsync(
                connection,
                "SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE application_name = 'blocked-completion' AND cardinality(pg_blocking_pids(pid)) > 0)"
            );
            if (backendDies)
                Assert.True(
                    await ScalarAsync(
                        connection,
                        "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE application_name = 'blocked-completion'"
                    )
                );
            else
                await cancellation.CancelAsync();

            var error = await Assert.ThrowsAsync<OutboxCompletionException>(() => attempt);
            Assert.Equal(message.MessageId, error.MessageId);
            if (backendDies)
                Assert.IsAssignableFrom<NpgsqlException>(error.InnerException);
            else
                Assert.IsAssignableFrom<OperationCanceledException>(error.InnerException);

            string outcome = backendDies ? "failed" : "cancelled";
            Assert.Equal(
                outcome,
                Assert.Single(telemetry.Spans).GetTagItem("gloaming.messaging.result")
            );
            Assert.All(telemetry.Metrics, metric => Assert.Equal(outcome, metric.Result));
        }
        finally
        {
            if (locked is not null)
                await locked.DisposeAsync();
        }

        var row = await ReadAsync(connection, message.MessageId);
        Assert.Equal(1, row.Attempts);
        Assert.Equal(0, row.FailureCount);
        Assert.Null(row.DispatchedAt);
        Assert.Null(row.DeadLetteredAt);
        Assert.NotNull(row.LeaseToken);
        await ExpireAsync(connection);
        publisher.OnPublish = null;
        await using var successor = Context(connection);
        Assert.Equal(
            OutboxDispatchResult.Published,
            await Dispatcher(successor, publisher).DispatchNextAsync(Token)
        );
        Assert.All(
            publisher.Messages,
            delivered => Assert.Equal(message.MessageId, delivered.MessageId)
        );
    }

    [Fact]
    public async Task WorkerShutdownDuringBlockedCompletionEndsNormallyWithoutAnotherPublication()
    {
        string connection = await DatabaseAsync(postgres);
        var message = await EnqueueAsync(connection);
        await using var barrier = new NpgsqlConnection(connection);
        await barrier.OpenAsync(Token);
        NpgsqlTransaction? locked = null;
        var publisher = new RecordingPublisher
        {
            OnPublish = async (_, token) =>
            {
                locked = await barrier.BeginTransactionAsync(token);
                await using var command = new NpgsqlCommand(
                    "SELECT message_id FROM messages.pending FOR UPDATE",
                    barrier,
                    locked
                );
                await command.ExecuteScalarAsync(token);
            },
        };
        string named = new NpgsqlConnectionStringBuilder(connection)
        {
            ApplicationName = "shutdown-completion",
        }.ConnectionString;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<OutboxConsumer>(options => options.UseNpgsql(named));
        services.AddSingleton(publisher);
        services.AddPostgresOutboxDispatcher<OutboxConsumer, RecordingPublisher>(
            new(TimeSpan.FromSeconds(30), TimeSpan.Zero)
        );
        services.AddOutboxWorker<OutboxConsumer>(
            new(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(10))
        );
        await using var provider = services.BuildServiceProvider();
        var worker = Assert.IsAssignableFrom<BackgroundService>(
            Assert.Single(provider.GetServices<IHostedService>())
        );
        await worker.StartAsync(Token);
        try
        {
            await WaitAsync(
                connection,
                "SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE application_name = 'shutdown-completion' AND cardinality(pg_blocking_pids(pid)) > 0)"
            );
            await worker.StopAsync(Token).WaitAsync(TimeSpan.FromSeconds(10), Token);
            Assert.True(worker.ExecuteTask!.IsCompletedSuccessfully);
        }
        finally
        {
            await worker.StopAsync(Token);
            if (locked is not null)
                await locked.DisposeAsync();
        }

        Assert.Single(publisher.Messages);
        var row = await ReadAsync(connection, message.MessageId);
        Assert.Equal(0, row.FailureCount);
        Assert.Null(row.DispatchedAt);
        Assert.NotNull(row.LeaseToken);
    }
}
