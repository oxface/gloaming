using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Tests.Infrastructure;
using static Gloaming.Messaging.Tests.OutboxProof;

namespace Gloaming.Messaging.Tests;

[Collection("Messaging PostgreSQL")]
public sealed class RouteSelectionTests(PostgreSqlFixture postgres)
{
    [Fact]
    public async Task ExcludedRouteIsNotClaimedAndLiteralQuotedKeyIsParameterizedWithExactCase()
    {
        var connection = await DatabaseAsync(postgres);
        const string selected = "Route'); DROP TABLE messages.pending; --";
        var excluded = await EnqueueAsync(connection, ForRoute(selected.ToLowerInvariant()));
        var included = await EnqueueAsync(connection, ForRoute(selected));
        var publisher = new RecordingPublisher();
        await using var database = Context(connection);
        var dispatcher = Select(database, publisher, [selected]);
        Assert.Equal(OutboxDispatchResult.Published, await dispatcher.DispatchNextAsync(Token));
        Assert.Equal(included.MessageId, Assert.Single(publisher.Messages).MessageId);
        Assert.Equal(OutboxDispatchResult.NoWork, await dispatcher.DispatchNextAsync(Token));
        var untouched = await database
            .Set<OutboxMessageRecord>()
            .AsNoTracking()
            .SingleAsync(row => row.MessageId == excluded.MessageId, Token);
        Assert.Equal(0, untouched.Attempts);
        Assert.Equal(0, untouched.Revision);
        Assert.Null(untouched.LeaseToken);
        Assert.Null(untouched.LeaseUntil);
        Assert.Null(untouched.DispatchedAt);
        Assert.Equal(2, await database.Set<OutboxMessageRecord>().CountAsync(Token));
    }

    [Fact]
    public async Task DisjointRouteSelectorsProgressWhileAnotherSelectedPublisherIsBlocked()
    {
        var connection = await DatabaseAsync(postgres);
        await EnqueueAsync(connection, ForRoute("first"));
        await EnqueueAsync(connection, ForRoute("second"));
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new RecordingPublisher
        {
            OnPublish = async (_, token) =>
            {
                entered.SetResult();
                await release.Task.WaitAsync(token);
            },
        };
        var second = new RecordingPublisher();
        await using var a = Context(connection);
        await using var b = Context(connection);
        var pending = Select(a, first, ["first"]).DispatchNextAsync(Token);
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
            Assert.Equal(
                OutboxDispatchResult.Published,
                await Select(b, second, ["second"])
                    .DispatchNextAsync(Token)
                    .WaitAsync(TimeSpan.FromSeconds(10), Token)
            );
            Assert.Equal("second", Assert.Single(second.Messages).RouteKey);
        }
        finally
        {
            release.TrySetResult();
        }

        Assert.Equal(OutboxDispatchResult.Published, await pending);
        Assert.Equal("first", Assert.Single(first.Messages).RouteKey);
    }

    [Fact]
    public async Task MutableSelectionIsCopiedAtConstructionAndFreshDispatcherObservesChangedPolicy()
    {
        var connection = await DatabaseAsync(postgres);
        await EnqueueAsync(connection, ForRoute("original"));
        await EnqueueAsync(connection, ForRoute("replacement"));
        var routes = new List<string> { "original" };
        var publisher = new RecordingPublisher();
        await using var database = Context(connection);
        var dispatcher = Select(database, publisher, routes);
        routes.Clear();
        routes.Add("replacement");
        Assert.Equal(OutboxDispatchResult.Published, await dispatcher.DispatchNextAsync(Token));
        Assert.Equal("original", Assert.Single(publisher.Messages).RouteKey);
        Assert.Equal(OutboxDispatchResult.NoWork, await dispatcher.DispatchNextAsync(Token));
        Assert.Equal(
            OutboxDispatchResult.Published,
            await Select(database, publisher, routes).DispatchNextAsync(Token)
        );
        Assert.Equal(
            (IEnumerable<string>)["original", "replacement"],
            publisher.Messages.Select(message => message.RouteKey)
        );
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("blank")]
    [InlineData("null-entry")]
    public void InvalidSelectionFailsBeforeDatabaseIoForDirectAndRegisteredDispatchers(string kind)
    {
        IReadOnlyCollection<string> routes = kind switch
        {
            "empty" => [],
            "blank" => ["accepted", "  "],
            _ => [null!],
        };
        using var database = Context("Host=not-opened;Database=unused;Username=unused");
        Assert.Throws<ArgumentException>(() => Select(database, new RecordingPublisher(), routes));
        Assert.Throws<ArgumentException>(() =>
            new ServiceCollection().AddPostgresOutboxDispatcher<OutboxConsumer, RecordingPublisher>(
                new(TimeSpan.FromSeconds(30), TimeSpan.Zero) { RouteKeys = routes }
            )
        );
    }

    private static OutgoingMessage ForRoute(string route) =>
        new(Guid.NewGuid(), route, "exports.render", 2, Message().Payload);

    private static PostgresOutboxDispatcher<OutboxConsumer> Select(
        OutboxConsumer database,
        RecordingPublisher publisher,
        IReadOnlyCollection<string> routes
    ) =>
        new(
            database,
            publisher,
            new(TimeSpan.FromSeconds(30), TimeSpan.Zero) { RouteKeys = routes }
        );
}
