using System.Collections.Concurrent;
using System.Text.Json;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gloaming.Messaging.WakeupProof;

public static class QueueConsumer
{
    public static async Task SuperviseAsync(
        IServiceScopeFactory scopes,
        NativeWakeups wakeups,
        TimeSpan fallback,
        Probe probe,
        CancellationToken token
    )
    {
        using var lifetime = CancellationTokenSource.CreateLinkedTokenSource(token);
        Task[] running =
        [
            wakeups.RunAsync(lifetime.Token),
            RunInboxAsync(scopes, wakeups, fallback, probe, lifetime.Token),
            RunOutboxAsync(scopes, wakeups, fallback, probe, lifetime.Token),
        ];
        Exception? failure = null;
        try
        {
            Task finished = await Task.WhenAny(running);
            await finished;
            if (!token.IsCancellationRequested)
                throw new InvalidOperationException("A selected worker stopped unexpectedly.");
        }
        catch (Exception error)
        {
            failure = error;
        }

        try
        {
            await lifetime.CancelAsync();
        }
        catch (Exception error)
        {
            failure = failure is null ? error : new AggregateException(failure, error);
        }

        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await Task.WhenAll(running).WaitAsync(cleanup.Token);
        }
        catch (Exception error)
        {
            if (!ReferenceEquals(failure, error))
                failure = failure is null ? error : new AggregateException(failure, error);
        }

