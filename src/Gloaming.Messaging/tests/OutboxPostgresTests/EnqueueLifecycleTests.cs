using Gloaming.Messaging.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Tests.Infrastructure;
using Npgsql;
using static Gloaming.Messaging.Tests.OutboxProof;

namespace Gloaming.Messaging.Tests;

[Collection("Messaging PostgreSQL")]
public sealed class EnqueueLifecycleTests(PostgreSqlFixture postgres)
{
    [Fact]
    public async Task SqlSaveFailureCanRetryTheOriginalMessageInTheSameNativeTransaction()
    {
        string connection = await DatabaseAsync(postgres);
        await SqlAsync(
            connection,
            "ALTER TABLE messages.pending ADD CONSTRAINT test_enqueue CHECK (message_name <> 'exports.render')"
        );
        var message = Message();
        Guid businessId = Guid.NewGuid();
        await using (var writer = Context(connection))
        {
            await using var transaction = await writer.Database.BeginTransactionAsync(Token);
            writer.Add(
                new BusinessState
                {
                    Id = businessId,
                    Amount = 3,
                    Version = 1,
                }
            );
            await writer.SaveChangesAsync(Token);
            new EfOutbox<OutboxConsumer>(writer).Enqueue(message);
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() =>
                writer.SaveChangesAsync(Token)
            );
            Assert.Equal(
                PostgresErrorCodes.CheckViolation,
                Assert.IsType<PostgresException>(failure.InnerException).SqlState
            );

            // EF rolls the failed save back to a savepoint. Fix the injected fault, not the envelope.
            await writer.Database.ExecuteSqlRawAsync(
                "ALTER TABLE messages.pending DROP CONSTRAINT test_enqueue",
                Token
            );
            writer.SaveChanges();
            await transaction.CommitAsync(Token);
        }
        await using var fresh = Context(connection);
        Assert.Equal(
            businessId,
            Assert.Single(await fresh.Set<BusinessState>().ToArrayAsync(Token)).Id
        );
        Assert.Equal(
            message.MessageId,
            Assert.Single(await fresh.Set<OutboxMessageRecord>().ToArrayAsync(Token)).MessageId
        );
    }

    [Fact]
    public async Task DetachedRowCanReturnToItsOwningContextButCannotBeSavedByAnotherContext()
    {
        string connection = await DatabaseAsync(postgres);
        var message = Message();
        await using (var writer = Context(connection))
        {
            await using var transaction = await writer.Database.BeginTransactionAsync(Token);
            new EfOutbox<OutboxConsumer>(writer).Enqueue(message);
            var row = writer.ChangeTracker.Entries<OutboxMessageRecord>().Single().Entity;
            writer.ChangeTracker.Clear();
            await using (var other = Context(connection))
            {
                await using var otherTransaction = await other.Database.BeginTransactionAsync(
                    Token
                );
                other.Add(row);
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    other.SaveChangesAsync(Token)
                );
                other.ChangeTracker.Clear();
                await otherTransaction.RollbackAsync(Token);
            }
            // The original row is still referenced, so its enqueue evidence remains available.
            writer.Add(row);
            await writer.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }
        await using var fresh = Context(connection);
        Assert.Equal(
            message.MessageId,
            Assert.Single(await fresh.Set<OutboxMessageRecord>().ToArrayAsync(Token)).MessageId
        );
    }

    [Fact]
    public async Task ReusedPooledContextCannotAdoptWorkFromAnEarlierLeaseAndCanEnqueueFreshWork()
    {
        string connection = await DatabaseAsync(postgres);
        var services = new ServiceCollection();
        services.AddDbContextPool<OutboxConsumer>(
            options => options.UseNpgsql(connection),
            poolSize: 1
        );
        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true }
        );
        OutboxConsumer original;
        DbContextId originalLease;
        OutboxMessageRecord abandoned;
        await using (var scope = provider.CreateAsyncScope())
        {
            original = scope.ServiceProvider.GetRequiredService<OutboxConsumer>();
            originalLease = original.ContextId;
            await using var transaction = await original.Database.BeginTransactionAsync(Token);
            new EfOutbox<OutboxConsumer>(original).Enqueue(Message());
            abandoned = original.ChangeTracker.Entries<OutboxMessageRecord>().Single().Entity;
            await transaction.RollbackAsync(Token);
        }
        var accepted = Message();
        await using (var scope = provider.CreateAsyncScope())
        {
            var reused = scope.ServiceProvider.GetRequiredService<OutboxConsumer>();
            Assert.Same(original, reused);
            Assert.NotEqual(originalLease, reused.ContextId);
            await using var transaction = await reused.Database.BeginTransactionAsync(Token);
            reused.Add(abandoned);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                reused.SaveChangesAsync(Token)
            );
            reused.ChangeTracker.Clear();
            new EfOutbox<OutboxConsumer>(reused).Enqueue(accepted);
            await reused.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }
        await using var fresh = Context(connection);
        Assert.Equal(
            accepted.MessageId,
            Assert.Single(await fresh.Set<OutboxMessageRecord>().ToArrayAsync(Token)).MessageId
        );
    }
}
