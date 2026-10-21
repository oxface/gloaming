using System.Collections.Concurrent;
using System.Transactions;
using Gloaming.Messaging.EntityFrameworkCore;
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
public sealed class RegistrationTests(PostgreSqlFixture postgres)
{
    [Fact]
    public async Task PolicyAndDeadLetterRegistrationAreIndependentScopedAndRejectConflictingPolicies()
    {
        var services = new ServiceCollection();
        services.AddDbContext<OutboxConsumer>(options =>
            options.UseNpgsql("Host=not-opened;Database=unused;Username=unused")
        );
        services.AddOutboxFailurePolicy<OutboxConsumer, RegisteredPolicy>();
        Assert.Throws<InvalidOperationException>(() =>
            services.AddOutboxFailurePolicy<OutboxConsumer, RegisteredPolicy>()
        );
        services.AddPostgresOutboxDeadLetters<OutboxConsumer>();
        services.AddPostgresOutboxDeadLetters<OutboxConsumer>();
        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
        );
        await using var first = provider.CreateAsyncScope();
        await using var second = provider.CreateAsyncScope();
        Assert.Same(
            first.ServiceProvider.GetRequiredService<IOutboxDeadLetters<OutboxConsumer>>(),
            first.ServiceProvider.GetRequiredService<IOutboxDeadLetters<OutboxConsumer>>()
        );
        Assert.NotSame(
            first.ServiceProvider.GetRequiredService<IOutboxDeadLetters<OutboxConsumer>>(),
            second.ServiceProvider.GetRequiredService<IOutboxDeadLetters<OutboxConsumer>>()
        );
        Assert.NotSame(
            first.ServiceProvider.GetRequiredService<IOutboxFailurePolicy<OutboxConsumer>>(),
            second.ServiceProvider.GetRequiredService<IOutboxFailurePolicy<OutboxConsumer>>()
        );
        Assert.Null(first.ServiceProvider.GetService<IOutboxDispatcher<OutboxConsumer>>());
        Assert.Null(first.ServiceProvider.GetService<IOutbox<OutboxConsumer>>());
        Assert.Empty(provider.GetServices<IHostedService>());
    }

    public sealed class RegisteredPolicy : IOutboxFailurePolicy<OutboxConsumer>
    {
        public OutboxFailureDecision Decide(OutboxFailure failure) =>
            new OutboxFailureDecision.DeadLetter("review");
    }

    [Fact]
    public async Task TypedModuleRegistrationsAndQuotedTablesRemainIndependent()
    {
        string connection = await DatabaseAsync(postgres);
        await using (
            var second = new OtherModule(
                new DbContextOptionsBuilder<OtherModule>().UseNpgsql(connection).Options
            )
        )
            await second.Database.ExecuteSqlRawAsync(second.Database.GenerateCreateScript(), Token);
        var services = new ServiceCollection();
        services.AddDbContext<OutboxConsumer>(options => options.UseNpgsql(connection));
        services.AddDbContext<OtherModule>(options => options.UseNpgsql(connection));
        services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        var firstPublisher = new RecordingPublisher();
        var secondPublisher = new OtherPublisher();
        services.AddScoped(_ => firstPublisher);
        services.AddScoped(_ => secondPublisher);
        services.AddPostgresOutbox<OutboxConsumer>();
        services.AddPostgresOutbox<OtherModule>();
        services.AddPostgresOutboxDispatcher<OutboxConsumer, RecordingPublisher>(
            new(TimeSpan.FromSeconds(30), TimeSpan.Zero)
        );
        services.AddPostgresOutboxDispatcher<OtherModule, OtherPublisher>(
            new(TimeSpan.FromMinutes(1), TimeSpan.FromSeconds(2))
        );
        services.AddOutboxWorker<OutboxConsumer>(
            new(TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(20))
        );
        services.AddOutboxWorker<OtherModule>(
            new(TimeSpan.FromMilliseconds(20), TimeSpan.FromMilliseconds(30))
        );
        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
        );
        var firstMessage = Message();
        var secondMessage = Message();
        await using (var scope = provider.CreateAsyncScope())
        {
            Assert.Null(scope.ServiceProvider.GetService<IMessagePublisher>());
            var first = scope.ServiceProvider.GetRequiredService<OutboxConsumer>();
            await using (var transaction = await first.Database.BeginTransactionAsync(Token))
            {
                scope
                    .ServiceProvider.GetRequiredService<IOutbox<OutboxConsumer>>()
                    .Enqueue(firstMessage);
                await first.SaveChangesAsync(Token);
                await transaction.CommitAsync(Token);
            }
            var second = scope.ServiceProvider.GetRequiredService<OtherModule>();
            await using var otherTransaction = await second.Database.BeginTransactionAsync(Token);
            scope.ServiceProvider.GetRequiredService<IOutbox<OtherModule>>().Enqueue(secondMessage);
            await second.SaveChangesAsync(Token);
            await otherTransaction.CommitAsync(Token);
        }
        await using (var scope = provider.CreateAsyncScope())
        {
            Assert.Equal(
                OutboxDispatchResult.Published,
                await scope
                    .ServiceProvider.GetRequiredService<IOutboxDispatcher<OtherModule>>()
                    .DispatchNextAsync(Token)
            );
            Assert.Empty(firstPublisher.Messages);
            Assert.Equal(
                secondMessage.MessageId,
                Assert.Single(secondPublisher.Messages).MessageId
            );
            Assert.Equal(
                OutboxDispatchResult.Published,
                await scope
                    .ServiceProvider.GetRequiredService<IOutboxDispatcher<OutboxConsumer>>()
                    .DispatchNextAsync(Token)
            );
            Assert.Equal(firstMessage.MessageId, Assert.Single(firstPublisher.Messages).MessageId);
        }
        // The same independently bound dispatchers also work through two optional host registrations.
        await using (var scope = provider.CreateAsyncScope())
        {
            var first = scope.ServiceProvider.GetRequiredService<OutboxConsumer>();
            await using var firstTransaction = await first.Database.BeginTransactionAsync(Token);
            scope.ServiceProvider.GetRequiredService<IOutbox<OutboxConsumer>>().Enqueue(Message());
            await first.SaveChangesAsync(Token);
            await firstTransaction.CommitAsync(Token);
            var second = scope.ServiceProvider.GetRequiredService<OtherModule>();
            await using var secondTransaction = await second.Database.BeginTransactionAsync(Token);
            scope.ServiceProvider.GetRequiredService<IOutbox<OtherModule>>().Enqueue(Message());
            await second.SaveChangesAsync(Token);
            await secondTransaction.CommitAsync(Token);
        }
        var workers = provider.GetServices<IHostedService>().ToArray();
        Assert.Equal(2, workers.Length);
        try
        {
            foreach (var worker in workers)
                await worker.StartAsync(Token);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            while (true)
            {
                await using var scope = provider.CreateAsyncScope();
                int first = await scope
                    .ServiceProvider.GetRequiredService<OutboxConsumer>()
                    .Set<OutboxMessageRecord>()
                    .CountAsync(row => row.DispatchedAt != null, timeout.Token);
                int second = await scope
                    .ServiceProvider.GetRequiredService<OtherModule>()
                    .Set<OutboxMessageRecord>()
                    .CountAsync(row => row.DispatchedAt != null, timeout.Token);
                if (first == 2 && second == 2)
                    break;
                await Task.Delay(10, timeout.Token);
            }
        }
        finally
        {
            foreach (var worker in workers)
                await worker.StopAsync(Token).WaitAsync(TimeSpan.FromSeconds(10), Token);
        }
        Assert.Equal(2, firstPublisher.Messages.Count);
        Assert.Equal(2, secondPublisher.Messages.Count);
    }

    [Fact]
    public async Task DispatchRejectsProducerAndAmbientTransactionsBeforePublication()
    {
        string connection = await DatabaseAsync(postgres);
        await EnqueueAsync(connection);
        await using var context = Context(connection);
        var publisher = new RecordingPublisher();
        await using (var transaction = await context.Database.BeginTransactionAsync(Token))
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Dispatcher(context, publisher).DispatchNextAsync(Token)
            );
        using (var ambient = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Dispatcher(context, publisher).DispatchNextAsync(Token)
            );
        context.Add(
            new BusinessState
            {
                Id = Guid.NewGuid(),
                Amount = 1,
                Version = 1,
            }
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Dispatcher(context, publisher).DispatchNextAsync(Token)
        );
        Assert.Empty(publisher.Messages);
    }

    [Fact]
    public void MissingOrIncompatibleModelAndInvalidOptionsFailWithoutOpeningTheDatabase()
    {
        using var context = new MissingModel(
            new DbContextOptionsBuilder<MissingModel>()
                .UseNpgsql("Host=not-opened;Database=not-opened")
                .Options
        );
        Assert.Throws<InvalidOperationException>(() =>
            new PostgresOutboxDispatcher<MissingModel>(
                context,
                new RecordingPublisher(),
                new(TimeSpan.FromSeconds(1), TimeSpan.Zero)
            )
        );
        using var unmapped = new MissingModel(new DbContextOptionsBuilder<MissingModel>().Options);
        Assert.Throws<InvalidOperationException>(() =>
            new PostgresOutboxDispatcher<MissingModel>(
                unmapped,
                new RecordingPublisher(),
                new(TimeSpan.FromSeconds(1), TimeSpan.Zero)
            )
        );
        var services = new ServiceCollection();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            services.AddPostgresOutboxDispatcher<OutboxConsumer, RecordingPublisher>(
                new(TimeSpan.Zero, TimeSpan.Zero)
            )
        );
    }
}

public sealed class MissingModel(DbContextOptions<MissingModel> options) : DbContext(options);

public sealed class OtherModule(DbContextOptions<OtherModule> options) : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ConfigurePostgresOutbox("Other Module", "Quoted Queue");

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default
    )
    {
        this.ValidateOutboxChanges();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}

public sealed class OtherPublisher : IMessagePublisher
{
    public ConcurrentQueue<OutgoingMessage> Messages { get; } = new();

    public Task PublishAsync(OutgoingMessage message, CancellationToken cancellationToken)
    {
        Messages.Enqueue(message);
        return Task.CompletedTask;
    }
}
