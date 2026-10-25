using System.Data.Common;
using System.Text.Json;
using Gloaming.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Gloaming.Messaging.Tests;

[Collection("Messaging PostgreSQL")]
public sealed class ContributionTests(PostgreSqlFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Due = new(2026, 11, 5, 0, 0, 0, TimeSpan.Zero);
    private static readonly ContributionSetKey Key = new(
        "technical",
        Guid.Parse("00000000-0000-0000-0000-000000000071"),
        Guid.Parse("00000000-0000-0000-0000-000000000072")
    );
    private static readonly Guid CompleteId = Guid.Parse("00000000-0000-0000-0000-000000000073");
    private static readonly Guid SignalId = Guid.Parse("00000000-0000-0000-0000-000000000074");
    private static readonly Guid TimeoutId = Guid.Parse("00000000-0000-0000-0000-000000000075");
    private static readonly string[] AllExpected = ["a", "b"];

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ExactExpectedIdentitiesCompleteOnceAndRetainOpaqueContents(bool timeout)
    {
        string connection = await SetupAsync(timeout);
        Assert.Equal(ContributionResult.Accepted, await AddAsync(connection, "a", Value(17)));
        await ExpectedAsync(connection, ContributionSetState.Open, 1, 0, timeout);
        Assert.Equal(ContributionResult.Completed, await AddAsync(connection, "b", Value(23)));
        Assert.Equal(ContributionResult.Duplicate, await AddAsync(connection, "a", Value(17)));
        Assert.Equal(ContributionResult.Duplicate, await AddAsync(connection, "b", Value(23)));
        await Assert.ThrowsAsync<ContributionConflictException>(() =>
            AddAsync(connection, "a", Value(18))
        );
        await ExpectedAsync(connection, ContributionSetState.Completed, 2, 1, timeout);
        await using var read = Open(connection);
        var message = await read.Set<OutboxMessageRecord>().SingleAsync(Token);
        Assert.Equal(CompleteId, message.MessageId);
        Assert.Null(message.TenantKey);
        Assert.Equal("consumer.completed", message.MessageName);
        Assert.Equal(99, message.SchemaVersion);
        Assert.Equal("retained", message.Payload.GetProperty("body").GetString());
        Assert.Equal("conversation", message.CorrelationId);
        Assert.Equal("cause", message.CausationId);
        Assert.Equal("vendor=old", message.TraceState);
        var head = await read.Set<ContributionSetRecord>().SingleAsync(Token);
        using var expected = JsonDocument.Parse(
            "{\"Protocol\":1,\"Identities\":[\"a\",\"b\"],\"Required\":2}"
        );
        using var actual = JsonDocument.Parse(head.Expected);
        Assert.True(JsonElement.DeepEquals(expected.RootElement, actual.RootElement));
        Assert.Equal(3, head.Revision);
        Assert.All(
            await read.Set<ContributionRecord>().ToArrayAsync(Token),
            row => Assert.Equal(7, row.SchemaVersion)
        );
    }

    [Fact]
    public async Task UnexpectedContributorAndChangedContractIdentityDoNotAdvanceProgress()
    {
        string connection = await SetupAsync();
        await Assert.ThrowsAsync<ArgumentException>(() => AddAsync(connection, "A", Value(17)));
        Assert.Equal(ContributionResult.Accepted, await AddAsync(connection, "a", Value(17)));
        await Assert.ThrowsAsync<ContributionConflictException>(() =>
            AddAsync(connection, "a", new("other-name", 7, Value(17).Payload))
        );
        await Assert.ThrowsAsync<ContributionConflictException>(() =>
            AddAsync(connection, "a", new("consumer.content", 8, Value(17).Payload))
        );
        await ExpectedAsync(connection, ContributionSetState.Open, 1, 0, true);
    }

    [Fact]
    public async Task ContributionOwnsItsPayloadAndDefinitionFreezesTheExplicitSet()
    {
        var identities = new[] { "b", "a" };
        var definition = Definition(true, identities);
        identities[0] = "wrong";
        Assert.Equal(AllExpected, definition.Expected);
        using var source = JsonDocument.Parse("{\"value\":17}");
        var contribution = new ContributionValue("consumer.content", 7, source.RootElement);
        source.Dispose();
        string connection = await SetupAsync();
        Assert.Equal(ContributionResult.Accepted, await AddAsync(connection, "a", contribution));
        await using var read = Open(connection);
        Assert.Equal(
            17,
            (await read.Set<ContributionRecord>().SingleAsync(Token))
                .Payload.GetProperty("value")
                .GetInt32()
        );
    }

    [Fact]
    public async Task EligibleTimeoutClosesOnceAndRejectsNewArrivals()
    {
        string connection = await SetupAsync();
        Assert.Equal(
            ContributionTimeoutResult.NotDue,
            await TimeoutAsync(connection, Due.AddTicks(-10))
        );
        Assert.Equal(ContributionTimeoutResult.TimedOut, await TimeoutAsync(connection, Due));
        Assert.Equal(
            ContributionTimeoutResult.AlreadyTimedOut,
            await TimeoutAsync(connection, Due.AddDays(1))
        );
        Assert.Equal(
            ContributionResult.AlreadyTimedOut,
            await AddAsync(connection, "a", Value(17))
        );
        await ExpectedAsync(connection, ContributionSetState.TimedOut, 0, 1, true);
        await using var read = Open(connection);
        Assert.Equal(
            TimeoutId,
            (await read.Set<OutboxMessageRecord>().SingleAsync(Token)).MessageId
        );
    }

    [Fact]
    public async Task AlreadyReleasedTimeoutSignalMustRecheckRetainedCompletionAndCannotRecallIt()
    {
        string connection = await SetupAsync();
        await WriteAsync(
            connection,
            async database =>
            {
                Assert.Equal(
                    SignalId,
                    Assert.Single(
                        await new PostgresDueIntents<ContributionDatabase>(database).ReleaseAsync(
                            Key.ScopeKey,
                            Due,
                            1,
                            Token
                        )
                    )
                );
                return true;
            }
        );
        await AddAsync(connection, "a", Value(17));
        Assert.Equal(ContributionResult.Completed, await AddAsync(connection, "b", Value(23)));
        Assert.Equal(
            ContributionTimeoutResult.AlreadyCompleted,
            await TimeoutAsync(connection, Due)
        );
        await ExpectedAsync(
            connection,
            ContributionSetState.Completed,
            2,
            2,
            true,
            DueIntentState.Released
        );
        await using var read = Open(connection);
        Assert.Equal(
            new[] { CompleteId, SignalId },
            await read.Set<OutboxMessageRecord>()
                .Select(row => row.MessageId)
                .Order()
                .ToArrayAsync(Token)
        );
    }

    [Fact]
    public async Task TwoRacingContributorsRetainBothAndOnlyOneCompletion()
    {
        string connection = await SetupAsync();
        await using var first = Open(connection);
        await using var transaction = await first.Database.BeginTransactionAsync(Token);
        Assert.Equal(
            ContributionResult.Accepted,
            await new PostgresContributionSets<ContributionDatabase>(first).AddAsync(
                Key,
                "a",
                Value(17),
                Token
            )
        );
        var second = AddAsync(connection, "b", Value(23));
        await WaitForLockAsync(connection);
        Assert.False(second.IsCompleted);
        await first.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
        Assert.Equal(ContributionResult.Completed, await second);
        await ExpectedAsync(connection, ContributionSetState.Completed, 2, 1, true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RacingEqualOrChangedAttemptsCompareRetainedContent(bool changed)
    {
        string connection = await SetupAsync();
        await using var first = Open(connection);
        await using var transaction = await first.Database.BeginTransactionAsync(Token);
        await new PostgresContributionSets<ContributionDatabase>(first).AddAsync(
            Key,
            "a",
            Value(17),
            Token
        );
        var second = AddAsync(connection, "a", Value(changed ? 18 : 17));
        await WaitForLockAsync(connection);
        await first.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
        if (changed)
            await Assert.ThrowsAsync<ContributionConflictException>(() => second);
        else
            Assert.Equal(ContributionResult.Duplicate, await second);

        await ExpectedAsync(connection, ContributionSetState.Open, 1, 0, true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task LastArrivalAndEligibleTimeoutHaveOneLockedWinner(bool timeoutFirst)
    {
        string connection = await SetupAsync();
        await AddAsync(connection, "a", Value(17));
        await using var first = Open(connection);
        await using var transaction = await first.Database.BeginTransactionAsync(Token);
        var sets = new PostgresContributionSets<ContributionDatabase>(first);
        Task<ContributionResult>? arrival = null;
        Task<ContributionTimeoutResult>? timeout = null;
        if (timeoutFirst)
        {
            Assert.Equal(
                ContributionTimeoutResult.TimedOut,
                await sets.TimeoutAsync(Key, Due, Token)
            );
            arrival = AddAsync(connection, "b", Value(23));
        }
        else
        {
            Assert.Equal(
                ContributionResult.Completed,
                await sets.AddAsync(Key, "b", Value(23), Token)
            );
            timeout = TimeoutAsync(connection, Due);
        }

        await WaitForLockAsync(connection);
        await first.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
        if (timeoutFirst)
            Assert.Equal(ContributionResult.AlreadyTimedOut, await arrival!);
        else
            Assert.Equal(ContributionTimeoutResult.AlreadyCompleted, await timeout!);

        await ExpectedAsync(
            connection,
            timeoutFirst ? ContributionSetState.TimedOut : ContributionSetState.Completed,
            timeoutFirst ? 1 : 2,
            1,
            true
        );
        await using var read = Open(connection);
        Assert.Equal(
            timeoutFirst ? TimeoutId : CompleteId,
            (await read.Set<OutboxMessageRecord>().SingleAsync(Token)).MessageId
        );
    }

    [Theory]
    [InlineData(false, "head")]
    [InlineData(true, "head")]
    [InlineData(false, "content")]
    [InlineData(true, "content")]
    [InlineData(false, "outbox")]
    [InlineData(true, "outbox")]
    [InlineData(false, "timer")]
    [InlineData(true, "timer")]
    [InlineData(false, "accept")]
    [InlineData(true, "accept")]
    [InlineData(false, "detach")]
    [InlineData(true, "detach")]
    [InlineData(false, "original-state")]
    [InlineData(true, "original-state")]
    public async Task EveryCompletionParticipantAndItsOriginalValuesMustSurviveNativeSave(
        bool asynchronous,
        string fault
    )
    {
        string connection = await SetupAsync();
        await AddAsync(connection, "a", Value(17));
        await using var database = Open(connection);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        await new PostgresContributionSets<ContributionDatabase>(database).AddAsync(
            Key,
            "b",
            Value(23),
            Token
        );
        if (fault == "head")
            database.ChangeTracker.Entries<ContributionSetRecord>().Single().State =
                EntityState.Unchanged;
        else if (fault == "content")
            database.ChangeTracker.Entries<ContributionRecord>().Single().State =
                EntityState.Unchanged;
        else if (fault == "outbox")
            database.ChangeTracker.Entries<OutboxMessageRecord>().Single().State =
                EntityState.Unchanged;
        else if (fault == "timer")
            database.ChangeTracker.Entries<DueIntentRecord>().Single().State =
                EntityState.Unchanged;
        else if (fault == "accept")
            database.ChangeTracker.AcceptAllChanges();
        else if (fault == "detach")
            database.ChangeTracker.Entries<ContributionSetRecord>().Single().State =
                EntityState.Detached;
        else
        {
            var state = database
                .ChangeTracker.Entries<ContributionSetRecord>()
                .Single()
                .Property(row => row.State);
            state.OriginalValue = state.CurrentValue;
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SaveAsync(database, asynchronous)
        );
        await transaction.RollbackAsync(Token);
        await ExpectedAsync(connection, ContributionSetState.Open, 1, 0, true);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task GenuineSavedCompletionSupportsExplicitAcceptanceAndRepeatedNativeSaves(
        bool asynchronous,
        bool accept
    )
    {
        string connection = await SetupAsync();
        await AddAsync(connection, "a", Value(17));
        await using var database = Open(connection);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        await new PostgresContributionSets<ContributionDatabase>(database).AddAsync(
            Key,
            "b",
            Value(23),
            Token
        );
        Assert.Equal(4, await SaveAsync(database, asynchronous, accept));
        if (!accept)
            database.ChangeTracker.AcceptAllChanges();

        Assert.Equal(0, await SaveAsync(database, asynchronous));
        await transaction.CommitAsync(Token);
        await ExpectedAsync(connection, ContributionSetState.Completed, 2, 1, true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UndoneSavepointCannotMasqueradeAsStoredCompletion(bool asynchronous)
    {
        string connection = await SetupAsync();
        await AddAsync(connection, "a", Value(17));
        await using var database = Open(connection);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        await transaction.CreateSavepointAsync("before", Token);
        await new PostgresContributionSets<ContributionDatabase>(database).AddAsync(
            Key,
            "b",
            Value(23),
            Token
        );
        await SaveAsync(database, asynchronous);
        await transaction.RollbackToSavepointAsync("before", Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SaveAsync(database, asynchronous)
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ConstraintFailureRollsBackEveryCompletionEffectAndFreshRecoveryWorks(
        bool asynchronous
    )
    {
        string connection = await SetupAsync();
        await AddAsync(connection, "a", Value(17));
        await using (var database = Open(connection))
        {
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            await new PostgresContributionSets<ContributionDatabase>(database).AddAsync(
                Key,
                "b",
                Value(23),
                Token
            );
            database.Add(new ConsumerEffect { Id = 1 });
            var error = await Assert.ThrowsAsync<DbUpdateException>(() =>
                SaveAsync(database, asynchronous)
            );
            Assert.Equal(
                PostgresErrorCodes.NotNullViolation,
                Assert.IsType<PostgresException>(error.InnerException).SqlState
            );
            await transaction.RollbackAsync(Token);
        }

        await ExpectedAsync(connection, ContributionSetState.Open, 1, 0, true);
        Assert.Equal(ContributionResult.Completed, await AddAsync(connection, "b", Value(23)));
        await ExpectedAsync(connection, ContributionSetState.Completed, 2, 1, true);
    }

    [Fact]
    public async Task LostCompletionCommitResponseRecoversRetainedIdentityWithoutAnotherNotice()
    {
        string connection = await SetupAsync();
        await AddAsync(connection, "a", Value(17));
        await using (var database = Open(connection, new LoseResponse()))
        {
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.Equal(
                ContributionResult.Completed,
                await new PostgresContributionSets<ContributionDatabase>(database).AddAsync(
                    Key,
                    "b",
                    Value(23),
                    Token
                )
            );
            await database.SaveChangesAsync(Token);
            await Assert.ThrowsAsync<IOException>(() => transaction.CommitAsync(Token));
        }

        Assert.Equal(ContributionResult.Duplicate, await AddAsync(connection, "b", Value(23)));
        await ExpectedAsync(connection, ContributionSetState.Completed, 2, 1, true);
        await using var read = Open(connection);
        Assert.Equal(
            CompleteId,
            (await read.Set<OutboxMessageRecord>().SingleAsync(Token)).MessageId
        );
    }

    [Fact]
    public async Task ConsumerSelectedMinimumCompletesWithoutAdmittingLateUnrecordedContent()
    {
        string connection = await SetupAsync(false, ["a", "b", "c"], 2);
        Assert.Equal(ContributionResult.Accepted, await AddAsync(connection, "c", Value(31)));
        Assert.Equal(ContributionResult.Completed, await AddAsync(connection, "a", Value(17)));
        Assert.Equal(
            ContributionResult.AlreadyCompleted,
            await AddAsync(connection, "b", Value(23))
        );
        Assert.Equal(ContributionResult.Duplicate, await AddAsync(connection, "a", Value(17)));
        await ExpectedAsync(connection, ContributionSetState.Completed, 2, 1, false);
    }

    [Theory]
    [InlineData("{\"x\":1,\"x\":2}")]
    [InlineData("{\"outer\":[{\"x\":1,\"x\":2}]}")]
    [InlineData("{\"x\":1,\"\\u0078\":2}")]
    public async Task AmbiguousJsonIsRejectedBeforeContentOrTerminalTemplatesCanBeStaged(
        string text
    )
    {
        using var ambiguous = JsonDocument.Parse(text);
        string connection = await SetupAsync();
        await Assert.ThrowsAsync<ArgumentException>(() =>
            WriteAsync(
                connection,
                database =>
                    new PostgresContributionSets<ContributionDatabase>(database).AddAsync(
                        Key,
                        "a",
                        new("consumer.content", 7, ambiguous.RootElement),
                        Token
                    )
            )
        );
        var message = new OutgoingMessage(
            Guid.NewGuid(),
            "route",
            "notice",
            1,
            ambiguous.RootElement
        );
        Assert.Throws<ArgumentException>(() =>
            new ContributionSetDefinition(AllExpected, 2, message, null)
        );
        Assert.Throws<ArgumentException>(() =>
            new ContributionTimeout(Due, message, Message(Guid.NewGuid(), "outcome"))
        );
        Assert.Throws<ArgumentException>(() =>
            new ContributionTimeout(Due, Message(Guid.NewGuid(), "signal"), message)
        );
        await ExpectedAsync(connection, ContributionSetState.Open, 0, 0, true);
    }

    [Fact]
    public async Task AcceptedJsonValueRetriesIgnoreObjectOrderAndNumericFormatting()
    {
        string connection = await SetupAsync();
        using var first = JsonDocument.Parse("{\"b\":[2],\"a\":1.0}");
        using var again = JsonDocument.Parse("{\"a\":1,\"b\":[2.0]}");
        Assert.Equal(
            ContributionResult.Accepted,
            await AddAsync(connection, "a", new("values", 1, first.RootElement))
        );
        Assert.Equal(
            ContributionResult.Duplicate,
            await AddAsync(connection, "a", new("values", 1, again.RootElement))
        );
        await ExpectedAsync(connection, ContributionSetState.Open, 1, 0, true);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeTemplateValidationFailsAtRegistrationBeforeAnyContributionsCanAccumulate(
        bool scalarNul
    )
    {
        string connection = await SetupAsync(false);
        using var number = JsonDocument.Parse("1e1000000");
        var completion = new OutgoingMessage(
            Guid.NewGuid(),
            scalarNul ? "bad\0route" : "route",
            "complete",
            1,
            scalarNul ? JsonSerializer.SerializeToElement(new { valid = true }) : number.RootElement
        );
        var key = new ContributionSetKey("invalid-template", Guid.NewGuid(), Guid.NewGuid());
        await using (var database = Open(connection))
        {
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            new PostgresContributionSets<ContributionDatabase>(database).Register(
                key,
                new(AllExpected, 2, completion, null)
            );
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() =>
                database.SaveChangesAsync(Token)
            );
            _ = Assert.IsType<PostgresException>(failure.InnerException);
            await transaction.RollbackAsync(Token);
        }

        await ExpectedAsync(connection, ContributionSetState.Open, 0, 0, false);
        await using var read = Open(connection);
        Assert.Equal(1, await read.Set<ContributionSetRecord>().CountAsync(Token));
        Assert.Throws<ArgumentException>(() =>
            new ContributionSetDefinition(
                ["valid", "invalid\0identity"],
                2,
                Message(Guid.NewGuid(), "notice"),
                null
            )
        );
    }

    [Fact]
    public async Task ActuallyBlockedContributionCancellationLeavesItsGenerationRecoverable()
    {
        string connection = await SetupAsync();
        await using var first = Open(connection);
        await using var firstTransaction = await first.Database.BeginTransactionAsync(Token);
        await new PostgresContributionSets<ContributionDatabase>(first).AddAsync(
            Key,
            "a",
            Value(17),
            Token
        );
        await using var second = Open(connection);
        await using var secondTransaction = await second.Database.BeginTransactionAsync(Token);
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var blocked = new PostgresContributionSets<ContributionDatabase>(second).AddAsync(
            Key,
            "b",
            Value(23),
            cancelled.Token
        );
        await WaitForLockAsync(connection);
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => blocked);
        await secondTransaction.RollbackAsync(Token);
        await firstTransaction.RollbackAsync(Token);
        await ExpectedAsync(connection, ContributionSetState.Open, 0, 0, true);
        Assert.Equal(ContributionResult.Accepted, await AddAsync(connection, "a", Value(17)));
        Assert.Equal(ContributionResult.Completed, await AddAsync(connection, "b", Value(23)));
    }

    [Theory]
    [InlineData("set-filter")]
    [InlineData("content-filter")]
    [InlineData("set-key")]
    [InlineData("content-key")]
    [InlineData("revision-token")]
    [InlineData("completion-type")]
    [InlineData("roster-type")]
    [InlineData("deadline-precision")]
    [InlineData("content-column")]
    [InlineData("contributor-collation")]
    [InlineData("foreign-key")]
    public void IncompatibleContributionModelsFailBeforeAnyConnection(string fault)
    {
        using var database = new FaultyDatabase(
            new DbContextOptionsBuilder<FaultyDatabase>()
                .UseNpgsql("Host=unused;Database=unused;Username=unused")
                .ReplaceService<IModelCacheKeyFactory, FaultyModelKey>()
                .Options,
            fault
        );
        Assert.Throws<InvalidOperationException>(() =>
            new PostgresContributionSets<FaultyDatabase>(database)
        );
    }

    [Fact]
    public void SupportedContributionModelNeedsNoNativeConnection()
    {
        using var database = Open("Host=unused;Database=unused;Username=unused");
        _ = new PostgresContributionSets<ContributionDatabase>(database);
    }

    [Fact]
    public async Task RegisteredAsyncGuardsUseCancellableNativeStoredParticipantReads()
    {
        string connection = await SetupAsync();
        await AddAsync(connection, "a", Value(17));
        var services = new ServiceCollection();
        services.AddPostgresContributions<RegisteredDatabase>();
        await using var provider = services.BuildServiceProvider();
        var interceptor = new AsyncReadsOnly();
        var options = new DbContextOptionsBuilder<RegisteredDatabase>()
            .UseNpgsql(connection)
            .AddInterceptors(interceptor);
        options.ConfigureGloamingCapabilities<RegisteredDatabase>(provider);
        await using var database = new RegisteredDatabase(options.Options);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        await new PostgresContributionSets<RegisteredDatabase>(database).AddAsync(
            Key,
            "b",
            Value(23),
            Token
        );
        await database.SaveChangesAsync(Token);
        int before = interceptor.Reads;
        Assert.Equal(0, await database.SaveChangesAsync(Token));
        Assert.Equal(before + 4, interceptor.Reads);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            database.SaveChangesAsync(cancelled.Token)
        );
        await transaction.RollbackAsync(Token);
        await ExpectedAsync(connection, ContributionSetState.Open, 1, 0, true);
    }

    private async Task<string> SetupAsync(
        bool timeout = true,
        string[]? expected = null,
        int? required = null
    )
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using (var database = Open(connection))
            await database.Database.EnsureCreatedAsync(Token);

        await WriteAsync(
            connection,
            database =>
            {
                new PostgresContributionSets<ContributionDatabase>(database).Register(
                    Key,
                    Definition(timeout, expected, required)
                );
                return Task.FromResult(true);
            }
        );
        return connection;
    }

    private static ContributionSetDefinition Definition(
        bool timeout,
        string[]? expected = null,
        int? required = null
    ) =>
        new(
            expected ?? AllExpected,
            required ?? (expected ?? AllExpected).Length,
            Message(CompleteId, "consumer.completed"),
            timeout
                ? new(
                    Due,
                    Message(SignalId, "consumer.timeout-signal"),
                    Message(TimeoutId, "consumer.timed-out")
                )
                : null
        );

    private static OutgoingMessage Message(Guid id, string name) =>
        new(
            id,
            "selected-route",
            name,
            99,
            JsonSerializer.SerializeToElement(new { body = "retained" }),
            correlationId: "conversation",
            causationId: "cause",
            traceParent: "00-0123456789abcdef0123456789abcdef-0123456789abcdef-01",
            traceState: "vendor=old"
        );

    private static ContributionValue Value(int value) =>
        new("consumer.content", 7, JsonSerializer.SerializeToElement(new { value }));

    private static ContributionDatabase Open(
        string connection,
        params IInterceptor[] interceptors
    ) =>
        new(
            new DbContextOptionsBuilder<ContributionDatabase>()
                .UseNpgsql(connection)
                .AddInterceptors(interceptors)
                .Options
        );

    private static Task<ContributionResult> AddAsync(
        string connection,
        string contributor,
        ContributionValue value
    ) =>
        WriteAsync(
            connection,
            database =>
                new PostgresContributionSets<ContributionDatabase>(database).AddAsync(
                    Key,
                    contributor,
                    value,
                    Token
                )
        );

    private static Task<ContributionTimeoutResult> TimeoutAsync(
        string connection,
        DateTimeOffset observed
    ) =>
        WriteAsync(
            connection,
            database =>
                new PostgresContributionSets<ContributionDatabase>(database).TimeoutAsync(
                    Key,
                    observed,
                    Token
                )
        );

    private static async Task<T> WriteAsync<T>(
        string connection,
        Func<ContributionDatabase, Task<T>> operation
    )
    {
        await using var database = Open(connection);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        T result = await operation(database);
        await database.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
        return result;
    }

    private static Task<int> SaveAsync(
        ContributionDatabase database,
        bool asynchronous,
        bool accept = true
    ) =>
        asynchronous
            ? database.SaveChangesAsync(accept, Token)
            : Task.FromResult(database.SaveChanges(accept));

    private static async Task ExpectedAsync(
        string connection,
        ContributionSetState state,
        int contributions,
        int outgoing,
        bool timer,
        DueIntentState? timerState = null
    )
    {
        await using var read = Open(connection);
        Assert.Equal(state, (await read.Set<ContributionSetRecord>().SingleAsync(Token)).State);
        Assert.Equal(contributions, await read.Set<ContributionRecord>().CountAsync(Token));
        Assert.Equal(outgoing, await read.Set<OutboxMessageRecord>().CountAsync(Token));
        Assert.Equal(timer ? 1 : 0, await read.Set<DueIntentRecord>().CountAsync(Token));
        if (timer)
            Assert.Equal(
                timerState
                    ?? (
                        state == ContributionSetState.Open
                            ? DueIntentState.Pending
                            : DueIntentState.Cancelled
                    ),
                (await read.Set<DueIntentRecord>().SingleAsync(Token)).State
            );
    }

    private static async Task WaitForLockAsync(string connection)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(15));
        await using var observer = new NpgsqlConnection(connection);
        await observer.OpenAsync(deadline.Token);
        await using var command = new NpgsqlCommand(
            "SELECT count(*) FROM pg_stat_activity WHERE datname = current_database() AND wait_event_type = 'Lock'",
            observer
        );
        while ((long)(await command.ExecuteScalarAsync(deadline.Token))! == 0)
            await Task.Delay(25, deadline.Token);
    }

    private sealed class ContributionDatabase(DbContextOptions<ContributionDatabase> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model)
        {
            model.ConfigurePostgresOutbox("joining", "outbox");
            model.ConfigurePostgresDueIntents("joining");
            model.ConfigurePostgresContributions("joining");
            model
                .Entity<ConsumerEffect>()
                .ToTable("consumer_effects", "joining")
                .HasKey(row => row.Id);
        }

        public override int SaveChanges(bool acceptAllChangesOnSuccess)
        {
            this.ValidateContributionChanges();

            return base.SaveChanges(acceptAllChangesOnSuccess);
        }

        public override async Task<int> SaveChangesAsync(
            bool acceptAllChangesOnSuccess,
            CancellationToken cancellationToken = default
        )
        {
            await this.ValidateContributionChangesAsync(cancellationToken);

            return await base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
        }
    }

    private sealed class ConsumerEffect
    {
        public int Id { get; set; }
        public string Required { get; set; } = null!;
    }

    private sealed class RegisteredDatabase(DbContextOptions<RegisteredDatabase> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model)
        {
            model.ConfigurePostgresOutbox("joining", "outbox");
            model.ConfigurePostgresDueIntents("joining");
            model.ConfigurePostgresContributions("joining");
        }
    }

    private sealed class FaultyDatabase(DbContextOptions<FaultyDatabase> options, string fault)
        : DbContext(options)
    {
        internal string Fault => fault;

        protected override void OnModelCreating(ModelBuilder model)
        {
            model.ConfigurePostgresOutbox("joining", "outbox");
            model.ConfigurePostgresDueIntents("joining");
            model.ConfigurePostgresContributions("joining");
            var set = model.Entity<ContributionSetRecord>();
            var content = model.Entity<ContributionRecord>();
            switch (fault)
            {
                case "set-filter":
                    set.HasQueryFilter("wrong-set", row => row.State == ContributionSetState.Open);
                    break;
                case "content-filter":
                    content.HasQueryFilter("wrong-content", row => row.ContributorId != "hidden");
                    break;
                case "set-key":
                    set.HasKey(row => new
                    {
                        row.ScopeKey,
                        row.GenerationId,
                        row.WorkId,
                    });
                    break;
                case "content-key":
                    content.HasKey(row => new
                    {
                        row.ScopeKey,
                        row.GenerationId,
                        row.WorkId,
                        row.ContributorId,
                    });
                    break;
                case "revision-token":
                    set.Property(row => row.Revision).IsConcurrencyToken(false);
                    break;
                case "completion-type":
                    set.Property(row => row.Completion).HasColumnType("text");
                    break;
                case "roster-type":
                    set.Property(row => row.Expected).HasColumnType("text");
                    break;
                case "deadline-precision":
                    set.Property(row => row.DueAt).HasPrecision(3);
                    break;
                case "content-column":
                    content.Property(row => row.Name).HasColumnName("different");
                    break;
                case "contributor-collation":
                    content.Property(row => row.ContributorId).UseCollation("default");
                    break;
                case "foreign-key":
                    content
                        .HasOne<ContributionSetRecord>()
                        .WithMany()
                        .HasForeignKey(row => new
                        {
                            row.ScopeKey,
                            row.WorkId,
                            row.GenerationId,
                        })
                        .HasPrincipalKey(row => new
                        {
                            row.ScopeKey,
                            row.GenerationId,
                            row.WorkId,
                        })
                        .OnDelete(DeleteBehavior.Restrict);
                    break;
            }
        }
    }

    private sealed class FaultyModelKey : IModelCacheKeyFactory
    {
        public object Create(DbContext context, bool designTime) =>
            (context.GetType(), ((FaultyDatabase)context).Fault, designTime);
    }

    private sealed class AsyncReadsOnly : DbCommandInterceptor
    {
        internal int Reads { get; private set; }

        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result
        ) => throw new InvalidOperationException("This consumer permits only async native reads.");

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default
        )
        {
            Reads++;
            return ValueTask.FromResult(result);
        }
    }

    private sealed class LoseResponse : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default
        ) => throw new IOException("Native completion committed; acknowledgement was lost.");
    }
}
