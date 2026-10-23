using System.Text.Json;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using static Gloaming.Messaging.Tests.OutboxProof;

namespace Gloaming.Messaging.Tests;

[Collection("Messaging PostgreSQL")]
public sealed class DispatchTests(PostgreSqlFixture postgres)
{
    [Fact]
    public async Task ClaimIgnoresUncommittedWorkAndPublishesOnlyAfterTheProducerCommits()
    {
        string connection = await DatabaseAsync(postgres);
        var message = Message();
        var publisher = new RecordingPublisher();
        await using var writer = Context(connection);
        await using var transaction = await writer.Database.BeginTransactionAsync(Token);
        new EfOutbox<OutboxConsumer>(writer).Enqueue(message);
        await writer.SaveChangesAsync(Token);
        await using (var before = Context(connection))
            Assert.Equal(
                OutboxDispatchResult.NoWork,
                await Dispatcher(before, publisher).DispatchNextAsync(Token)
            );
        Assert.Empty(publisher.Messages);
        await transaction.CommitAsync(Token);
        await using var dispatcherContext = Context(connection);
        publisher.OnPublish = (_, _) =>
        {
            Assert.Null(dispatcherContext.Database.CurrentTransaction);
            return Task.CompletedTask;
        };
        Assert.Equal(
            OutboxDispatchResult.Published,
            await Dispatcher(dispatcherContext, publisher).DispatchNextAsync(Token)
        );
        var delivered = Assert.Single(publisher.Messages);
        Assert.Equal(message.MessageId, delivered.MessageId);
        Assert.True(JsonElement.DeepEquals(message.Payload, delivered.Payload));
        Assert.Equal(message.TenantKey, delivered.TenantKey);
        Assert.Equal(message.CorrelationId, delivered.CorrelationId);
        Assert.Equal(message.CausationId, delivered.CausationId);
        Assert.Equal(
            OutboxDispatchResult.NoWork,
            await Dispatcher(dispatcherContext, publisher).DispatchNextAsync(Token)
        );
        var saved = await dispatcherContext
            .Set<OutboxMessageRecord>()
            .AsNoTracking()
            .SingleAsync(Token);
        Assert.NotNull(saved.DispatchedAt);
        Assert.Null(saved.LeaseToken);
        Assert.Null(saved.LeaseUntil);
        Assert.Equal(1, saved.Attempts);
    }

