using System.Data;
using Gloaming.Messaging.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using ModulithFoundry.Tests.Infrastructure;
using Npgsql;
using static Gloaming.Messaging.Tests.OutboxProof;

namespace Gloaming.Messaging.Tests;

[Collection("Messaging PostgreSQL")]
public sealed class ProducerTests(PostgreSqlFixture postgres)
{
    [Theory]
    [InlineData("CorrelationId")]
    [InlineData("CausationId")]
    public async Task RetainedReplyMetadataCannotBeChangedAfterEnqueue(string property)
    {
        string connection = await DatabaseAsync(postgres);
        await using var writer = Context(connection);
        await using var transaction = await writer.Database.BeginTransactionAsync(Token);
        new EfOutbox<OutboxConsumer>(writer).Enqueue(Message());
        writer
            .ChangeTracker.Entries<OutboxMessageRecord>()
            .Single()
            .Property(property)
            .CurrentValue = "changed";
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.SaveChangesAsync(Token));
        await transaction.RollbackAsync(Token);
        await using var fresh = Context(connection);
        Assert.Empty(await fresh.Set<OutboxMessageRecord>().ToArrayAsync(Token));
    }

    [Fact]
    public async Task CancelledLaterSaveRollsBackEarlierBusinessSqlAndFreshContextCanRetry()
    {
        string connection = await DatabaseAsync(postgres);
        await using (var writer = Context(connection))
        {
            await using var transaction = await writer.Database.BeginTransactionAsync(Token);
            writer.Add(
                new BusinessState
                {
                    Id = Guid.NewGuid(),
                    Amount = 3,
                    Version = 1,
                }
            );
            await writer.SaveChangesAsync(Token);
            new EfOutbox<OutboxConsumer>(writer).Enqueue(Message());
            using var cancelled = new CancellationTokenSource();
            await cancelled.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
                writer.SaveChangesAsync(cancelled.Token)
            );
            await transaction.RollbackAsync(Token);
        }
        await using (var fresh = Context(connection))
        {
            Assert.Empty(await fresh.Set<BusinessState>().ToArrayAsync(Token));
            Assert.Empty(await fresh.Set<OutboxMessageRecord>().ToArrayAsync(Token));
        }
        await EnqueueAsync(connection);
        await using var recovered = Context(connection);
        Assert.Single(await recovered.Set<OutboxMessageRecord>().ToArrayAsync(Token));
    }

    [Theory]
    [InlineData(IsolationLevel.ReadCommitted)]
    [InlineData(IsolationLevel.RepeatableRead)]
    [InlineData(IsolationLevel.Serializable)]
    public async Task BusinessAndOutgoingWorkCommitOrRollbackInTheRequestedNativeIsolation(
        IsolationLevel isolation
    )
    {
        string connection = await DatabaseAsync(postgres);
        Guid id = Guid.NewGuid();
        await using (var writer = Context(connection))
        {
            await using var transaction = await writer.Database.BeginTransactionAsync(
                isolation,
                Token
            );
            writer.Add(
                new BusinessState
                {
                    Id = id,
                    Version = 1,
                    Amount = 9,
                }
            );
            new EfOutbox<OutboxConsumer>(writer).Enqueue(Message());
            await writer.SaveChangesAsync(Token);
            await using var observer = Context(connection);
            Assert.Empty(await observer.Set<BusinessState>().ToArrayAsync(Token));
            Assert.Empty(await observer.Set<OutboxMessageRecord>().ToArrayAsync(Token));
            Assert.Equal(isolation, transaction.GetDbTransaction().IsolationLevel);
            await transaction.CommitAsync(Token);
        }
        await using (var writer = Context(connection))
        {
            await using var transaction = await writer.Database.BeginTransactionAsync(
                isolation,
                Token
            );
            writer.Add(
                new BusinessState
                {
                    Id = Guid.NewGuid(),
                    Version = 1,
                    Amount = 10,
                }
            );
            new EfOutbox<OutboxConsumer>(writer).Enqueue(Message());
            writer.SaveChanges();
            await transaction.RollbackAsync(Token);
        }
        await using var fresh = Context(connection);
        Assert.Equal(id, Assert.Single(await fresh.Set<BusinessState>().ToArrayAsync(Token)).Id);
        var outgoing = Assert.Single(await fresh.Set<OutboxMessageRecord>().ToArrayAsync(Token));
        Assert.Equal("résumé", outgoing.Payload.GetProperty("name").GetString());
        Assert.Equal("tenant-alpha", outgoing.TenantKey);
        Assert.NotEqual(default, outgoing.QueuedAt);
        Assert.NotEqual(default, outgoing.AvailableAt);
    }

    [Fact]
    public async Task ChangedTransactionAndTrackedEnvelopeMutationAreRejectedBeforeSave()
    {
        string connection = await DatabaseAsync(postgres);
        await using var writer = Context(connection);
        var outbox = new EfOutbox<OutboxConsumer>(writer);
        Assert.Throws<InvalidOperationException>(() => outbox.Enqueue(Message()));
        await using (var original = await writer.Database.BeginTransactionAsync(Token))
        {
            outbox.Enqueue(Message());
            await original.RollbackAsync(Token);
        }
        await using var replacement = await writer.Database.BeginTransactionAsync(Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() => writer.SaveChangesAsync(Token));
        writer.ChangeTracker.Clear();
        outbox.Enqueue(Message());
        writer
            .ChangeTracker.Entries<OutboxMessageRecord>()
            .Single()
            .Property(row => row.RouteKey)
            .CurrentValue = "changed";
        Assert.Throws<InvalidOperationException>(() => writer.SaveChanges());
        await replacement.RollbackAsync(Token);
        await using var fresh = Context(connection);
        Assert.Empty(await fresh.Set<OutboxMessageRecord>().ToArrayAsync(Token));
    }

    [Fact]
    public async Task LaterOutboxFailureRollsBackAnEarlierBusinessSaveAndFreshScopeCanRecover()
    {
        string connection = await DatabaseAsync(postgres);
        Guid id = Guid.NewGuid();
        var original = await EnqueueAsync(connection);
        await using (var writer = Context(connection))
        {
            await using var transaction = await writer.Database.BeginTransactionAsync(Token);
            writer.Add(
                new BusinessState
                {
                    Id = id,
                    Amount = 4,
                    Version = 1,
                }
            );
            await writer.SaveChangesAsync(Token);
            new EfOutbox<OutboxConsumer>(writer).Enqueue(Message(original.MessageId));
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() =>
                writer.SaveChangesAsync(Token)
            );
            Assert.Equal(
                PostgresErrorCodes.UniqueViolation,
                Assert.IsType<PostgresException>(failure.InnerException).SqlState
            );
            await transaction.RollbackAsync(Token);
        }
        await using (var fresh = Context(connection))
        {
            Assert.Empty(await fresh.Set<BusinessState>().ToArrayAsync(Token));
            await using var transaction = await fresh.Database.BeginTransactionAsync(Token);
            fresh.Add(
                new BusinessState
                {
                    Id = id,
                    Amount = 4,
                    Version = 1,
                }
            );
            new EfOutbox<OutboxConsumer>(fresh).Enqueue(Message());
            await fresh.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }
        await using var recovered = Context(connection);
        Assert.Single(await recovered.Set<BusinessState>().ToArrayAsync(Token));
        Assert.Equal(2, await recovered.Set<OutboxMessageRecord>().CountAsync(Token));
    }

    [Theory]
    [InlineData(IsolationLevel.ReadCommitted)]
    [InlineData(IsolationLevel.Serializable)]
    public async Task CompetingBusinessDecisionsCannotCommitTheirLosingOutgoingWork(
        IsolationLevel isolation
    )
    {
        string connection = await DatabaseAsync(postgres);
        Guid id = Guid.NewGuid();
        await using (var setup = Context(connection))
        {
            setup.Add(
                new BusinessState
                {
                    Id = id,
                    Amount = 2,
                    Version = 1,
                }
            );
            await setup.SaveChangesAsync(Token);
        }
        await using var winner = Context(connection);
        await using var loser = Context(connection);
        await using var first = await winner.Database.BeginTransactionAsync(isolation, Token);
        await using var second = await loser.Database.BeginTransactionAsync(isolation, Token);
        var winningState = await winner.Set<BusinessState>().SingleAsync(Token);
        var losingState = await loser.Set<BusinessState>().SingleAsync(Token);
        winningState.Amount = 1;
        losingState.Amount = 0;
        winningState.Version++;
        losingState.Version++;
        new EfOutbox<OutboxConsumer>(winner).Enqueue(Message());
        new EfOutbox<OutboxConsumer>(loser).Enqueue(Message());
        await winner.SaveChangesAsync(Token);
        await first.CommitAsync(Token);
        if (isolation == IsolationLevel.ReadCommitted)
            await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() =>
                loser.SaveChangesAsync(Token)
            );
        else
        {
            var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                loser.SaveChangesAsync(Token)
            );
            var update = Assert.IsType<DbUpdateException>(failure.InnerException);
            Assert.Equal(
                PostgresErrorCodes.SerializationFailure,
                Assert.IsType<PostgresException>(update.InnerException).SqlState
            );
        }
        await second.RollbackAsync(Token);
        await using var fresh = Context(connection);
        Assert.Equal(1, (await fresh.Set<BusinessState>().SingleAsync(Token)).Amount);
        Assert.Single(await fresh.Set<OutboxMessageRecord>().ToArrayAsync(Token));
    }
}
