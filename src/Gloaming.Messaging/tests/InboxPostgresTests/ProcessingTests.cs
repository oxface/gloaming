using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using static Gloaming.Messaging.InboxTests.InboxProof;

namespace Gloaming.Messaging.InboxTests;

public sealed class ProcessingTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task HandlerEffectsReplyAndCompletionCommitTogether()
    {
        string connection = await DatabaseAsync(fixture);
        var scenario = new HandlerScenario { Reply = true };
        await using var provider = Provider(connection, scenario);
        var message = Message();
        await ReceiveAsync(provider, message);
        Assert.Equal(InboxProcessingResult.Processed, await ProcessAsync(provider));
        await using var fresh = Context(connection);
        Assert.Equal(message.MessageId, (await fresh.Set<HandledItem>().SingleAsync(Token)).Id);
        Assert.NotNull((await fresh.Set<InboxMessageRecord>().SingleAsync(Token)).ProcessedAt);
        var reply = await fresh.Set<OutboxMessageRecord>().SingleAsync(Token);
        Assert.Equal(message.CorrelationId, reply.CorrelationId);
        Assert.Equal(message.MessageId.ToString(), reply.CausationId);
        Assert.Equal(message.TenantKey, reply.TenantKey);
    }

    [Fact]
    public async Task CompetingProcessorsSkipTheLockedDeliveryAndCanHandleAnotherRow()
    {
        string connection = await DatabaseAsync(fixture);
        var first = Message();
        var second = Message();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scenario = new HandlerScenario
        {
            OnHandle = async (database, message, token) =>
            {
                database.Add(new HandledItem { Id = message.MessageId, Value = 7 });
                if (message.MessageId == first.MessageId)
                {
                    entered.SetResult();
                    await release.Task.WaitAsync(token);
                }
            },
        };
        await using var provider = Provider(connection, scenario);
        await ReceiveAsync(provider, first);
        var pending = ProcessAsync(provider);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
        try
        {
            Assert.Equal(
                InboxProcessingResult.NoWork,
                await ProcessAsync(provider).WaitAsync(TimeSpan.FromSeconds(5), Token)
            );
            await ReceiveAsync(provider, second);
            Assert.Equal(
                InboxProcessingResult.Processed,
                await ProcessAsync(provider).WaitAsync(TimeSpan.FromSeconds(5), Token)
            );
            await using var observer = Context(connection);
            Assert.Equal(
                second.MessageId,
                (await observer.Set<HandledItem>().SingleAsync(Token)).Id
            );
        }
        finally
        {
            release.TrySetResult();
        }
        Assert.Equal(InboxProcessingResult.Processed, await pending);
        await using var fresh = Context(connection);
        Assert.Equal(2, await fresh.Set<HandledItem>().CountAsync(Token));
        Assert.Equal(
            2,
            await fresh.Set<InboxMessageRecord>().CountAsync(row => row.ProcessedAt != null, Token)
        );
    }

    [Theory]
    [InlineData("handler")]
    [InlineData("save")]
    [InlineData("completion")]
    public async Task FailureRollsBackLocalSqlAndReplyThenDefersWithoutBlockingOtherWork(
        string fault
    )
    {
        string connection = await DatabaseAsync(fixture);
        var scenario = new HandlerScenario { Reply = true };
        var failed = Message();
        if (fault == "handler")
            scenario.OnHandle = async (database, message, token) =>
            {
                database.Add(new HandledItem { Id = message.MessageId, Value = 7 });
                // Exercise rollback of already executed EF SQL, not just untracked proposals.
                await database.SaveChangesAsync(token);
                throw new IOException("handler failed after an earlier save");
            };
        if (fault == "save")
            await SqlAsync(
                connection,
                "ALTER TABLE receiver.handled ADD CONSTRAINT proof_save CHECK (\"Value\" > 7)"
            );
        if (fault == "completion")
            await SqlAsync(
                connection,
                "ALTER TABLE receiver.inbox ADD CONSTRAINT proof_completion CHECK (processed_at IS NULL)"
            );
        await using var provider = Provider(connection, scenario);
        await ReceiveAsync(provider, failed);
        await Assert.ThrowsAnyAsync<Exception>(() => ProcessAsync(provider));
        await using (var read = Context(connection))
        {
            Assert.Empty(await read.Set<HandledItem>().ToArrayAsync(Token));
            Assert.Empty(await read.Set<OutboxMessageRecord>().ToArrayAsync(Token));
            var row = await read.Set<InboxMessageRecord>().SingleAsync(Token);
            Assert.Null(row.ProcessedAt);
            Assert.True(row.AvailableAt > row.ReceivedAt);
            Assert.Equal(1, row.FailureCount);
            Assert.Equal(1, row.FailuresSinceRedrive);
            Assert.Equal(1, row.Revision);
            Assert.NotNull(row.LastFailedAt);
            Assert.Equal("processing-failed", row.LastFailureCode);
            Assert.Null(row.DeadLetteredAt);
        }
        scenario.OnHandle = null;
        if (fault == "save")
            await SqlAsync(connection, "ALTER TABLE receiver.handled DROP CONSTRAINT proof_save");
        if (fault == "completion")
            await SqlAsync(
                connection,
                "ALTER TABLE receiver.inbox DROP CONSTRAINT proof_completion"
            );
        Assert.Equal(InboxProcessingResult.NoWork, await ProcessAsync(provider));
        var another = Message();
        await ReceiveAsync(provider, another);
        Assert.Equal(InboxProcessingResult.Processed, await ProcessAsync(provider));
        await ReadyAsync(connection);
        Assert.Equal(InboxProcessingResult.Processed, await ProcessAsync(provider));
        await using var recovered = Context(connection);
        Assert.Equal(2, await recovered.Set<HandledItem>().CountAsync(Token));
        Assert.Equal(2, await recovered.Set<OutboxMessageRecord>().CountAsync(Token));
        Assert.Equal(
            2,
            await recovered
                .Set<InboxMessageRecord>()
                .CountAsync(row => row.ProcessedAt != null, Token)
        );
        Assert.Equal(3, scenario.Contexts.Distinct().Count());
    }

    [Fact]
    public async Task CancellationRollsBackAnEarlierSaveAndFreshProcessingRecovers()
    {
        string connection = await DatabaseAsync(fixture);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scenario = new HandlerScenario
        {
            OnHandle = async (database, message, token) =>
            {
                database.Add(new HandledItem { Id = message.MessageId, Value = 7 });
                await database.SaveChangesAsync(token);
                entered.SetResult();
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
            },
        };
        await using var provider = Provider(connection, scenario);
        await ReceiveAsync(provider, Message());
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var processing = ProcessAsync(provider, cancel.Token);
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
        await cancel.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => processing);
        await using (var observer = Context(connection))
        {
            Assert.Empty(await observer.Set<HandledItem>().ToArrayAsync(Token));
            Assert.Null((await observer.Set<InboxMessageRecord>().SingleAsync(Token)).ProcessedAt);
        }
        scenario.OnHandle = null;
        Assert.Equal(InboxProcessingResult.Processed, await ProcessAsync(provider));
    }

    [Fact]
    public async Task TerminatedConnectionReleasesTheLockAndOldAttemptCannotUndoSuccessorCompletion()
    {
        string connection = await DatabaseAsync(fixture);
        var entered = new TaskCompletionSource<int>(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var scenario = new HandlerScenario
        {
            OnHandle = async (database, message, token) =>
            {
                database.Add(new HandledItem { Id = message.MessageId, Value = 7 });
                await database.SaveChangesAsync(token);
                await using var command = database.Database.GetDbConnection().CreateCommand();
                command.Transaction = database.Database.CurrentTransaction!.GetDbTransaction();
                command.CommandText = "SELECT pg_backend_pid()";
                entered.SetResult((int)(await command.ExecuteScalarAsync(token))!);
                await release.Task.WaitAsync(token);
            },
        };
        await using var provider = Provider(connection, scenario);
        await ReceiveAsync(provider, Message());
        DateTimeOffset originalAvailability;
        await using (var initial = Context(connection))
            originalAvailability = (
                await initial.Set<InboxMessageRecord>().SingleAsync(Token)
            ).AvailableAt;
        var old = ProcessAsync(provider);
        int pid = await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
        try
        {
            await using var control = new NpgsqlConnection(connection);
            await control.OpenAsync(Token);
            await using var kill = new NpgsqlCommand("SELECT pg_terminate_backend(@pid)", control);
            kill.Parameters.AddWithValue("pid", pid);
            Assert.Equal(true, await kill.ExecuteScalarAsync(Token));
            scenario.OnHandle = null;
            Assert.Equal(
                InboxProcessingResult.Processed,
                await ProcessAsync(provider).WaitAsync(TimeSpan.FromSeconds(10), Token)
            );
        }
        finally
        {
            release.TrySetResult();
        }
        await Assert.ThrowsAnyAsync<Exception>(() => old);
        await using var fresh = Context(connection);
        Assert.Single(await fresh.Set<HandledItem>().ToArrayAsync(Token));
        var retained = await fresh.Set<InboxMessageRecord>().SingleAsync(Token);
        Assert.NotNull(retained.ProcessedAt);
        Assert.Equal(originalAvailability, retained.AvailableAt);
        Assert.Equal(0, retained.FailureCount);
        Assert.Equal(1, retained.Revision);
    }

    [Fact]
    public async Task DifferentDeliveriesStillObeyBusinessConcurrencyAndLosingCompletionRollsBack()
    {
        string connection = await DatabaseAsync(fixture);
        Guid id = Guid.NewGuid();
        await using (var seed = Context(connection))
        {
            seed.Add(
                new SharedState
                {
                    Id = id,
                    Version = 1,
                    Value = 2,
                }
            );
            await seed.SaveChangesAsync(Token);
        }
        var firstLoaded = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var bothLoaded = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        var winnerDone = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        int arrivals = 0;
        var scenario = new HandlerScenario
        {
            Reply = true,
            OnHandle = async (database, _, token) =>
            {
                var state = await database.Set<SharedState>().SingleAsync(token);
                state.Value--;
                state.Version++;
                if (Interlocked.Increment(ref arrivals) == 1)
                {
                    firstLoaded.SetResult();
                    await bothLoaded.Task.WaitAsync(token);
                }
                else
                {
                    bothLoaded.SetResult();
                    await winnerDone.Task.WaitAsync(token);
                }
            },
        };
        await using var provider = Provider(connection, scenario);
        await ReceiveAsync(provider, Message());
        await ReceiveAsync(provider, Message());
        var winning = ProcessAsync(provider);
        await firstLoaded.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
        var losing = ProcessAsync(provider);
        try
        {
            Assert.Equal(
                InboxProcessingResult.Processed,
                await winning.WaitAsync(TimeSpan.FromSeconds(10), Token)
            );
        }
        finally
        {
            winnerDone.TrySetResult();
        }
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => losing);
        await using var fresh = Context(connection);
        Assert.Equal(1, (await fresh.Set<SharedState>().SingleAsync(Token)).Value);
        Assert.Single(await fresh.Set<OutboxMessageRecord>().ToArrayAsync(Token));
        Assert.Equal(
            1,
            await fresh.Set<InboxMessageRecord>().CountAsync(row => row.ProcessedAt != null, Token)
        );
        scenario.OnHandle = async (database, _, token) =>
        {
            var state = await database.Set<SharedState>().SingleAsync(token);
            state.Value--;
            state.Version++;
        };
        await ReadyAsync(connection);
        Assert.Equal(InboxProcessingResult.Processed, await ProcessAsync(provider));
        await using var recovered = Context(connection);
        Assert.Equal(0, (await recovered.Set<SharedState>().SingleAsync(Token)).Value);
        Assert.Equal(2, await recovered.Set<OutboxMessageRecord>().CountAsync(Token));
    }
}
