using Gloaming.Messaging.DueTimeProof.Current;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;

namespace Gloaming.Messaging.Tests;

public sealed partial class DueTimeIntentTests
{
    [Fact]
    public async Task ApprovedEarlyClosureReplacesPreparationButNeverInitiatesAReview()
    {
        string connection = await SetupAsync();
        Guid person = Guid.NewGuid();
        await GrowthWriteAsync(
            connection,
            database =>
                Growth.ApprovedClosureAsync(database, Scope, person, Due.AddYears(-1), Due, Token)
        );
        await GrowthWriteAsync(
            connection,
            database => NativeDueIntents.ReleaseAsync(database, Scope, Due, 1, Token)
        );
        var old = await GrowthDispatchAsync(connection);
        DateTimeOffset newPreparation = Due.AddMonths(3);
        await GrowthWriteAsync(
            connection,
            database =>
                Growth.ApprovedClosureAsync(
                    database,
                    Scope,
                    person,
                    Due.AddMonths(-9),
                    newPreparation,
                    Token
                )
        );
        Assert.Equal(
            Acceptance.Stale,
            await GrowthWriteAsync(
                connection,
                database => Growth.AcceptAsync(database, Scope, old, newPreparation, Token)
            )
        );
        Assert.Empty(
            await GrowthWriteAsync(
                connection,
                database => NativeDueIntents.ReleaseAsync(database, Scope, Due, 1, Token)
            )
        );
        Assert.Single(
            await GrowthWriteAsync(
                connection,
                database => NativeDueIntents.ReleaseAsync(database, Scope, newPreparation, 1, Token)
            )
        );
        var current = await GrowthDispatchAsync(connection);
        Assert.Equal(
            Acceptance.Accepted,
            await GrowthWriteAsync(
                connection,
                database => Growth.AcceptAsync(database, Scope, current, newPreparation, Token)
            )
        );
        Assert.Equal(
            Acceptance.Duplicate,
            await GrowthWriteAsync(
                connection,
                database => Growth.AcceptAsync(database, Scope, current, newPreparation, Token)
            )
        );
        await using var read = GrowthContext(connection);
        var indication = await read.Set<PreparationIndication>().SingleAsync(Token);
        var cycle = await read.Set<ReviewCycle>().SingleAsync(Token);
        Assert.Equal(cycle.OccurrenceId, indication.OccurrenceId);
        Assert.Equal(Due.AddMonths(-9), cycle.ApprovedClosureAt);
    }

    [Fact]
    public async Task CancellingAnUnfinishedReviewPreservesTheApprovedClosureAnchorAndItsTimer()
    {
        string connection = await SetupAsync();
        Guid person = Guid.NewGuid();
        Guid original = await GrowthWriteAsync(
            connection,
            database =>
                Growth.ApprovedClosureAsync(database, Scope, person, Due.AddYears(-1), Due, Token)
        );
        await GrowthWriteAsync(
            connection,
            async database =>
            {
                await Growth.CancelUnfinishedReviewAsync(database, Scope, person, Token);
                return true;
            }
        );
        await GrowthWriteAsync(
            connection,
            database => NativeDueIntents.ReleaseAsync(database, Scope, Due, 1, Token)
        );
        var message = await GrowthDispatchAsync(connection);
        Assert.Equal(
            Acceptance.Accepted,
            await GrowthWriteAsync(
                connection,
                database => Growth.AcceptAsync(database, Scope, message, Due, Token)
            )
        );
        await using var read = GrowthContext(connection);
        var cycle = await read.Set<ReviewCycle>().SingleAsync(Token);
        Assert.Equal(original, cycle.OccurrenceId);
        Assert.Equal(Due.AddYears(-1), cycle.ApprovedClosureAt);
        Assert.True(cycle.UnfinishedReviewCancelled);
        Assert.Equal(1, await read.Set<PreparationIndication>().CountAsync(Token));
    }

    private static async Task<T> GrowthWriteAsync<T>(
        string connection,
        Func<GrowthDatabase, Task<T>> action
    )
    {
        await using var database = GrowthContext(connection);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        T result = await action(database);
        await database.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
        return result;
    }

    private static async Task<OutgoingMessage> GrowthDispatchAsync(string connection)
    {
        await using var database = GrowthContext(connection);
        var publisher = new RecordingPublisher();
        var dispatcher = new PostgresOutboxDispatcher<GrowthDatabase>(
            database,
            publisher,
            new(TimeSpan.FromSeconds(30), TimeSpan.Zero)
        );
        Assert.Equal(OutboxDispatchResult.Published, await dispatcher.DispatchNextAsync(Token));
        return Assert.Single(publisher.Messages);
    }
}