        if (failure is not null)
            System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    public static ServiceProvider Services(string connection, Probe? probe = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton(probe ?? new Probe());
        services.AddDbContext<QueueDatabase>(options => options.UseNpgsql(connection));
        services.AddPostgresInbox<QueueDatabase>();
        services.AddPostgresInboxProcessor<QueueDatabase>(new(TimeSpan.FromSeconds(10)));
        services.AddInboxHandler<QueueDatabase, Handler>("selected");
        services.AddPostgresOutbox<QueueDatabase>();
        services.AddPostgresOutboxDispatcher<QueueDatabase, Publisher>(
            new(TimeSpan.FromSeconds(10), TimeSpan.Zero)
        );
        return services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
        );
    }

    public static async Task RunInboxAsync(
        IServiceScopeFactory scopes,
        NativeWakeups wakeups,
        TimeSpan fallback,
        Probe probe,
        CancellationToken token
    )
    {
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                InboxProcessingResult result;
                await using (var scope = scopes.CreateAsyncScope())
                {
                    probe.CountInboxPoll();
                    result = await scope
                        .ServiceProvider.GetRequiredService<IInboxProcessor<QueueDatabase>>()
                        .ProcessNextAsync("selected", token);
                }

                if (result != InboxProcessingResult.NoWork)
                    probe.CountInboxCompletion();

                if (result == InboxProcessingResult.NoWork)
                    await NativeWakeups.WaitOrPollAsync(wakeups.Inbox, fallback, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
        // All other faults, including unconfirmed accounting, reach the supervising host.
    }

    public static async Task RunOutboxAsync(
        IServiceScopeFactory scopes,
        NativeWakeups wakeups,
        TimeSpan fallback,
        Probe probe,
        CancellationToken token
    )
    {
        try
        {
            while (true)
            {
                token.ThrowIfCancellationRequested();
                OutboxDispatchResult result;
                await using (var scope = scopes.CreateAsyncScope())
                {
                    probe.CountOutboxPoll();
                    result = await scope
                        .ServiceProvider.GetRequiredService<IOutboxDispatcher<QueueDatabase>>()
                        .DispatchNextAsync(token);
                }

                if (result != OutboxDispatchResult.NoWork)
                    probe.CountOutboxCompletion();

                if (result == OutboxDispatchResult.NoWork)
                    await NativeWakeups.WaitOrPollAsync(wakeups.Outbox, fallback, token);
            }
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { }
    }

    public static async Task StageAsync(
        ServiceProvider services,
        Guid id,
        bool inbox,
        CancellationToken token
    )
    {
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<QueueDatabase>();
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        if (inbox)
        {
            var intake = scope.ServiceProvider.GetRequiredService<IInbox<QueueDatabase>>();
            await intake.ReceiveAsync(
                "selected",
                new(
                    id,
                    "producer",
                    "owned.work",
                    1,
                    JsonSerializer.SerializeToElement(new { value = 7 })
                ),
                token
            );
        }
        else
            scope
                .ServiceProvider.GetRequiredService<IOutbox<QueueDatabase>>()
                .Enqueue(
                    new(
                        id,
                        "selected",
                        "owned.work",
                        1,
                        JsonSerializer.SerializeToElement(new { value = 7 })
                    )
                );

        await database.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
    }

    // Reviewed additive consumer migration. The owner chooses channels, deployment and removal.
    public const string InstallTriggers = """
        CREATE FUNCTION waking.queue_hint() RETURNS trigger LANGUAGE plpgsql SECURITY INVOKER
        SET search_path = pg_catalog, pg_temp AS $$
        BEGIN
            IF TG_TABLE_NAME = 'inbox' THEN PERFORM pg_catalog.pg_notify('proof_inbox', '');
            ELSE PERFORM pg_catalog.pg_notify('proof_outbox', ''); END IF;
            RETURN NULL;
        END $$;
        REVOKE ALL ON FUNCTION waking.queue_hint() FROM PUBLIC;
        CREATE TRIGGER queue_hint AFTER INSERT ON waking.inbox FOR EACH STATEMENT EXECUTE FUNCTION waking.queue_hint();
        CREATE TRIGGER queue_hint AFTER INSERT ON waking.outbox FOR EACH STATEMENT EXECUTE FUNCTION waking.queue_hint();
        """;

    public sealed class QueueDatabase(DbContextOptions<QueueDatabase> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigurePostgresInbox("waking", "inbox");
            modelBuilder.ConfigurePostgresOutbox("waking", "outbox");
            modelBuilder.Entity<Handled>().ToTable("handled", "waking").HasKey(x => x.Id);
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            this.ValidateInboxChanges();
            this.ValidateOutboxChanges();

            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override Task<int> SaveChangesAsync(
            bool acceptAllChangesOnSuccess,
            CancellationToken cancellationToken = default
        )
        {
            this.ValidateInboxChanges();
            this.ValidateOutboxChanges();

            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
    }

    public sealed class Handled
    {
        public Guid Id { get; set; }
        public int Value { get; set; }
    }

    public sealed class Probe
    {
        public ConcurrentQueue<Guid> Published { get; } = new();
        public bool FailHandler { get; set; }
        private int inboxPolls;
        private int outboxPolls;
        private int inboxCompletions;
        private int outboxCompletions;
        public int InboxPolls => Volatile.Read(ref inboxPolls);
        public int OutboxPolls => Volatile.Read(ref outboxPolls);
        public int InboxCompletions => Volatile.Read(ref inboxCompletions);
        public int OutboxCompletions => Volatile.Read(ref outboxCompletions);

        public void CountInboxPoll() => Interlocked.Increment(ref inboxPolls);

        public void CountOutboxPoll() => Interlocked.Increment(ref outboxPolls);

        public void CountInboxCompletion() => Interlocked.Increment(ref inboxCompletions);

        public void CountOutboxCompletion() => Interlocked.Increment(ref outboxCompletions);
    }

    private sealed class Handler(QueueDatabase database, Probe probe) : IInboxHandler<QueueDatabase>
    {
        public Task HandleAsync(IncomingMessage message, CancellationToken cancellationToken)
        {
            if (probe.FailHandler)
                throw new InvalidOperationException("owned handler failed");

            if (message.MessageName != "owned.work" || message.SchemaVersion != 1)
                throw new InvalidOperationException("Unknown owned work contract.");

            database.Add(
                new Handled
                {
                    Id = message.MessageId,
                    Value = message.Payload.GetProperty("value").GetInt32(),
                }
            );
            return Task.CompletedTask;
        }
    }

    private sealed class Publisher(Probe probe) : IMessagePublisher
    {
        public Task PublishAsync(
            OutgoingMessage message,
            CancellationToken cancellationToken = default
        )
        {
            probe.Published.Enqueue(message.MessageId);
            return Task.CompletedTask;
        }
    }
}
