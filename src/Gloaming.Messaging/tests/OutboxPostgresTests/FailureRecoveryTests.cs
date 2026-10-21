using System.Text.Json;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using ModulithFoundry.Tests.Infrastructure;
using Npgsql;
using static Gloaming.Messaging.Tests.OutboxProof;
using static Gloaming.Messaging.Tests.OutboxRecoveryProof;

namespace Gloaming.Messaging.Tests;

[Collection("Messaging PostgreSQL")]
public sealed class FailureRecoveryTests(PostgreSqlFixture postgres)
{
    [Fact]
    public async Task DurableBudgetAndRepeatedRetryPreserveEnvelopeAndLifetimeEvidence()
    {
        string connection = await DatabaseAsync(postgres);
        var original = await EnqueueAsync(connection);
        var budget = new Policy(failure =>
            failure.FailuresSinceRedrive < 2
                ? new OutboxFailureDecision.RetryAfter(TimeSpan.Zero, "transient")
                : new OutboxFailureDecision.DeadLetter("budget")
        );
        await FailAsync(connection, budget);
        var first = await ReadAsync(connection, original.MessageId);
        Assert.Equal(1, first.Attempts);
        Assert.Equal(1, first.FailureCount);
        Assert.Equal(1, first.FailuresSinceRedrive);
        Assert.Equal(2, first.Revision);
        Assert.Null(first.LeaseToken);
        await FailAsync(connection, budget);
        var stopped = await ReadAsync(connection, original.MessageId);
        Assert.Equal(2, stopped.FailureCount);
        Assert.NotNull(stopped.DeadLetteredAt);
        await using (var fresh = Context(connection))
            Assert.Equal(
                OutboxDispatchResult.NoWork,
                await Dispatcher(fresh, new()).DispatchNextAsync(Token)
            );

        for (int cycle = 1; cycle <= 2; cycle++)
        {
            Assert.Equal(
                OutboxRetryResult.Retried,
                await StoreAsync(
                    connection,
                    store =>
                        store.RetryAsync("exports", original.MessageId, stopped.Revision, Token)
                )
            );
            Assert.Equal(
                OutboxRetryResult.NotApplied,
                await StoreAsync(
                    connection,
                    store =>
                        store.RetryAsync("exports", original.MessageId, stopped.Revision, Token)
                )
            );
            var retried = await ReadAsync(connection, original.MessageId);
            Assert.Equal(cycle, retried.RedriveCount);
            Assert.Equal(0, retried.FailuresSinceRedrive);
            Assert.Equal(stopped.FailureCount, retried.FailureCount);
            Assert.Equal(stopped.LastFailedAt, retried.LastFailedAt);
            if (cycle == 1)
            {
                await FailAsync(connection, Stop);
                var again = await ReadAsync(connection, original.MessageId);
                Assert.Equal(3, again.FailureCount);
                Assert.Equal(1, again.FailuresSinceRedrive);
                Assert.Equal(
                    OutboxRetryResult.NotApplied,
                    await StoreAsync(
                        connection,
                        store =>
                            store.RetryAsync("exports", original.MessageId, stopped.Revision, Token)
                    )
                );
                stopped = again;
            }
        }

        var publisher = new RecordingPublisher();
        await using (var fresh = Context(connection))
            Assert.Equal(
                OutboxDispatchResult.Published,
                await Dispatcher(fresh, publisher).DispatchNextAsync(Token)
            );
        var completed = await ReadAsync(connection, original.MessageId);
        Assert.Equal(4, completed.Attempts);
        Assert.Equal(3, completed.FailureCount);
        Assert.Equal(2, completed.RedriveCount);
        Assert.Equal(first.QueuedAt, completed.QueuedAt);
        Assert.NotNull(completed.DispatchedAt);
        Assert.Equal(
            OutboxRetryResult.NotApplied,
            await StoreAsync(
                connection,
                store => store.RetryAsync("exports", original.MessageId, completed.Revision, Token)
            )
        );
        Assert.True(JsonElement.DeepEquals(original.Payload, completed.Payload));
        Assert.Equal(original.RouteKey, completed.RouteKey);
        Assert.Equal(original.MessageName, completed.MessageName);
        Assert.Equal(original.SchemaVersion, completed.SchemaVersion);
        Assert.Equal(original.TenantKey, completed.TenantKey);
        Assert.Equal(original.CorrelationId, completed.CorrelationId);
        Assert.Equal(original.CausationId, completed.CausationId);
        Assert.Equal(original.TraceParent, completed.TraceParent);
        Assert.Equal(original.TraceState, completed.TraceState);
        Assert.Equal(original.MessageId, Assert.Single(publisher.Messages).MessageId);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ObsoletePublisherCannotAlterSuccessorDeadLetterOrOperatorRetry(bool oldFails)
    {
        string connection = await DatabaseAsync(postgres);
        var original = await EnqueueAsync(connection);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var accepted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var old = new RecordingPublisher
        {
            OnPublish = async (_, token) =>
            {
                entered.SetResult();
                await release.Task.WaitAsync(token);
                accepted.SetResult();
                if (oldFails)
                    throw new IOException("late ambiguous acceptance");
            },
        };
        await using var oldContext = Context(connection);
        var oldAttempt = new PostgresOutboxDispatcher<OutboxConsumer>(
            oldContext,
            old,
            new(TimeSpan.FromSeconds(30), TimeSpan.Zero),
            failurePolicy: Stop
        ).DispatchNextAsync(Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
        try
        {
            await ExpireAsync(connection);
            await FailAsync(connection, Stop);
            var stopped = await ReadAsync(connection, original.MessageId);
            Assert.Equal(1, stopped.FailureCount);
            Assert.Equal(2, stopped.Attempts);
            Assert.Equal(
                OutboxRetryResult.Retried,
                await StoreAsync(
                    connection,
                    store =>
                        store.RetryAsync("exports", original.MessageId, stopped.Revision, Token)
                )
            );
            var retried = await ReadAsync(connection, original.MessageId);
            release.SetResult();
            if (oldFails)
                await Assert.ThrowsAsync<IOException>(() => oldAttempt);
            else
                Assert.Equal(OutboxDispatchResult.ClaimLost, await oldAttempt);
            Assert.True(accepted.Task.IsCompletedSuccessfully);
            var afterOld = await ReadAsync(connection, original.MessageId);
            Assert.Equal(retried.Revision, afterOld.Revision);
            Assert.Equal(retried.AvailableAt, afterOld.AvailableAt);
            Assert.Equal(retried.FailureCount, afterOld.FailureCount);
            Assert.Equal(0, afterOld.FailuresSinceRedrive);
            Assert.Null(afterOld.DeadLetteredAt);
            Assert.Null(afterOld.LeaseToken);
        }
        finally
        {
            release.TrySetResult();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExpiryWhileFinishingWaitsCannotCountStopOrCompleteTheMessage(
        bool publicationFails
    )
    {
        string connection = await DatabaseAsync(postgres);
        var original = await EnqueueAsync(connection);
        await using var blocker = new NpgsqlConnection(connection);
        await blocker.OpenAsync(Token);
        NpgsqlTransaction? locked = null;
        var publication = new IOException("publisher failure");
        var publisher = new RecordingPublisher
        {
            OnPublish = async (_, token) =>
            {
                locked = await blocker.BeginTransactionAsync(token);
                await using var command = new NpgsqlCommand(
                    "SELECT message_id FROM messages.pending FOR UPDATE",
                    blocker,
                    locked
                );
                await command.ExecuteScalarAsync(token);
                if (publicationFails)
                    throw publication;
            },
        };
        string dispatchConnection = new NpgsqlConnectionStringBuilder(connection)
        {
            ApplicationName = "expiry-accounting",
        }.ConnectionString;
        await using var database = Context(dispatchConnection);
        var attempt = new PostgresOutboxDispatcher<OutboxConsumer>(
            database,
            publisher,
            new(TimeSpan.FromSeconds(2), TimeSpan.Zero),
            failurePolicy: Stop
        ).DispatchNextAsync(Token);
        try
        {
            await WaitAsync(
                connection,
                "SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE application_name = 'expiry-accounting' AND cardinality(pg_blocking_pids(pid)) > 0)"
            );
            await WaitAsync(
                connection,
                "SELECT lease_until <= clock_timestamp() FROM messages.pending"
            );
            await locked!.CommitAsync(Token);
            if (publicationFails)
                Assert.Same(publication, await Assert.ThrowsAsync<IOException>(() => attempt));
            else
                Assert.Equal(OutboxDispatchResult.ClaimLost, await attempt);
        }
        finally
        {
            if (locked is not null)
                await locked.DisposeAsync();
        }

        var row = await ReadAsync(connection, original.MessageId);
        Assert.Equal(1, row.Attempts);
        Assert.Equal(0, row.FailureCount);
        Assert.Null(row.DeadLetteredAt);
        Assert.NotNull(row.LeaseToken);
        await using var successor = Context(connection);
        Assert.Equal(
            OutboxDispatchResult.Published,
            await Dispatcher(successor, new()).DispatchNextAsync(Token)
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LostCommitResponsesPreserveActualAccountingOrCompletionWithoutCompensation(
        bool publicationFails
    )
    {
        string connection = await DatabaseAsync(postgres);
        var original = await EnqueueAsync(connection);
        var publisher = new RecordingPublisher
        {
            OnPublish = (_, _) =>
                publicationFails ? throw new IOException("publisher failed") : Task.CompletedTask,
        };
        await using var database = new OutboxConsumer(
            new DbContextOptionsBuilder<OutboxConsumer>()
                .UseNpgsql(connection)
                .AddInterceptors(new LostCommitResponse(2))
                .Options
        );
        var dispatcher = new PostgresOutboxDispatcher<OutboxConsumer>(
            database,
            publisher,
            new(TimeSpan.FromSeconds(30), TimeSpan.Zero),
            failurePolicy: Stop
        );
        if (publicationFails)
        {
            var error = await Assert.ThrowsAsync<OutboxFailureAccountingException>(() =>
                dispatcher.DispatchNextAsync(Token)
            );
            Assert.Equal(original.MessageId, error.MessageId);
            Assert.IsType<IOException>(error.PublicationFailure);
            Assert.IsType<IOException>(error.AccountingFailure);
        }
        else
        {
            var error = await Assert.ThrowsAsync<OutboxCompletionException>(() =>
                dispatcher.DispatchNextAsync(Token)
            );
            Assert.Equal(original.MessageId, error.MessageId);
            Assert.IsType<IOException>(error.InnerException);
        }

        var row = await ReadAsync(connection, original.MessageId);
        Assert.Equal(publicationFails ? 1 : 0, row.FailureCount);
        Assert.Equal(publicationFails, row.DeadLetteredAt is not null);
        Assert.Equal(!publicationFails, row.DispatchedAt is not null);
        Assert.Null(row.LeaseToken);
        await using var fresh = Context(connection);
        Assert.Equal(
            OutboxDispatchResult.NoWork,
            await Dispatcher(fresh, publisher).DispatchNextAsync(Token)
        );
        Assert.Single(publisher.Messages);
    }

    [Theory]
    [InlineData("throw")]
    [InlineData("null")]
    [InlineData("delay")]
    [InlineData("code")]
    [InlineData("sql")]
    public async Task UnconfirmedAccountingRetainsBothErrorsAndTheCommittedClaim(string fault)
    {
        string connection = await DatabaseAsync(postgres);
        var original = await EnqueueAsync(connection);
        if (fault == "sql")
            await SqlAsync(
                connection,
                "ALTER TABLE messages.pending ADD CONSTRAINT reject_accounting CHECK (failure_count = 0)"
            );
        var policy = new Policy(_ =>
            fault switch
            {
                "throw" => throw new InvalidOperationException("classification unavailable"),
                "null" => null!,
                "delay" => new OutboxFailureDecision.RetryAfter(
                    TimeSpan.FromSeconds(-1),
                    "invalid"
                ),
                "code" => new OutboxFailureDecision.DeadLetter(" "),
                _ => new OutboxFailureDecision.DeadLetter("review"),
            }
        );
        var publication = new IOException("original transport exception");
        await using var database = Context(connection);
        var publisher = new RecordingPublisher { OnPublish = (_, _) => throw publication };
        var error = await Assert.ThrowsAsync<OutboxFailureAccountingException>(() =>
            new PostgresOutboxDispatcher<OutboxConsumer>(
                database,
                publisher,
                new(TimeSpan.FromSeconds(30), TimeSpan.Zero),
                failurePolicy: policy
            ).DispatchNextAsync(Token)
        );
        Assert.Equal(original.MessageId, error.MessageId);
        Assert.Same(publication, error.PublicationFailure);
        Assert.Same(publication, error.InnerExceptions[0]);
        Assert.Same(error.AccountingFailure, error.InnerExceptions[1]);
        var row = await ReadAsync(connection, original.MessageId);
        Assert.Equal(1, row.Attempts);
        Assert.Equal(1, row.Revision);
        Assert.Equal(0, row.FailureCount);
        Assert.Null(row.LastFailureCode);
        Assert.Null(row.DeadLetteredAt);
        Assert.NotNull(row.LeaseToken);
    }

    [Fact]
    public async Task ActualAccountingBackendDeathPreservesTheOriginalErrorAndSuccessorRecovery()
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
            ApplicationName = "terminate-accounting",
        }.ConnectionString;
        await using var database = Context(named);
        var attempt = new PostgresOutboxDispatcher<OutboxConsumer>(
            database,
            publisher,
            new(TimeSpan.FromSeconds(30), TimeSpan.Zero),
            failurePolicy: Stop
        ).DispatchNextAsync(Token);
        try
        {
            await WaitAsync(
                connection,
                "SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE application_name = 'terminate-accounting' AND cardinality(pg_blocking_pids(pid)) > 0)"
            );
            Assert.True(
                await ScalarAsync(
                    connection,
                    "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE application_name = 'terminate-accounting'"
                )
            );
            var error = await Assert.ThrowsAsync<OutboxFailureAccountingException>(() => attempt);
            Assert.Same(publication, error.PublicationFailure);
            Assert.IsAssignableFrom<NpgsqlException>(error.AccountingFailure);
        }
        finally
        {
            if (locked is not null)
                await locked.DisposeAsync();
        }

        var row = await ReadAsync(connection, message.MessageId);
        Assert.Equal(0, row.FailureCount);
        Assert.NotNull(row.LeaseToken);
        await ExpireAsync(connection);
        await using var successor = Context(connection);
        Assert.Equal(
            OutboxDispatchResult.Published,
            await Dispatcher(successor, new()).DispatchNextAsync(Token)
        );
    }
}
