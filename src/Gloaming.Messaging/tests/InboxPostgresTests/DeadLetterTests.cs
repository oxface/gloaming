using System.Data.Common;
using System.Security.Cryptography;
using System.Text.Json;
using System.Transactions;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Tests.Infrastructure;
using static Gloaming.Messaging.InboxTests.InboxProof;

namespace Gloaming.Messaging.InboxTests;

public sealed class DeadLetterTests(PostgreSqlFixture postgres)
{
    [Fact]
    public async Task SharedCodecPreservesTheExistingInboxCursorRepresentation()
    {
        string connection = await DatabaseAsync(postgres);
        await using var provider = DeadLetterProvider(connection);
        await RetainFailureAsync(provider, Message());
        await RetainFailureAsync(provider, Message());
        await using var scope = provider.CreateAsyncScope();
        var letters = scope.ServiceProvider.GetRequiredService<IInboxDeadLetters<InboxConsumer>>();
        var first = await letters.ListAsync("render", "exports", new(1, "review"), Token);
        var item = Assert.Single(first.Items);
        // The format below is the pre-extraction private inbox codec, assembled independently.
        string[] lane = ["render", "exports", "review"];
        string fingerprint = Convert.ToHexString(
            SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(lane))
        );
        string legacy = Convert
            .ToBase64String(
                JsonSerializer.SerializeToUtf8Bytes(
                    new
                    {
                        Version = 1,
                        Scope = fingerprint,
                        item.DeadLetteredAt,
                        item.MessageId,
                    }
                )
            )
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
        Assert.Equal(legacy, first.NextCursor);
        var next = await letters.ListAsync("render", "exports", new(1, "review", legacy), Token);
        Assert.NotEqual(item.MessageId, Assert.Single(next.Items).MessageId);
    }

    [Fact]
    public async Task PagesUseDatabaseUuidOrderAtTiedTimesAndFilterExactLaneAndCode()
    {
        string connection = await DatabaseAsync(postgres);
        await using var provider = DeadLetterProvider(connection);
        Guid[] ids =
        [
            Guid.Parse("ffffffff-0000-0000-0000-000000000001"),
            Guid.Parse("00000001-0000-0000-0000-000000000001"),
            Guid.Parse("10000000-0000-0000-0000-000000000001"),
            Guid.Parse("000000ff-0000-0000-0000-000000000001"),
            Guid.Parse("80000000-0000-0000-0000-000000000001"),
        ];
        foreach (Guid id in ids)
            await RetainFailureAsync(provider, Message(id));

        await RetainFailureAsync(provider, Message(producer: "other"));
        await RetainFailureAsync(provider, Message(), "render.other");
        provider.GetRequiredService<FailureRecoveryTests.FailureScenario>().Decide =
            _ => new InboxFailureDecision.DeadLetter("Review");
        await RetainFailureAsync(provider, Message());
        await SqlAsync(
            connection,
            "UPDATE receiver.inbox SET quarantined_at = '2026-10-01T00:00:00Z'"
        );
        await using var scope = provider.CreateAsyncScope();
        var letters = scope.ServiceProvider.GetRequiredService<IInboxDeadLetters<InboxConsumer>>();
        var first = await letters.ListAsync("render", "exports", new(2, "review"), Token);
        Assert.Equal(
            ids.OrderBy(id => id.ToString("D"), StringComparer.Ordinal).Take(2),
            first.Items.Select(item => item.MessageId)
        );
        Assert.NotNull(first.NextCursor);
        var second = await letters.ListAsync(
            "render",
            "exports",
            new(2, "review", first.NextCursor),
            Token
        );
        var third = await letters.ListAsync(
            "render",
            "exports",
            new(2, "review", second.NextCursor),
            Token
        );
        Assert.Single(third.Items);
        Assert.Null(third.NextCursor);
        var all = first.Items.Concat(second.Items).Concat(third.Items).ToArray();
        Assert.Equal(
            ids.OrderBy(id => id.ToString("D"), StringComparer.Ordinal),
            all.Select(item => item.MessageId)
        );
        Assert.Equal(5, all.Select(item => item.MessageId).Distinct().Count());
        Assert.All(all, item => Assert.Equal("review", item.LastFailureCode));
        Assert.Single(
            (await letters.ListAsync("render", "exports", new(50, "Review"), Token)).Items
        );
        Assert.Empty((await letters.ListAsync("Render", "exports", new(), Token)).Items);
        Assert.Empty(
            (await letters.ListAsync("render", "exports", new(50, "missing"), Token)).Items
        );
        Assert.Single((await letters.ListAsync("render.other", "exports", new(), Token)).Items);
        Assert.Single((await letters.ListAsync("render", "other", new(), Token)).Items);
        Assert.Empty(
            scope.ServiceProvider.GetRequiredService<InboxConsumer>().ChangeTracker.Entries()
        );

        // A position can change page size but cannot be reused for another lane/filter.
        Assert.Equal(
            3,
            (
                await letters.ListAsync(
                    "render",
                    "exports",
                    new(100, "review", first.NextCursor),
                    Token
                )
            )
                .Items
                .Count
        );
        const string unavailable =
            "Host=127.0.0.1;Port=1;Database=absent;Username=unused;Timeout=1";
        await using var offline = DeadLetterProvider(unavailable);
        await using var offlineScope = offline.CreateAsyncScope();
        var offlineLetters = offlineScope.ServiceProvider.GetRequiredService<
            IInboxDeadLetters<InboxConsumer>
        >();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            offlineLetters.ListAsync("other", "exports", new(2, "review", first.NextCursor), Token)
        );
        await Assert.ThrowsAsync<ArgumentException>(() =>
            offlineLetters.ListAsync("render", "other", new(2, "review", first.NextCursor), Token)
        );
        await Assert.ThrowsAsync<ArgumentException>(() =>
            offlineLetters.ListAsync("render", "exports", new(2, "Review", first.NextCursor), Token)
        );
    }

    [Fact]
    public async Task InspectionSelectsDetachedMetadataAndExcludesOtherLifecycleStates()
    {
        string connection = await DatabaseAsync(postgres);
        var capture = new ReadCapture();
        await using var provider = DeadLetterProvider(connection, capture);
        var original = Message(json: "{\"private-payload\":\"must-not-be-selected\"}");
        await RetainFailureAsync(provider, original);
        var pending = Message();
        await ReceiveAsync(provider, pending);
        await using (var success = Provider(connection))
            Assert.Equal(InboxProcessingResult.Processed, await ProcessAsync(success));
        var waiting = Message();
        await ReceiveAsync(provider, waiting);

        capture.Commands.Clear();
        await using var scope = provider.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<InboxConsumer>();
        var letters = scope.ServiceProvider.GetRequiredService<IInboxDeadLetters<InboxConsumer>>();
        var item = Assert.IsType<InboxDeadLetter>(
            await letters.GetAsync("render", "exports", original.MessageId, Token)
        );
        Assert.Equal(original.MessageId, item.MessageId);
        Assert.Equal(original.MessageName, item.MessageName);
        Assert.Equal(original.SchemaVersion, item.SchemaVersion);
        Assert.Equal(1, item.Revision);
        Assert.Equal(1, item.FailureCount);
        Assert.Equal(0, item.RedriveCount);
        Assert.Single((await letters.ListAsync("render", "exports", new(), Token)).Items);
        Assert.Null(await letters.GetAsync("render", "exports", pending.MessageId, Token));
        Assert.Null(await letters.GetAsync("render", "exports", waiting.MessageId, Token));
        Assert.Null(await letters.GetAsync("other", "exports", original.MessageId, Token));
        Assert.Null(await letters.GetAsync("render", "other", original.MessageId, Token));
        Assert.Null(await letters.GetAsync("render", "exports", Guid.NewGuid(), Token));
        Assert.Empty(database.ChangeTracker.Entries());
        Assert.Null(database.Database.CurrentTransaction);
        Assert.NotEmpty(capture.Commands);
        foreach (string sql in capture.Commands)
        {
            Assert.DoesNotContain("\"payload\"", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("\"tenant_key\"", sql, StringComparison.Ordinal);
            Assert.DoesNotContain("FOR UPDATE", sql, StringComparison.Ordinal);
        }

        string json = JsonSerializer.Serialize(item);
        Assert.DoesNotContain("payload", json, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("TenantKey", json, StringComparison.Ordinal);
        Assert.DoesNotContain("CorrelationId", json, StringComparison.Ordinal);
        Assert.DoesNotContain("Exception", json, StringComparison.Ordinal);
        Assert.Equal(
            InboxRetryResult.Retried,
            await letters.RetryAsync("render", "exports", original.MessageId, item.Revision, Token)
        );
        Assert.Null(await letters.GetAsync("render", "exports", original.MessageId, Token));
        Assert.Empty((await letters.ListAsync("render", "exports", new(), Token)).Items);
    }

    [Theory]
    [InlineData(0, null, null)]
    [InlineData(101, null, null)]
    [InlineData(-1, null, null)]
    [InlineData(1, "", null)]
    [InlineData(1, " ", null)]
    [InlineData(1, null, "")]
    [InlineData(1, null, "not-a-cursor!")]
    [InlineData(1, null, "bnVsbA")]
    [InlineData(1, null, "e30")]
    public async Task InvalidQueriesRejectBeforeOpeningStorage(
        int size,
        string? code,
        string? after
    )
    {
        await using var provider = DeadLetterProvider(
            "Host=127.0.0.1;Port=1;Database=absent;Username=unused;Timeout=1"
        );
        await using var scope = provider.CreateAsyncScope();
        var letters = scope.ServiceProvider.GetRequiredService<IInboxDeadLetters<InboxConsumer>>();
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            letters.ListAsync("render", "exports", new(size, code, after), Token)
        );
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            letters.ListAsync("render", "exports", new(1, new string('x', 129)), Token)
        );
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            letters.ListAsync("render", "exports", new(1, After: new string('x', 1025)), Token)
        );
    }

    [Fact]
    public async Task ReadsRejectDirtyOrOwnedTransactionsAndObserveCancellation()
    {
        string connection = await DatabaseAsync(postgres);
        await using var provider = DeadLetterProvider(connection);
        await using var scope = provider.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<InboxConsumer>();
        var letters = scope.ServiceProvider.GetRequiredService<IInboxDeadLetters<InboxConsumer>>();
        database.Add(new HandledItem { Id = Guid.NewGuid() });
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            letters.ListAsync("render", "exports", new(), Token)
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            letters.GetAsync("render", "exports", Guid.NewGuid(), Token)
        );
        database.ChangeTracker.Clear();
        await using (var transaction = await database.Database.BeginTransactionAsync(Token))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                letters.ListAsync("render", "exports", new(), Token)
            );
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                letters.GetAsync("render", "exports", Guid.NewGuid(), Token)
            );
        }

        using (var ambient = new TransactionScope(TransactionScopeAsyncFlowOption.Enabled))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                letters.ListAsync("render", "exports", new(), Token)
            );
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                letters.GetAsync("render", "exports", Guid.NewGuid(), Token)
            );
        }

        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            letters.ListAsync("render", "exports", new(), cancelled.Token)
        );
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            letters.GetAsync("render", "exports", Guid.NewGuid(), cancelled.Token)
        );
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            letters.ListAsync(" ", "exports", new(), Token)
        );
        await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            letters.GetAsync("render", "exports", Guid.Empty, Token)
        );
    }

    [Fact]
    public async Task LiveInspectionDoesNotWaitForRetryAndAnObservedRevisionCanBecomeStale()
    {
        string connection = await DatabaseAsync(postgres);
        await using var provider = DeadLetterProvider(connection);
        var message = Message();
        await RetainFailureAsync(provider, message);
        await using var scope = provider.CreateAsyncScope();
        var letters = scope.ServiceProvider.GetRequiredService<IInboxDeadLetters<InboxConsumer>>();
        var before = (await letters.GetAsync("render", "exports", message.MessageId, Token))!;
        await using (var owner = Context(connection))
        await using (var transaction = await owner.Database.BeginTransactionAsync(Token))
        {
            await owner.Database.ExecuteSqlRawAsync(
                """
                UPDATE receiver.inbox SET quarantined_at = NULL, revision = revision + 1,
                    failures_since_redrive = 0, redrive_count = redrive_count + 1,
                    last_redriven_at = clock_timestamp()
                """,
                Token
            );
            Assert.Equal(
                before,
                await letters
                    .GetAsync("render", "exports", message.MessageId, Token)
                    .WaitAsync(TimeSpan.FromSeconds(5), Token)
            );
            Assert.Single(
                (
                    await letters
                        .ListAsync("render", "exports", new(), Token)
                        .WaitAsync(TimeSpan.FromSeconds(5), Token)
                ).Items
            );
            await transaction.CommitAsync(Token);
        }

        Assert.Null(await letters.GetAsync("render", "exports", message.MessageId, Token));
        await Assert.ThrowsAsync<IOException>(() => ProcessAsync(provider));
        var again = (await letters.GetAsync("render", "exports", message.MessageId, Token))!;
        Assert.Equal(before.FailureCount + 1, again.FailureCount);
        Assert.Equal(before.Revision + 2, again.Revision);
        Assert.Equal(1, again.FailuresSinceRedrive);
        Assert.Equal(1, again.RedriveCount);
        Assert.NotNull(again.LastRedrivenAt);
        Assert.Equal(before.ReceivedAt, again.ReceivedAt);
        Assert.True(again.DeadLetteredAt >= before.DeadLetteredAt);
        Assert.Equal(
            InboxRetryResult.NotApplied,
            await letters.RetryAsync("render", "exports", message.MessageId, before.Revision, Token)
        );
        Assert.Equal(again, await letters.GetAsync("render", "exports", message.MessageId, Token));
        Assert.Equal(
            InboxRetryResult.Retried,
            await letters.RetryAsync("render", "exports", message.MessageId, again.Revision, Token)
        );
    }

    [Fact]
    public async Task ContinuationToleratesDisappearingRowsAndFreshProvidersAfterRetry()
    {
        string connection = await DatabaseAsync(postgres);
        await using var provider = DeadLetterProvider(connection);
        for (int i = 0; i < 4; i++)
            await RetainFailureAsync(provider, Message());

        await using var scope = provider.CreateAsyncScope();
        var letters = scope.ServiceProvider.GetRequiredService<IInboxDeadLetters<InboxConsumer>>();
        var page = await letters.ListAsync("render", "exports", new(2), Token);
        Assert.NotNull(page.NextCursor);
        foreach (var item in (await letters.ListAsync("render", "exports", new(), Token)).Items)
            Assert.Equal(
                InboxRetryResult.Retried,
                await letters.RetryAsync("render", "exports", item.MessageId, item.Revision, Token)
            );

        await using var restarted = DeadLetterProvider(connection);
        await using var fresh = restarted.CreateAsyncScope();
        var continued = await fresh
            .ServiceProvider.GetRequiredService<IInboxDeadLetters<InboxConsumer>>()
            .ListAsync("render", "exports", new(2, After: page.NextCursor), Token);
        Assert.Empty(continued.Items);
        Assert.Null(continued.NextCursor);
        await Assert.ThrowsAsync<IOException>(() => ProcessAsync(restarted));
        Assert.Single((await letters.ListAsync("render", "exports", new(), Token)).Items);
    }

    private static ServiceProvider DeadLetterProvider(
        string connection,
        DbCommandInterceptor? interceptor = null
    )
    {
        var services = Services(
            connection,
            new HandlerScenario
            {
                OnHandle = (_, _, _) => throw new IOException("processing failed"),
            }
        );
        services.AddSingleton(
            new FailureRecoveryTests.FailureScenario
            {
                Decide = _ => new InboxFailureDecision.DeadLetter("review"),
            }
        );
        services.AddInboxFailurePolicy<InboxConsumer, FailureRecoveryTests.Policy>("render");
        services.AddInboxHandler<InboxConsumer, LocalHandler>("render.other");
        services.AddInboxFailurePolicy<InboxConsumer, FailureRecoveryTests.Policy>("render.other");
        services.AddPostgresInboxDeadLetters<InboxConsumer>();
        if (interceptor is not null)
            services.AddDbContext<InboxConsumer>(options => options.AddInterceptors(interceptor));

        return services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
        );
    }

    private static async Task RetainFailureAsync(
        ServiceProvider provider,
        IncomingMessage message,
        string subscription = "render"
    )
    {
        await ReceiveAsync(provider, message, subscription);
        await Assert.ThrowsAsync<IOException>(() =>
            ProcessAsync(provider, subscription: subscription)
        );
    }

    private sealed class ReadCapture : DbCommandInterceptor
    {
        internal List<string> Commands { get; } = [];

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            Commands.Add(command.CommandText);
            return ValueTask.FromResult(result);
        }
    }
}
