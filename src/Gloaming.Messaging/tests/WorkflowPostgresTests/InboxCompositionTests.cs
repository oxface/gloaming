using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.Workflows.EntityFrameworkCore;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Gloaming.Messaging.WorkflowPostgresTests.WorkflowProof;

namespace Gloaming.Messaging.WorkflowPostgresTests;

public sealed class InboxCompositionTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task ProcessNextCommitsContinuationNextRequestAndInboxCompletionTogether()
    {
        var connection = await Database(fixture);
        var methods = new WorkspaceMethods();
        var first = await Start(connection, methods, new(Guid.NewGuid()));
        await using var provider = Provider(connection, methods);
        var reply = Reply(first, true);
        await Receive(provider, reply);
        Assert.Equal(InboxProcessingResult.Processed, await Process(provider));
        await using var db = Context(connection);
        var row = await db.Set<WorkflowInstanceRecord>().SingleAsync(Token);
        Assert.Equal(2, row.Version);
        var outgoing = await db.Set<OutboxMessageRecord>()
            .SingleAsync(item => item.MessageId == row.PendingCommandId, Token);
        Assert.Equal("resource.activate", outgoing.MessageName);
        Assert.Equal(reply.MessageId.ToString("D"), outgoing.CausationId);
        Assert.NotNull((await db.Set<InboxMessageRecord>().SingleAsync(Token)).ProcessedAt);
    }

    [Fact]
    public async Task OutboxConstraintFailureRollsBackContinuationAndCompletionThenFreshScopeRecovers()
    {
        var connection = await Database(fixture);
        var methods = new WorkspaceMethods();
        var first = await Start(connection, methods, new(Guid.NewGuid()));
        await using var provider = Provider(connection, methods);
        await Receive(provider, Reply(first, true));
        await using (var db = Context(connection))
            await db.Database.ExecuteSqlRawAsync(
                "ALTER TABLE operations.outgoing ADD CONSTRAINT reject_activation CHECK (message_name <> 'resource.activate')",
                Token
            );

        await Assert.ThrowsAsync<DbUpdateException>(() => Process(provider));
        await using (var observer = Context(connection))
        {
            Assert.Equal(first, await Runner(observer, methods).ReadAsync(first.Key, Token));
            Assert.Equal(1, await observer.Set<OutboxMessageRecord>().CountAsync(Token));
            Assert.Null((await observer.Set<InboxMessageRecord>().SingleAsync(Token)).ProcessedAt);
            await observer.Database.ExecuteSqlRawAsync(
                "ALTER TABLE operations.outgoing DROP CONSTRAINT reject_activation",
                Token
            );
        }

        Assert.Equal(InboxProcessingResult.Processed, await Process(provider));
        await using var recovered = Context(connection);
        Assert.Equal(2, (await recovered.Set<WorkflowInstanceRecord>().SingleAsync(Token)).Version);
        Assert.Equal(2, await recovered.Set<OutboxMessageRecord>().CountAsync(Token));
        Assert.NotNull((await recovered.Set<InboxMessageRecord>().SingleAsync(Token)).ProcessedAt);
    }

    [Fact]
    public async Task DifferentDeliveryOfEqualOutcomeCompletesWithoutInvokingContinuationAgain()
    {
        var connection = await Database(fixture);
        var methods = new WorkspaceMethods();
        var first = await Start(connection, methods, new(Guid.NewGuid()));
        await using var provider = Provider(connection, methods);
        await Receive(provider, Reply(first, true));
        Assert.Equal(InboxProcessingResult.Processed, await Process(provider));
        await Receive(provider, Reply(first, true));
        Assert.Equal(InboxProcessingResult.Processed, await Process(provider));
        Assert.Equal(1, methods.Invocations.Count(name => name == "created"));
        await using var observer = Context(connection);
        Assert.Equal(
            2,
            await observer
                .Set<InboxMessageRecord>()
                .CountAsync(row => row.ProcessedAt != null, Token)
        );
        Assert.Equal(2, await observer.Set<OutboxMessageRecord>().CountAsync(Token));
        Assert.Equal(2, (await observer.Set<WorkflowInstanceRecord>().SingleAsync(Token)).Version);
    }
}
