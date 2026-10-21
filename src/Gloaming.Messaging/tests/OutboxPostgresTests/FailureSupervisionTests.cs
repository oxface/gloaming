using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ModulithFoundry.Tests.Infrastructure;
using Npgsql;
using static Gloaming.Messaging.Tests.OutboxProof;
using static Gloaming.Messaging.Tests.OutboxRecoveryProof;

namespace Gloaming.Messaging.Tests;

[Collection("Messaging PostgreSQL")]
public sealed class FailureSupervisionTests(PostgreSqlFixture postgres)
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task AccountingFaultStopsPollingAndNativeHostSupervisionApplies(
        bool sqlFails,
        bool stopHost
    )
    {
        string connection = await DatabaseAsync(postgres);
        var message = await EnqueueAsync(connection);
        if (sqlFails)
            await SqlAsync(
                connection,
                "ALTER TABLE messages.pending ADD CONSTRAINT reject_accounting CHECK (failure_count = 0)"
            );
        var builder = Host.CreateApplicationBuilder();
        builder.Services.Configure<HostOptions>(options =>
            options.BackgroundServiceExceptionBehavior = stopHost
                ? BackgroundServiceExceptionBehavior.StopHost
                : BackgroundServiceExceptionBehavior.Ignore
        );
        builder.Services.AddDbContext<OutboxConsumer>(options => options.UseNpgsql(connection));
        int entries = 0;
        var publication = new IOException("publisher unavailable");
        builder.Services.AddScoped(_ => new RecordingPublisher
        {
            OnPublish = (_, _) =>
            {
                Interlocked.Increment(ref entries);
                throw publication;
            },
        });
        builder.Services.AddSingleton<IOutboxFailurePolicy<OutboxConsumer>>(
            new Policy(_ =>
                sqlFails
                    ? new OutboxFailureDecision.DeadLetter("review")
                    : throw new InvalidOperationException("policy unavailable")
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
            var error = await Assert.ThrowsAsync<OutboxFailureAccountingException>(() =>
                worker.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10), Token)
            );
            Assert.Equal(message.MessageId, error.MessageId);
            Assert.Same(publication, error.PublicationFailure);
            if (sqlFails)
                Assert.Equal(
                    PostgresErrorCodes.CheckViolation,
                    Assert.IsType<PostgresException>(error.AccountingFailure).SqlState
                );
            else
                Assert.IsType<InvalidOperationException>(error.AccountingFailure);
            if (stopHost)
                await stopping.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
            else
                Assert.False(stopping.Task.IsCompleted);
        }
        finally
        {
            await host.StopAsync(Token);
        }

        Assert.Equal(1, entries);
        var row = await ReadAsync(connection, message.MessageId);
        Assert.Equal(1, row.Attempts);
        Assert.Equal(0, row.FailureCount);
        Assert.NotNull(row.LeaseToken);
        Assert.Null(row.DeadLetteredAt);
    }

    [Fact]
    public async Task CompetingDispatcherSkipsLockedAccountingAndProcessesUnrelatedWork()
    {
        string connection = await DatabaseAsync(postgres);
        var stopped = await EnqueueAsync(connection);
        await EnqueueAsync(connection);
        await using var barrier = new NpgsqlConnection(connection);
        await barrier.OpenAsync(Token);
        await using (
            var install = new NpgsqlCommand(
                """
                SELECT pg_advisory_lock(280021);
                CREATE FUNCTION messages.hold_accounting() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN
                    IF NEW.failure_count > OLD.failure_count THEN PERFORM pg_advisory_xact_lock(280021); END IF;
                    RETURN NEW;
                END; $$;
                CREATE TRIGGER hold_accounting BEFORE UPDATE ON messages.pending FOR EACH ROW EXECUTE FUNCTION messages.hold_accounting();
                """,
                barrier
            )
        )
            await install.ExecuteNonQueryAsync(Token);
        string accountingConnection = new NpgsqlConnectionStringBuilder(connection)
        {
            ApplicationName = "held-accounting",
        }.ConnectionString;
        var failure = FailAsync(accountingConnection, Stop);
        try
        {
            await WaitAsync(
                connection,
                "SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE application_name = 'held-accounting' AND wait_event = 'advisory')"
            );
            await using var successor = Context(connection);
            Assert.Equal(
                OutboxDispatchResult.Published,
                await Dispatcher(successor, new()).DispatchNextAsync(Token)
            );
            Assert.Equal(
                OutboxRetryResult.NotApplied,
                await StoreAsync(
                    connection,
                    store => store.RetryAsync("exports", stopped.MessageId, 1, Token)
                )
            );
            Assert.Empty(
                (
                    await StoreAsync(connection, store => store.ListAsync("exports", new(), Token))
                ).Items
            );
        }
        finally
        {
            await using var release = new NpgsqlCommand(
                "SELECT pg_advisory_unlock(280021)",
                barrier
            );
            await release.ExecuteNonQueryAsync(Token);
        }

        await failure;
        Assert.NotNull((await ReadAsync(connection, stopped.MessageId)).DeadLetteredAt);
    }

    [Fact]
    public async Task RequestedCancellationDuringAccountingPreservesBothErrorsWithoutRecordingFailure()
    {
        string connection = await DatabaseAsync(postgres);
        var message = await EnqueueAsync(connection);
        await using var barrier = new NpgsqlConnection(connection);
        await barrier.OpenAsync(Token);
        NpgsqlTransaction? locked = null;
        var publication = new IOException("publisher failed");
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
                throw publication;
            },
        };
        string named = new NpgsqlConnectionStringBuilder(connection)
        {
            ApplicationName = "cancel-accounting",
        }.ConnectionString;
        await using var database = Context(named);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
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
                "SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE application_name = 'cancel-accounting' AND cardinality(pg_blocking_pids(pid)) > 0)"
            );
            await cancellation.CancelAsync();
            var error = await Assert.ThrowsAsync<OutboxFailureAccountingException>(() => attempt);
            Assert.Same(publication, error.PublicationFailure);
            Assert.IsAssignableFrom<OperationCanceledException>(error.AccountingFailure);
        }
        finally
        {
            if (locked is not null)
                await locked.DisposeAsync();
        }

        var row = await ReadAsync(connection, message.MessageId);
        Assert.Equal(0, row.FailureCount);
        Assert.NotNull(row.LeaseToken);
    }

    [Fact]
    public async Task WorkerShutdownDuringAccountingEndsWithoutAnotherPublication()
    {
        string connection = await DatabaseAsync(postgres);
        var message = await EnqueueAsync(connection);
        await using var barrier = new NpgsqlConnection(connection);
        await barrier.OpenAsync(Token);
        NpgsqlTransaction? locked = null;
        int publications = 0;
        var publisher = new RecordingPublisher
        {
            OnPublish = async (_, token) =>
            {
                Interlocked.Increment(ref publications);
                locked = await barrier.BeginTransactionAsync(token);
                await using var command = new NpgsqlCommand(
                    "SELECT message_id FROM messages.pending FOR UPDATE",
                    barrier,
                    locked
                );
                await command.ExecuteScalarAsync(token);
                throw new IOException("publisher failed before shutdown");
            },
        };
        string named = new NpgsqlConnectionStringBuilder(connection)
        {
            ApplicationName = "shutdown-accounting",
        }.ConnectionString;
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddDbContext<OutboxConsumer>(options => options.UseNpgsql(named));
        services.AddScoped(_ => publisher);
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
                "SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE application_name = 'shutdown-accounting' AND cardinality(pg_blocking_pids(pid)) > 0)"
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

        Assert.Equal(1, publications);
        var row = await ReadAsync(connection, message.MessageId);
        Assert.Equal(0, row.FailureCount);
        Assert.NotNull(row.LeaseToken);
    }
}