    [Fact]
    public async Task RowLockedByAnotherTransactionIsSkippedWithoutWaiting()
    {
        string connection = await DatabaseAsync(postgres);
        await EnqueueAsync(connection);
        await using var lockOwner = new NpgsqlConnection(connection);
        await lockOwner.OpenAsync(Token);
        await using var locked = await lockOwner.BeginTransactionAsync(Token);
        await using var command = new NpgsqlCommand(
            "SELECT message_id FROM messages.pending FOR UPDATE",
            lockOwner,
            locked
        );
        await command.ExecuteScalarAsync(Token);
        await using var context = Context(connection);
        var publisher = new RecordingPublisher();
        Assert.Equal(
            OutboxDispatchResult.NoWork,
            await Dispatcher(context, publisher)
                .DispatchNextAsync(Token)
                .WaitAsync(TimeSpan.FromSeconds(5), Token)
        );
        Assert.Empty(publisher.Messages);
        await locked.RollbackAsync(Token);
        Assert.Equal(
            OutboxDispatchResult.Published,
            await Dispatcher(context, publisher).DispatchNextAsync(Token)
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActiveClaimIsExclusiveAndAnExpiredPublisherCannotCompleteOrDeferItsSuccessor(
        bool oldFails
    )
    {
        string connection = await DatabaseAsync(postgres);
        var message = await EnqueueAsync(connection);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var old = new RecordingPublisher
        {
            OnPublish = async (_, cancellation) =>
            {
                entered.SetResult();
                await release.Task.WaitAsync(cancellation);
                if (oldFails)
                    throw new IOException("late transport failure");
            },
        };
        await using var oldContext = Context(connection);
        var oldDispatch = Dispatcher(oldContext, old).DispatchNextAsync(Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
        await using var currentContext = Context(connection);
        var current = new RecordingPublisher();
        Assert.Equal(
            OutboxDispatchResult.NoWork,
            await Dispatcher(currentContext, current).DispatchNextAsync(Token)
        );
        await ExpireAsync(connection);
        Assert.Equal(
            OutboxDispatchResult.Published,
            await Dispatcher(currentContext, current).DispatchNextAsync(Token)
        );
        release.SetResult();
        if (oldFails)
            await Assert.ThrowsAsync<IOException>(() => oldDispatch);
        else
            Assert.Equal(OutboxDispatchResult.ClaimLost, await oldDispatch);
        Assert.Equal(message.MessageId, Assert.Single(old.Messages).MessageId);
        Assert.Equal(message.MessageId, Assert.Single(current.Messages).MessageId);
        await using var fresh = Context(connection);
        var saved = await fresh.Set<OutboxMessageRecord>().SingleAsync(Token);
        Assert.NotNull(saved.DispatchedAt);
        Assert.Null(saved.LeaseToken);
        Assert.Equal(2, saved.Attempts);
    }

    [Fact]
    public async Task ExpiredClaimCannotCompleteEvenBeforeAnotherDispatcherTakesOver()
    {
        string connection = await DatabaseAsync(postgres);
        await EnqueueAsync(connection);
        var publisher = new RecordingPublisher { OnPublish = (_, _) => ExpireAsync(connection) };
        await using var context = Context(connection);
        Assert.Equal(
            OutboxDispatchResult.ClaimLost,
            await Dispatcher(context, publisher).DispatchNextAsync(Token)
        );
        await using var fresh = Context(connection);
        var row = await fresh.Set<OutboxMessageRecord>().SingleAsync(Token);
        Assert.Null(row.DispatchedAt);
        Assert.NotNull(row.LeaseToken);
    }

    [Fact]
    public async Task PublicationFailureUsesDatabaseRetryTimeAndPropagatesItsOriginalError()
    {
        string connection = await DatabaseAsync(postgres);
        await EnqueueAsync(connection);
        var expected = new IOException("publisher unavailable");
        var failed = new RecordingPublisher { OnPublish = (_, _) => Task.FromException(expected) };
        await using (var context = Context(connection))
            Assert.Same(
                expected,
                await Assert.ThrowsAsync<IOException>(() =>
                    Dispatcher(context, failed, TimeSpan.FromMinutes(2)).DispatchNextAsync(Token)
                )
            );
        await using var fresh = Context(connection);
        var row = await fresh.Set<OutboxMessageRecord>().SingleAsync(Token);
        Assert.Null(row.DispatchedAt);
        Assert.Null(row.LeaseToken);
        Assert.True(row.AvailableAt > row.QueuedAt.AddMinutes(1));
        Assert.Equal(
            OutboxDispatchResult.NoWork,
            await Dispatcher(fresh, new()).DispatchNextAsync(Token)
        );
        await SqlAsync(
            connection,
            "UPDATE messages.pending SET available_at = clock_timestamp() - interval '1 second'"
        );
        Assert.Equal(
            OutboxDispatchResult.Published,
            await Dispatcher(fresh, new()).DispatchNextAsync(Token)
        );
    }

    [Fact]
    public async Task ExternalAcceptanceFollowedByCompletionFailureRepeatsTheRetainedEnvelopeInAFreshContext()
    {
        string connection = await DatabaseAsync(postgres);
        var message = await EnqueueAsync(connection);
        await SqlAsync(
            connection,
            "ALTER TABLE messages.pending ADD CONSTRAINT test_completion CHECK (dispatched_at IS NULL)"
        );
        var publisher = new RecordingPublisher();
        await using (var context = Context(connection))
        {
            var failure = await Assert.ThrowsAsync<OutboxCompletionException>(() =>
                Dispatcher(context, publisher).DispatchNextAsync(Token)
            );
            Assert.Equal(message.MessageId, failure.MessageId);
            Assert.Equal(
                PostgresErrorCodes.CheckViolation,
                Assert.IsType<PostgresException>(failure.InnerException).SqlState
            );
        }

        await SqlAsync(connection, "ALTER TABLE messages.pending DROP CONSTRAINT test_completion");
        await ExpireAsync(connection);
        await using var recovered = Context(connection);
        Assert.Equal(
            OutboxDispatchResult.Published,
            await Dispatcher(recovered, publisher).DispatchNextAsync(Token)
        );
        Assert.Equal(2, publisher.Messages.Count);
        Assert.All(
            publisher.Messages,
            delivered =>
            {
                Assert.Equal(message.MessageId, delivered.MessageId);
                Assert.True(JsonElement.DeepEquals(message.Payload, delivered.Payload));
            }
        );
        Assert.Single(
            publisher.Messages.Select(delivered => delivered.Payload.GetRawText()).Distinct()
        );
    }

    [Fact]
    public async Task CancellationLeavesAnAbandonedClaimRecoverableAndDoesNotFalselyComplete()
    {
        string connection = await DatabaseAsync(postgres);
        await EnqueueAsync(connection);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var publisher = new RecordingPublisher
        {
            OnPublish = async (_, token) =>
            {
                entered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            },
        };
        await using (var context = Context(connection))
        {
            var dispatch = Dispatcher(context, publisher).DispatchNextAsync(cancellation.Token);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => dispatch);
        }
        await using var fresh = Context(connection);
        var abandoned = await fresh.Set<OutboxMessageRecord>().AsNoTracking().SingleAsync(Token);
        Assert.NotNull(abandoned.LeaseToken);
        Assert.Null(abandoned.DispatchedAt);
        await ExpireAsync(connection);
        Assert.Equal(
            OutboxDispatchResult.Published,
            await Dispatcher(fresh, new()).DispatchNextAsync(Token)
        );
    }

    [Fact]
    public async Task PublicationAndDeferFailuresAreBothRetained()
    {
        string connection = await DatabaseAsync(postgres);
        await EnqueueAsync(connection);
        var publication = new IOException("network failed");
        var publisher = new RecordingPublisher
        {
            OnPublish = async (_, _) =>
            {
                await SqlAsync(
                    connection,
                    "ALTER TABLE messages.pending ADD CONSTRAINT test_defer CHECK (lease_token IS NOT NULL)"
                );
                throw publication;
            },
        };
        await using var context = Context(connection);
        var failure = await Assert.ThrowsAsync<OutboxFailureAccountingException>(() =>
            Dispatcher(context, publisher).DispatchNextAsync(Token)
        );
        Assert.Same(publication, failure.InnerExceptions[0]);
        Assert.IsType<PostgresException>(failure.InnerExceptions[1]);
    }
}
