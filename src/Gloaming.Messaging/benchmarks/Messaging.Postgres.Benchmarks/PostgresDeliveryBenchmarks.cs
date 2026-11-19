using System.Text.Json;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Engines;
using DotNet.Testcontainers.Configurations;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using Testcontainers.PostgreSql;

namespace Gloaming.Messaging.Benchmarks;

[SimpleJob(RunStrategy.Monitoring, launchCount: 1, warmupCount: 1, iterationCount: 5)]
[JsonExporterAttribute.Full]
public class PostgresDeliveryBenchmarks
{
    private const string Route = "delivery-benchmark";
    private const int BatchSize = 512;
    private PostgreSqlContainer? _postgres;
    private NpgsqlDataSource? _source;
    private ServiceProvider? _services;
    private Guid[] _submitted = [];

    [Params(1, 4)]
    public int Workers { get; set; }

    [Params(32, 1024)]
    public int RetainedMessages { get; set; }

    [GlobalSetup]
    public void StartDatabase() => StartAsync().GetAwaiter().GetResult();

    [GlobalCleanup]
    public void StopDatabase() => StopAsync().GetAwaiter().GetResult();

    [IterationSetup]
    public void PrepareBacklog() => PrepareAsync().GetAwaiter().GetResult();

    [IterationCleanup]
    public void VerifyBacklog() => VerifyAsync().GetAwaiter().GetResult();

