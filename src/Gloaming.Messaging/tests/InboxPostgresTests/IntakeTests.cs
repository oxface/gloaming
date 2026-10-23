using System.Data;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using static Gloaming.Messaging.InboxTests.InboxProof;

namespace Gloaming.Messaging.InboxTests;

public sealed class IntakeTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task IntakeCommitsExplicitlyAndRecognizesPendingAndCompletedRedelivery()
    {
        string connection = await DatabaseAsync(fixture);
        await using var provider = Provider(connection);
        var message = Message();
        Assert.Equal(
            InboxReceiveResult.Queued,
            await ReceiveAsync(provider, message, commit: false)
        );
        await using (var read = Context(connection))
            Assert.Empty(await read.Set<InboxMessageRecord>().ToArrayAsync(Token));
        Assert.Equal(InboxReceiveResult.Queued, await ReceiveAsync(provider, message));
        Assert.Equal(InboxReceiveResult.AlreadyReceived, await ReceiveAsync(provider, message));
        Assert.Equal(InboxProcessingResult.Processed, await ProcessAsync(provider));
        Assert.Equal(InboxReceiveResult.AlreadyReceived, await ReceiveAsync(provider, message));
        Assert.Equal(InboxProcessingResult.NoWork, await ProcessAsync(provider));
        await using var fresh = Context(connection);
        Assert.Single(await fresh.Set<HandledItem>().ToArrayAsync(Token));
        Assert.NotNull((await fresh.Set<InboxMessageRecord>().SingleAsync(Token)).ProcessedAt);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task RacingArrivalUsesTheWinningCommitOrTakesOverRolledBackIntake(
        bool winnerCommits
    )
    {
        string connection = await DatabaseAsync(fixture);
        await using var provider = Provider(connection);
        var message = Message();
        await using var first = provider.CreateAsyncScope();
        await using var second = provider.CreateAsyncScope();
        var winner = first.ServiceProvider.GetRequiredService<InboxConsumer>();
        var contender = second.ServiceProvider.GetRequiredService<InboxConsumer>();
        await using var winning = await winner.Database.BeginTransactionAsync(Token);
        await using var competing = await contender.Database.BeginTransactionAsync(Token);
        Assert.Equal(
            InboxReceiveResult.Queued,
            await first
                .ServiceProvider.GetRequiredService<IInbox<InboxConsumer>>()
                .ReceiveAsync("render", message, Token)
        );
        var waiting = second
            .ServiceProvider.GetRequiredService<IInbox<InboxConsumer>>()
            .ReceiveAsync("render", message, Token);
        // Observe the actual unique-key wait, rather than guessing concurrency from a sleep.
        await WaitAsync(async () =>
        {
            await using var observer = new NpgsqlConnection(connection);
            await observer.OpenAsync(Token);
            await using var command = new NpgsqlCommand(
                "SELECT EXISTS (SELECT 1 FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock' AND query LIKE 'INSERT INTO %')",
                observer
            );
            return await command.ExecuteScalarAsync(Token) is true;
        });
        if (winnerCommits)
            await winning.CommitAsync(Token);
        else
            await winning.RollbackAsync(Token);
        Assert.Equal(
            winnerCommits ? InboxReceiveResult.AlreadyReceived : InboxReceiveResult.Queued,
            await waiting.WaitAsync(TimeSpan.FromSeconds(10), Token)
        );
        await competing.CommitAsync(Token);
        await using var fresh = Context(connection);
        Assert.Single(await fresh.Set<InboxMessageRecord>().ToArrayAsync(Token));
    }

    [Theory]
    [InlineData("name")]
    [InlineData("schema")]
    [InlineData("payload")]
    [InlineData("tenant")]
    [InlineData("correlation")]
    [InlineData("causation")]
    public async Task SameIdentityWithDifferentEnvelopeIsAnExplicitConflict(string field)
    {
        string connection = await DatabaseAsync(fixture);
        await using var provider = Provider(connection);
        var original = Message();
        await ReceiveAsync(provider, original);
        var changed = new IncomingMessage(
            original.MessageId,
            original.ProducerKey,
            field == "name" ? "other" : original.MessageName,
            field == "schema" ? 2 : original.SchemaVersion,
            field == "payload" ? Message(json: "{\"value\":2}").Payload : original.Payload,
            field == "tenant" ? "beta" : original.TenantKey,
            field == "correlation" ? null : original.CorrelationId,
            field == "causation" ? "different" : original.CausationId
        );
        var conflict = await Assert.ThrowsAsync<InboxMessageConflictException>(() =>
            ReceiveAsync(provider, changed)
        );
        Assert.Equal(original.MessageId, conflict.MessageId);
        Assert.Equal("render", conflict.SubscriptionKey);
        await using var fresh = Context(connection);
        Assert.Equal("alpha", (await fresh.Set<InboxMessageRecord>().SingleAsync(Token)).TenantKey);
    }

    [Fact]
    public async Task JsonbComparisonPreservesValuesAndDeliveryNamespacesRemainIndependent()
    {
        string connection = await DatabaseAsync(fixture);
        await using var provider = Provider(connection);
        Guid id = Guid.NewGuid();
        await ReceiveAsync(provider, Message(id, "{\"a\":1,\"b\":[2,3]}"));
        Assert.Equal(
            InboxReceiveResult.AlreadyReceived,
            await ReceiveAsync(provider, Message(id, "{ \"b\": [2,3], \"a\": 1.0 }"))
        );
        await Assert.ThrowsAsync<InboxMessageConflictException>(() =>
            ReceiveAsync(provider, Message(id, "{\"b\":[3,2],\"a\":1}"))
        );
        Assert.Equal(
            InboxReceiveResult.Queued,
            await ReceiveAsync(provider, Message(id, producer: "other"))
        );
        Assert.Equal(
            InboxReceiveResult.Queued,
            await ReceiveAsync(provider, Message(id), "another-subscription")
        );
        await using var fresh = Context(connection);
        Assert.Equal(3, await fresh.Set<InboxMessageRecord>().CountAsync(Token));
    }

    [Theory]
    [InlineData(IsolationLevel.RepeatableRead)]
    [InlineData(IsolationLevel.Serializable)]
    public async Task UnsupportedIntakeIsolationAndMissingTransactionRejectBeforeInsertion(
        IsolationLevel isolation
    )
    {
        string connection = await DatabaseAsync(fixture);
        await using var provider = Provider(connection);
        await using var scope = provider.CreateAsyncScope();
        var inbox = scope.ServiceProvider.GetRequiredService<IInbox<InboxConsumer>>();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            inbox.ReceiveAsync("render", Message(), Token)
        );
        var database = scope.ServiceProvider.GetRequiredService<InboxConsumer>();
        await using var transaction = await database.Database.BeginTransactionAsync(
            isolation,
            Token
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            inbox.ReceiveAsync("render", Message(), Token)
        );
        await transaction.RollbackAsync(Token);
        await using var fresh = Context(connection);
        Assert.Empty(await fresh.Set<InboxMessageRecord>().ToArrayAsync(Token));
    }
}
