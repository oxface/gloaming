using System.Threading.Channels;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Gloaming.Messaging.WakeupProof;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static Gloaming.Messaging.WakeupProof.QueueConsumer;

namespace Gloaming.Messaging.Tests;

[Collection("Messaging PostgreSQL")]
public sealed class WakeupTests(PostgreSqlFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task PopulatedTriggerRolloutAndRetainedHintsWakeBothActualTypedConsumers()
    {
        string connection = await SetupAsync(triggers: false);
        var probe = new Probe();
        await using var services = Services(connection, probe);
        await StageAsync(services, Guid.NewGuid(), inbox: true, Token);
        await StageAsync(services, Guid.NewGuid(), inbox: false, Token);
        await SqlAsync(connection, InstallTriggers);
        var wakeups = new NativeWakeups(connection, TimeSpan.FromMilliseconds(100));
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(Token);
        Task pump = wakeups.RunAsync(lifetime.Token);
        Task inbox = Task.CompletedTask;
        Task outbox = Task.CompletedTask;
        try
        {
            await UntilAsync(() => Task.FromResult(wakeups.Registrations == 1));
            inbox = RunInboxAsync(
                services.GetRequiredService<IServiceScopeFactory>(),
                wakeups,
                TimeSpan.FromSeconds(30),
                probe,
                lifetime.Token
            );
            outbox = RunOutboxAsync(
                services.GetRequiredService<IServiceScopeFactory>(),
                wakeups,
                TimeSpan.FromSeconds(30),
                probe,
                lifetime.Token
            );
            await UntilAsync(async () =>
                await HandledAsync(services) == 1
                && probe.Published.Count == 1
                && probe.InboxCompletions == 1
                && probe.OutboxCompletions == 1
            );
            await UntilAsync(() =>
                Task.FromResult(probe.InboxPolls >= 3 && probe.OutboxPolls >= 3)
            );
            await StageAsync(services, Guid.NewGuid(), inbox: true, Token);
            await StageAsync(services, Guid.NewGuid(), inbox: false, Token);
            await UntilAsync(async () =>
                await HandledAsync(services) == 2
                && probe.Published.Count == 2
                && probe.InboxCompletions == 2
                && probe.OutboxCompletions == 2
            );
            Assert.Equal(2, probe.Published.Distinct().Count());
            Assert.InRange(probe.InboxPolls, 3, 10);
            Assert.InRange(probe.OutboxPolls, 3, 10);
        }
        finally
        {
            await StopAsync(lifetime, pump, inbox, outbox);
        }
    }

    [Fact]
    public async Task WorkProgressesWithNotificationsAbsentAndRetriesBecomingDueWithoutWrites()
    {
        string connection = await SetupAsync(triggers: false);
        var probe = new Probe();
        await using var services = Services(connection, probe);
        var wakeups = new NativeWakeups(connection, TimeSpan.FromMilliseconds(100));
        await StageAsync(services, Guid.NewGuid(), inbox: true, Token);
        await StageAsync(services, Guid.NewGuid(), inbox: false, Token);
        await SqlAsync(
            connection,
            "UPDATE waking.outbox SET available_at = clock_timestamp() + interval '200 milliseconds'; UPDATE waking.inbox SET available_at = clock_timestamp() + interval '200 milliseconds'"
        );
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(Token);
        // Intentionally no listener task: fallback polling alone owns correctness and due eligibility.
        Task inbox = RunInboxAsync(
            services.GetRequiredService<IServiceScopeFactory>(),
            wakeups,
            TimeSpan.FromMilliseconds(50),
            probe,
            lifetime.Token
        );
        Task outbox = RunOutboxAsync(
            services.GetRequiredService<IServiceScopeFactory>(),
            wakeups,
            TimeSpan.FromMilliseconds(50),
            probe,
            lifetime.Token
        );
        try
        {
            await UntilAsync(async () =>
                await HandledAsync(services) == 1
                && probe.Published.Count == 1
                && probe.InboxCompletions == 1
                && probe.OutboxCompletions == 1
            );
            Assert.True(probe.InboxPolls > 1);
            Assert.True(probe.OutboxPolls > 1);
        }
        finally
        {
            await StopAsync(lifetime, inbox, outbox);
        }
    }

    [Fact]
    public async Task LostListenerDoesNotStopPollingAndReconnectForcesNewPolls()
    {
        string connection = await SetupAsync();
        var probe = new Probe();
        await using var services = Services(connection, probe);
        var wakeups = new NativeWakeups(connection, TimeSpan.FromSeconds(2));
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(Token);
        Task pump = wakeups.RunAsync(lifetime.Token);
        Task inbox = Task.CompletedTask;
        Task outbox = Task.CompletedTask;
        try
        {
            await UntilAsync(() => Task.FromResult(wakeups.Registrations == 1));
            inbox = RunInboxAsync(
                services.GetRequiredService<IServiceScopeFactory>(),
                wakeups,
                TimeSpan.FromMilliseconds(50),
                probe,
                lifetime.Token
            );
            outbox = RunOutboxAsync(
                services.GetRequiredService<IServiceScopeFactory>(),
                wakeups,
                TimeSpan.FromMilliseconds(50),
                probe,
                lifetime.Token
            );
            int original = wakeups.ListenerPid;
            await SqlAsync(connection, $"SELECT pg_terminate_backend({original})");
            await UntilAsync(() =>
                Task.FromResult(wakeups.Failures > 0 && wakeups.ListenerPid == 0)
            );
            await StageAsync(services, Guid.NewGuid(), inbox: true, Token);
            await StageAsync(services, Guid.NewGuid(), inbox: false, Token);
            await UntilAsync(async () =>
                await HandledAsync(services) == 1
                && probe.Published.Count == 1
                && probe.InboxCompletions == 1
                && probe.OutboxCompletions == 1
            );
            Assert.Equal(1, wakeups.Registrations);
            await UntilAsync(() => Task.FromResult(wakeups.Registrations == 2));
            Assert.NotEqual(original, wakeups.ListenerPid);
            await StageAsync(services, Guid.NewGuid(), inbox: true, Token);
            await StageAsync(services, Guid.NewGuid(), inbox: false, Token);
            await UntilAsync(async () =>
                await HandledAsync(services) == 2
                && probe.Published.Count == 2
                && probe.InboxCompletions == 2
                && probe.OutboxCompletions == 2
            );
        }
        finally
        {
            await StopAsync(lifetime, pump, inbox, outbox);
        }
    }

    [Fact]
    public async Task NotificationConsumedDuringOrdinaryCommandRemainsAvailableBeforeWaiting()
    {
        string connection = await SetupAsync();
        var hints = Channel.CreateBounded<bool>(1);
        await using var listener = new NpgsqlConnection(connection);
        listener.Notification += (_, _) => hints.Writer.TryWrite(true);
        await listener.OpenAsync(Token);
        await using (var register = new NpgsqlCommand("LISTEN proof_outbox", listener))
            await register.ExecuteNonQueryAsync(Token);

        await SqlAsync(connection, "NOTIFY proof_outbox");
        await using (var command = new NpgsqlCommand("SELECT 1", listener))
            await command.ExecuteScalarAsync(Token);

        Assert.Equal(1, hints.Reader.Count);
        await NativeWakeups.WaitOrPollAsync(hints.Reader, TimeSpan.FromSeconds(30), Token);
        Assert.Equal(0, hints.Reader.Count);
    }

    [Fact]
    public async Task RepeatedTimeoutsLeaveNoLosingReadThatCanStealTheNextHint()
    {
        var hints = Channel.CreateBounded<bool>(1);
        for (int i = 0; i < 3; i++)
            await NativeWakeups.WaitOrPollAsync(hints.Reader, TimeSpan.FromMilliseconds(10), Token);

        Assert.True(hints.Writer.TryWrite(true));
        Assert.Equal(1, hints.Reader.Count);
        await NativeWakeups.WaitOrPollAsync(hints.Reader, TimeSpan.FromSeconds(30), Token);
        Assert.Equal(0, hints.Reader.Count);
    }

    [Fact]
    public async Task RolledBackWritesAndCoalescedSpuriousHintsCannotInventQueueWork()
    {
        string connection = await SetupAsync();
        var wakeups = new NativeWakeups(connection, TimeSpan.FromMilliseconds(100));
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(Token);
        Task pump = wakeups.RunAsync(lifetime.Token);
        try
        {
            await UntilAsync(() => Task.FromResult(wakeups.Registrations == 1));
            Assert.True(wakeups.Inbox.TryRead(out _));
            Assert.True(wakeups.Outbox.TryRead(out _));
            await SqlAsync(connection, "BEGIN; NOTIFY proof_inbox; ROLLBACK");
            await SqlAsync(connection, "NOTIFY proof_outbox");
            await UntilAsync(() => Task.FromResult(wakeups.Outbox.Count == 1));
            Assert.Equal(0, wakeups.Inbox.Count);
            Assert.True(wakeups.Outbox.TryRead(out _));
            await SqlAsync(
                connection,
                "BEGIN; "
                    + string.Concat(
                        Enumerable.Repeat("NOTIFY proof_inbox; NOTIFY proof_outbox;", 100)
                    )
                    + " COMMIT"
            );
            await UntilAsync(() =>
                Task.FromResult(wakeups.Inbox.Count == 1 && wakeups.Outbox.Count == 1)
            );
            await using var services = Services(connection);
            await using var scope = services.CreateAsyncScope();
            Assert.Equal(
                InboxProcessingResult.NoWork,
                await scope
                    .ServiceProvider.GetRequiredService<IInboxProcessor<QueueDatabase>>()
                    .ProcessNextAsync("selected", Token)
            );
            Assert.Equal(
                OutboxDispatchResult.NoWork,
                await scope
                    .ServiceProvider.GetRequiredService<IOutboxDispatcher<QueueDatabase>>()
                    .DispatchNextAsync(Token)
            );
            Assert.Equal(1, wakeups.Inbox.Count);
            Assert.Equal(1, wakeups.Outbox.Count);
        }
        finally
        {
            await StopAsync(lifetime, pump);
        }
    }

    private async Task<string> SetupAsync(bool triggers = true)
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using var services = Services(connection);
        await using var scope = services.CreateAsyncScope();
        await scope
            .ServiceProvider.GetRequiredService<QueueDatabase>()
            .Database.EnsureCreatedAsync(Token);
        if (triggers)
            await SqlAsync(connection, InstallTriggers);

        return connection;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task FatalTypedAccountingFaultsReachTheSupervisingHost(bool inbox)
    {
        string connection = await SetupAsync();
        var probe = new Probe { FailHandler = inbox };
        await using var services = Services(connection, probe);
        await StageAsync(services, Guid.NewGuid(), inbox, Token);
        await SqlAsync(
            connection,
            inbox
                ? """
                CREATE FUNCTION waking.fail_accounting() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN IF NEW.failure_count > OLD.failure_count THEN RAISE EXCEPTION 'injected inbox accounting fault'; END IF; RETURN NEW; END $$;
                CREATE TRIGGER fail_accounting BEFORE UPDATE ON waking.inbox FOR EACH ROW EXECUTE FUNCTION waking.fail_accounting();
                """
                : """
                CREATE FUNCTION waking.fail_accounting() RETURNS trigger LANGUAGE plpgsql AS $$
                BEGIN IF NEW.dispatched_at IS NOT NULL THEN RAISE EXCEPTION 'injected outbox completion fault'; END IF; RETURN NEW; END $$;
                CREATE TRIGGER fail_accounting BEFORE UPDATE ON waking.outbox FOR EACH ROW EXECUTE FUNCTION waking.fail_accounting();
                """
        );
        var wakeups = new NativeWakeups(connection, TimeSpan.FromMilliseconds(100));
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(Token);
        Task worker = SuperviseAsync(
            services.GetRequiredService<IServiceScopeFactory>(),
            wakeups,
            TimeSpan.FromSeconds(1),
            probe,
            lifetime.Token
        );
        if (inbox)
            await Assert.ThrowsAsync<InboxFailureAccountingException>(() => worker);
        else
        {
            await Assert.ThrowsAsync<OutboxCompletionException>(() => worker);
            Assert.Single(probe.Published);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeWaitCancellationDisposesTheExclusiveSession(bool preCancelled)
    {
        string connection = await SetupAsync();
        var settings = new NpgsqlConnectionStringBuilder(connection)
        {
            KeepAlive = 1,
            Pooling = false,
        };
        int pid;
        await using (var listener = new NpgsqlConnection(settings.ConnectionString))
        {
            await listener.OpenAsync(Token);
            pid = listener.ProcessID;
            await using (var register = new NpgsqlCommand("LISTEN proof_outbox", listener))
                await register.ExecuteNonQueryAsync(Token);

            using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
            if (preCancelled)
                await cancellation.CancelAsync();

            var waiting = listener.WaitAsync(5000, cancellation.Token);
            if (!preCancelled)
            {
                Assert.False(waiting.IsCompleted);
                await cancellation.CancelAsync();
            }

            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting);
        }

        await using var observer = new NpgsqlConnection(connection);
        await observer.OpenAsync(Token);
        await UntilAsync(async () =>
        {
            await using var command = new NpgsqlCommand(
                "SELECT NOT EXISTS(SELECT 1 FROM pg_stat_activity WHERE pid = @pid)",
                observer
            );
            command.Parameters.AddWithValue("pid", pid);
            return (bool)(await command.ExecuteScalarAsync(Token))!;
        });
    }

    [Fact]
    public async Task ResetPooledSessionsDoNotLeakListenRegistrationAndMultiplexingIsRejected()
    {
        string connection = await SetupAsync();
        var settings = new NpgsqlConnectionStringBuilder(connection)
        {
            Pooling = true,
            NoResetOnClose = false,
            MaxPoolSize = 1,
        };
        await using (var source = NpgsqlDataSource.Create(settings.ConnectionString))
        {
            int firstPid;
            await using (var first = await source.OpenConnectionAsync(Token))
            {
                firstPid = first.ProcessID;
                await using var register = new NpgsqlCommand("LISTEN proof_outbox", first);
                await register.ExecuteNonQueryAsync(Token);
            }

            await using var second = await source.OpenConnectionAsync(Token);
            Assert.Equal(firstPid, second.ProcessID);
            await using var check = new NpgsqlCommand(
                "SELECT count(*) FROM pg_listening_channels()",
                second
            );
            Assert.Equal(0L, await check.ExecuteScalarAsync(Token));
        }

        settings.Multiplexing = true;
        await using var multiplexedSource = NpgsqlDataSource.Create(settings.ConnectionString);
        await using var multiplexed = await multiplexedSource.OpenConnectionAsync(Token);
        await Assert.ThrowsAsync<NotSupportedException>(() => multiplexed.WaitAsync(10, Token));
    }

    private static async Task<long> HandledAsync(ServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<QueueDatabase>()
            .Set<Handled>()
            .LongCountAsync(Token);
    }

    [Fact]
    public async Task UnchangedEarlierNativeWriterWorksAcrossAdditiveTriggerRolloutAndRemoval()
    {
        string connection = await SetupAsync(triggers: false);
        await EarlierAsync(connection);
        await SqlAsync(connection, InstallTriggers);
        var probe = new Probe();
        await using var services = Services(connection, probe);
        var wakeups = new NativeWakeups(connection, TimeSpan.FromMilliseconds(100));
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(Token);
        Task running = SuperviseAsync(
            services.GetRequiredService<IServiceScopeFactory>(),
            wakeups,
            TimeSpan.FromMilliseconds(50),
            probe,
            lifetime.Token
        );
        try
        {
            await UntilAsync(async () =>
                await HandledAsync(services) == 1
                && probe.Published.Count == 1
                && probe.InboxCompletions == 1
                && probe.OutboxCompletions == 1
            );
            await EarlierAsync(connection);
            await UntilAsync(async () =>
                await HandledAsync(services) == 2
                && probe.Published.Count == 2
                && probe.InboxCompletions == 2
                && probe.OutboxCompletions == 2
            );
            await SqlAsync(
                connection,
                "DROP TRIGGER queue_hint ON waking.inbox; DROP TRIGGER queue_hint ON waking.outbox; DROP FUNCTION waking.queue_hint()"
            );
            await EarlierAsync(connection);
            await UntilAsync(async () =>
                await HandledAsync(services) == 3
                && probe.Published.Count == 3
                && probe.InboxCompletions == 3
                && probe.OutboxCompletions == 3
            );
            Assert.Equal(3, probe.Published.Distinct().Count());
        }
        finally
        {
            await StopAsync(lifetime, running);
        }
    }

    [Fact]
    public async Task RestrictedRuntimeRoleListensSignalsAndProcessesWithoutMigrationOrDeletionRights()
    {
        string connection = await SetupAsync();
        string role = "wakeup_runtime_" + Guid.NewGuid().ToString("N");
        string password = Guid.NewGuid().ToString("N");
        var settings = new NpgsqlConnectionStringBuilder(connection)
        {
            Username = role,
            Password = password,
        };
        await SqlAsync(
            connection,
            $"""
            CREATE ROLE {role} LOGIN PASSWORD '{password}' NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOINHERIT;
            GRANT CONNECT ON DATABASE "{settings.Database}" TO {role};
            GRANT USAGE ON SCHEMA waking TO {role};
            GRANT SELECT, INSERT, UPDATE ON waking.inbox, waking.outbox TO {role};
            GRANT SELECT, INSERT ON waking.handled TO {role};
            """
        );
        Exception? failure = null;
        try
        {
            await using var native = new NpgsqlConnection(settings.ConnectionString);
            await native.OpenAsync(Token);
            await using (
                var audit = new NpgsqlCommand(
                    """
                    SELECT NOT rolsuper AND NOT rolbypassrls AND NOT rolcreatedb AND NOT rolcreaterole
                        AND NOT EXISTS(SELECT 1 FROM pg_auth_members WHERE member = r.oid)
                        AND NOT has_schema_privilege(current_user, 'waking', 'CREATE')
                        AND NOT has_table_privilege(current_user, 'waking.inbox', 'DELETE')
                        AND NOT has_table_privilege(current_user, 'waking.outbox', 'DELETE')
                    FROM pg_roles r WHERE rolname = current_user
                    """,
                    native
                )
            )
                Assert.Equal(true, await audit.ExecuteScalarAsync(Token));

            await using (var denied = new NpgsqlCommand("DELETE FROM waking.outbox", native))
                Assert.Equal(
                    PostgresErrorCodes.InsufficientPrivilege,
                    (
                        await Assert.ThrowsAsync<PostgresException>(() =>
                            denied.ExecuteNonQueryAsync(Token)
                        )
                    ).SqlState
                );

            var probe = new Probe();
            await using var services = Services(settings.ConnectionString, probe);
            var wakeups = new NativeWakeups(
                settings.ConnectionString,
                TimeSpan.FromMilliseconds(100)
            );
            using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(Token);
            Task running = SuperviseAsync(
                services.GetRequiredService<IServiceScopeFactory>(),
                wakeups,
                TimeSpan.FromSeconds(30),
                probe,
                lifetime.Token
            );
            try
            {
                await UntilAsync(() => Task.FromResult(wakeups.Registrations == 1));
                await StageAsync(services, Guid.NewGuid(), inbox: true, Token);
                await StageAsync(services, Guid.NewGuid(), inbox: false, Token);
                await UntilAsync(async () =>
                    await HandledAsync(services) == 1
                    && probe.Published.Count == 1
                    && probe.InboxCompletions == 1
                    && probe.OutboxCompletions == 1
                );
            }
            finally
            {
                await StopAsync(lifetime, running);
            }
        }
        catch (Exception error)
        {
            failure = error;
            throw;
        }
        finally
        {
            using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            try
            {
                await using var owner = new NpgsqlConnection(connection);
                await owner.OpenAsync(cleanup.Token);
                await using var remove = new NpgsqlCommand(
                    $"DROP OWNED BY {role}; DROP ROLE {role}",
                    owner
                );
                await remove.ExecuteNonQueryAsync(cleanup.Token);
            }
            catch (Exception error) when (failure is not null)
            {
                Console.Error.WriteLine($"Wakeup role cleanup failed ({error.GetType().Name}).");
            }
        }
    }

    private static async Task EarlierAsync(string connection)
    {
        await using var old = new ChildHost(
            typeof(WakeupEarlier.Application).Assembly,
            new Dictionary<string, string> { ["WAKEUP_CONNECTION"] = connection },
            Guid.NewGuid().ToString("D")
        );
        Assert.True(await old.WaitForExitAsync(Token) == 0, old.Output);
    }

    [Fact]
    public async Task NotificationTriggerFailureRollsBackTheWholeProducerDecision()
    {
        string connection = await SetupAsync();
        await using var services = Services(connection);
        await SqlAsync(
            connection,
            """
            CREATE OR REPLACE FUNCTION waking.queue_hint() RETURNS trigger LANGUAGE plpgsql SECURITY INVOKER
            SET search_path = pg_catalog, pg_temp AS $$
            BEGIN
                IF TG_TABLE_NAME = 'outbox' THEN RAISE EXCEPTION 'owned notification trigger failed'; END IF;
                PERFORM pg_catalog.pg_notify('proof_inbox', ''); RETURN NULL;
            END $$;
            """
        );
        await Assert.ThrowsAsync<DbUpdateException>(async () =>
        {
            await using var scope = services.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<QueueDatabase>();
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            await scope
                .ServiceProvider.GetRequiredService<IInbox<QueueDatabase>>()
                .ReceiveAsync(
                    "selected",
                    new(
                        Guid.NewGuid(),
                        "owned",
                        "owned.work",
                        1,
                        System.Text.Json.JsonSerializer.SerializeToElement(new { value = 7 })
                    ),
                    Token
                );
            scope
                .ServiceProvider.GetRequiredService<IOutbox<QueueDatabase>>()
                .Enqueue(
                    new(
                        Guid.NewGuid(),
                        "selected",
                        "owned.work",
                        1,
                        System.Text.Json.JsonSerializer.SerializeToElement(new { value = 7 })
                    )
                );
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        });
        await using var read = services.CreateAsyncScope();
        var fresh = read.ServiceProvider.GetRequiredService<QueueDatabase>();
        Assert.Equal(0, await fresh.Set<InboxMessageRecord>().CountAsync(Token));
        Assert.Equal(0, await fresh.Set<OutboxMessageRecord>().CountAsync(Token));
    }

    private static async Task SqlAsync(string connection, string sql)
    {
        await using var session = new NpgsqlConnection(connection);
        await session.OpenAsync(Token);
        await using var command = new NpgsqlCommand(sql, session);
        await command.ExecuteNonQueryAsync(Token);
    }

    private static async Task UntilAsync(Func<Task<bool>> condition)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        while (!await condition())
            await Task.Delay(20, deadline.Token);
    }

    private static async Task StopAsync(CancellationTokenSource lifetime, params Task[] running)
    {
        await lifetime.CancelAsync();
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await Task.WhenAll(running).WaitAsync(cleanup.Token);
    }
}