    [Benchmark]
    public async Task DrainBacklog()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        await DrainAsync(deadline.Token);
    }

    private async Task StartAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var cancellation = deadline.Token;
        // Testcontainers tunes PostgreSQL for fast tests. Use server defaults for the baseline.
        _postgres = new PostgreSqlBuilder("postgres:18.6")
            .WithCommand(new OverwriteEnumerable<string>(["postgres"]))
            .Build();
        try
        {
            await _postgres.StartAsync(cancellation);
            _source = NpgsqlDataSource.Create(_postgres.GetConnectionString());
            await using var settings = _source.CreateCommand(
                "SELECT current_setting('server_version'), current_setting('fsync'), current_setting('synchronous_commit')"
            );
            await using var row = await settings.ExecuteReaderAsync(cancellation);
            if (
                !await row.ReadAsync(cancellation)
                || row.GetString(1) != "on"
                || row.GetString(2) != "on"
            )
                throw new InvalidOperationException(
                    "The benchmark requires PostgreSQL durability."
                );

            Console.WriteLine($"PostgreSQL {row.GetString(0)}, fsync=on, synchronous_commit=on");
            var registrations = new ServiceCollection();
            registrations.AddDbContext<DeliveryDatabase>(options => options.UseNpgsql(_source));
            registrations.AddPostgresOutbox<DeliveryDatabase>();
            registrations.AddPostgresOutboxDispatcher<DeliveryDatabase, LocalPublisher>(
                new(TimeSpan.FromSeconds(30), TimeSpan.Zero)
            );
            registrations.AddPostgresInbox<DeliveryDatabase>();
            registrations.AddPostgresInboxProcessor<DeliveryDatabase>(
                new(TimeSpan.FromSeconds(10))
            );
            registrations.AddInboxHandler<DeliveryDatabase, EffectHandler>(Route);
            _services = registrations.BuildServiceProvider(validateScopes: true);
            await using var scope = _services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DeliveryDatabase>();
            await db.Database.EnsureCreatedAsync(cancellation);
        }
        catch
        {
            await StopAsync();
            throw;
        }
    }

    private async Task StopAsync()
    {
        await using var postgres = _postgres;
        await using var source = _source;
        await using var services = _services;
        _services = null;
        _source = null;
        _postgres = null;
    }

    private async Task PrepareAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        var cancellation = deadline.Token;
        await using var reset = _source!.CreateCommand(
            "TRUNCATE delivery.inbox, delivery.outbox, delivery.handled"
        );
        await reset.ExecuteNonQueryAsync(cancellation);
        await EnqueueAsync(RetainedMessages, cancellation);
        await DrainAsync(cancellation);
        _submitted = await EnqueueAsync(BatchSize, cancellation);
    }

    private async Task<Guid[]> EnqueueAsync(int count, CancellationToken cancellation)
    {
        await using var scope = _services!.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DeliveryDatabase>();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellation);
        var outbox = scope.ServiceProvider.GetRequiredService<IOutbox<DeliveryDatabase>>();
        var submitted = new Guid[count];
        for (int index = 0; index < submitted.Length; index++)
        {
            Guid id = Guid.NewGuid();
            submitted[index] = id;
            outbox.Enqueue(
                OutgoingMessage.FromPayload(id, Route, Route, 1, index, JsonSerializerOptions.Web)
            );
        }

        await db.SaveChangesAsync(cancellation);
        await transaction.CommitAsync(cancellation);
        return submitted;
    }

    private Task DrainAsync(CancellationToken cancellation) =>
        Task.WhenAll(Enumerable.Range(0, Workers).Select(_ => RunWorkerAsync(cancellation)));

    private async Task RunWorkerAsync(CancellationToken cancellation)
    {
        var services = _services!;
        while (true)
        {
            cancellation.ThrowIfCancellationRequested();
            await using var dispatchScope = services.CreateAsyncScope();
            var dispatcher = dispatchScope.ServiceProvider.GetRequiredService<
                IOutboxDispatcher<DeliveryDatabase>
            >();
            var dispatched = await dispatcher.DispatchNextAsync(cancellation);
            await using var processScope = services.CreateAsyncScope();
            var processor = processScope.ServiceProvider.GetRequiredService<
                IInboxProcessor<DeliveryDatabase>
            >();
            var processed = await processor.ProcessNextAsync(Route, cancellation);
            if (
                dispatched == OutboxDispatchResult.NoWork
                && processed == InboxProcessingResult.NoWork
            )
                return;

            if (
                dispatched is not (OutboxDispatchResult.NoWork or OutboxDispatchResult.Published)
                || processed
                    is not (InboxProcessingResult.NoWork or InboxProcessingResult.Processed)
            )
                throw new InvalidOperationException(
                    $"Unexpected delivery results: {dispatched}, {processed}."
                );
        }
    }

    private async Task VerifyAsync()
    {
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        var cancellation = deadline.Token;
        await using var scope = _services!.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<DeliveryDatabase>();
        var backlog = await db.ReadPostgresOutboxBacklogAsync(Route, cancellation);
        var completed = await (
            from sent in db.Set<OutboxMessageRecord>()
            join received in db.Set<InboxMessageRecord>()
                on sent.MessageId equals received.MessageId
            join effect in db.Set<HandledItem>() on sent.MessageId equals effect.Id
            where
                _submitted.Contains(sent.MessageId)
                && sent.DispatchedAt != null
                && received.ProcessedAt != null
            select effect
        ).ToDictionaryAsync(effect => effect.Id, effect => effect.Value, cancellation);
        if (
            backlog.Pending != 0
            || backlog.DeadLetters != 0
            || completed.Count != BatchSize
            || await db.Set<HandledItem>().CountAsync(cancellation) != RetainedMessages + BatchSize
        )
            throw new InvalidOperationException(
                "The benchmark did not commit the expected delivery effects."
            );

        for (int index = 0; index < _submitted.Length; index++)
        {
            if (!completed.TryGetValue(_submitted[index], out int value) || value != index)
                throw new InvalidOperationException(
                    $"Missing or incorrect effect for message {index}."
                );
        }
    }

    private sealed class DeliveryDatabase(DbContextOptions<DeliveryDatabase> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigurePostgresInbox("delivery", "inbox");
            modelBuilder.ConfigurePostgresOutbox("delivery", "outbox");
            modelBuilder
                .Entity<HandledItem>()
                .ToTable("handled", "delivery")
                .HasKey(item => item.Id);
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

    private sealed class HandledItem
    {
        public Guid Id { get; set; }
        public int Value { get; set; }
    }

    private sealed class LocalPublisher(IServiceScopeFactory factory) : IMessagePublisher
    {
        public async Task PublishAsync(OutgoingMessage message, CancellationToken cancellationToken)
        {
            await using var scope = factory.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<DeliveryDatabase>();
            await using var transaction = await db.Database.BeginTransactionAsync(
                cancellationToken
            );
            var inbox = scope.ServiceProvider.GetRequiredService<IInbox<DeliveryDatabase>>();
            var incoming = new IncomingMessage(
                message.MessageId,
                Route,
                message.MessageName,
                message.SchemaVersion,
                message.Payload
            );
            if (
                await inbox.ReceiveAsync(Route, incoming, cancellationToken)
                != InboxReceiveResult.Queued
            )
                throw new InvalidOperationException(
                    "The benchmark expected to enqueue a new message."
                );

            await transaction.CommitAsync(cancellationToken);
        }
    }

    private sealed class EffectHandler(DeliveryDatabase database) : IInboxHandler<DeliveryDatabase>
    {
        public Task HandleAsync(IncomingMessage message, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            database.Add(
                new HandledItem { Id = message.MessageId, Value = message.Payload.GetInt32() }
            );
            return Task.CompletedTask;
        }
    }
}
