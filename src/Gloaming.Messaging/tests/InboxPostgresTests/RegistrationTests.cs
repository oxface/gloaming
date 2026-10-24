using System.Transactions;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using static Gloaming.Messaging.InboxTests.InboxProof;

namespace Gloaming.Messaging.InboxTests;

public sealed class RegistrationTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task SameSubscriptionAndIdentityStayIndependentAcrossTypedContextsAndInboxOnlyNeedsNoPublisher()
    {
        string connection = await DatabaseAsync(fixture);
        var services = Services(connection);
        services.AddDbContext<InboxOnlyConsumer>(options => options.UseNpgsql(connection));
        services.AddPostgresInbox<InboxOnlyConsumer>();
        services.AddPostgresInboxProcessor<InboxOnlyConsumer>(new(TimeSpan.FromSeconds(1)));
        services.AddInboxHandler<InboxOnlyConsumer, OtherHandler>("render");
        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
        );
        await using (var setup = provider.CreateAsyncScope())
        {
            var other = setup.ServiceProvider.GetRequiredService<InboxOnlyConsumer>();
            await other.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(Token);
            Assert.Null(other.Model.FindEntityType(typeof(OutboxMessageRecord)));
            Assert.Null(setup.ServiceProvider.GetService<IOutbox<InboxOnlyConsumer>>());
            Assert.Null(setup.ServiceProvider.GetService<IOutboxDispatcher<InboxOnlyConsumer>>());
            Assert.Null(setup.ServiceProvider.GetService<IMessagePublisher>());
            Assert.Null(setup.ServiceProvider.GetService<DbContext>());
        }
        var message = Message();
        await ReceiveAsync(provider, message);
        await using (var intake = provider.CreateAsyncScope())
        {
            var other = intake.ServiceProvider.GetRequiredService<InboxOnlyConsumer>();
            await using var transaction = await other.Database.BeginTransactionAsync(Token);
            Assert.Equal(
                InboxReceiveResult.Queued,
                await intake
                    .ServiceProvider.GetRequiredService<IInbox<InboxOnlyConsumer>>()
                    .ReceiveAsync("render", message, Token)
            );
            await transaction.CommitAsync(Token);
        }
        Assert.Equal(InboxProcessingResult.Processed, await ProcessAsync(provider));
        await using (var processing = provider.CreateAsyncScope())
            Assert.Equal(
                InboxProcessingResult.Processed,
                await processing
                    .ServiceProvider.GetRequiredService<IInboxProcessor<InboxOnlyConsumer>>()
                    .ProcessNextAsync("render", Token)
            );
        await using var read = provider.CreateAsyncScope();
        var first = read.ServiceProvider.GetRequiredService<InboxConsumer>();
        var second = read.ServiceProvider.GetRequiredService<InboxOnlyConsumer>();
        Assert.Equal(7, (await first.Set<HandledItem>().SingleAsync(Token)).Value);
        Assert.Equal(42, (await second.Set<HandledItem>().SingleAsync(Token)).Value);
        Assert.NotNull((await first.Set<InboxMessageRecord>().SingleAsync(Token)).ProcessedAt);
        Assert.NotNull((await second.Set<InboxMessageRecord>().SingleAsync(Token)).ProcessedAt);
    }

    [Fact]
    public void MissingProviderOrAlteredMappingFailsBeforeOpeningAnyDatabaseConnection()
    {
        var services = new ServiceCollection();
        services.AddScoped(_ => new UnsupportedConsumer(
            new DbContextOptions<UnsupportedConsumer>()
        ));
        services.AddPostgresInbox<UnsupportedConsumer>();
        using (var provider = services.BuildServiceProvider())
        using (var scope = provider.CreateScope())
            Assert.Throws<InvalidOperationException>(() =>
                scope.ServiceProvider.GetRequiredService<IInbox<UnsupportedConsumer>>()
            );
        services = new ServiceCollection();
        services.AddDbContext<AlteredConsumer>(options =>
            options.UseNpgsql("Host=not-opened;Database=not-used;Username=not-used")
        );
        services.AddPostgresInbox<AlteredConsumer>();
        using var altered = services.BuildServiceProvider();
        using var operation = altered.CreateScope();
        Assert.Throws<InvalidOperationException>(() =>
            operation.ServiceProvider.GetRequiredService<IInbox<AlteredConsumer>>()
        );
    }

    [Fact]
    public async Task AmbientTransactionsRejectIntakeAndProcessingBeforeAnyWork()
    {
        string connection = await DatabaseAsync(fixture);
        await using var provider = Provider(connection);
        await using var scope = provider.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<InboxConsumer>();
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        using (var ambient = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                scope
                    .ServiceProvider.GetRequiredService<IInbox<InboxConsumer>>()
                    .ReceiveAsync("render", Message(), Token)
            );
            await using var fresh = provider.CreateAsyncScope();
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                fresh
                    .ServiceProvider.GetRequiredService<IInboxProcessor<InboxConsumer>>()
                    .ProcessNextAsync("render", Token)
            );
        }
        await transaction.RollbackAsync(Token);
    }

    [Theory]
    [InlineData(EntityState.Added)]
    [InlineData(EntityState.Deleted)]
    public async Task ProvidedRecordsCannotBeInsertedOrDeletedThroughTrackedSaves(EntityState state)
    {
        string connection = await DatabaseAsync(fixture);
        await using var provider = Provider(connection);
        await ReceiveAsync(provider, Message());
        await using (var database = Context(connection))
        {
            var row = await database.Set<InboxMessageRecord>().SingleAsync(Token);
            database.Entry(row).State = state;
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                database.SaveChangesAsync(Token)
            );
        }
        await using var fresh = Context(connection);
        Assert.Single(await fresh.Set<InboxMessageRecord>().ToArrayAsync(Token));
    }

    public sealed class InboxOnlyConsumer(DbContextOptions<InboxOnlyConsumer> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigurePostgresInbox("second", "inbox");
            modelBuilder.Entity<HandledItem>().ToTable("handled", "second").HasKey(row => row.Id);
        }

        public override Task<int> SaveChangesAsync(
            bool acceptAllChangesOnSuccess,
            CancellationToken cancellationToken = default
        )
        {
            this.ValidateInboxChanges();
            return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
    }

    public sealed class OtherHandler(InboxOnlyConsumer database) : IInboxHandler<InboxOnlyConsumer>
    {
        public Task HandleAsync(IncomingMessage message, CancellationToken cancellationToken)
        {
            database.Add(new HandledItem { Id = message.MessageId, Value = 42 });
            return Task.CompletedTask;
        }
    }

    public sealed class UnsupportedConsumer(DbContextOptions<UnsupportedConsumer> options)
        : DbContext(options);

    public sealed class AlteredConsumer(DbContextOptions<AlteredConsumer> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder
                .ConfigurePostgresInbox("bad", "inbox")
                .Property(row => row.Payload)
                .HasColumnType("text");
        }
    }
}
