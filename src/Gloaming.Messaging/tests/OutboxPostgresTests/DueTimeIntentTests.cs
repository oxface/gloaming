using System.Data.Common;
using System.Text.Json;
using Gloaming.Messaging.DueTimeProof.Current;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Npgsql;

namespace Gloaming.Messaging.Tests;

[Collection("Messaging PostgreSQL")]
public sealed partial class DueTimeIntentTests(PostgreSqlFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Due = new(2030, 3, 15, 9, 0, 0, TimeSpan.Zero);
    private const string Scope = "admitted-organization";
    private static readonly int[] EnvelopeItems = [1, 2];

    [Fact]
    public async Task ReleasePreservesTheCompleteSelectedWireEnvelopeThroughNativeDispatch()
    {
        string connection = await SetupAsync();
        Guid delivery = Guid.NewGuid();
        var message = new OutgoingMessage(
            delivery,
            "selected-route",
            "selected.wire-name",
            3,
            JsonSerializer.SerializeToElement(new { title = "résumé", items = EnvelopeItems }),
            Scope,
            "selected-conversation",
            "selected-cause",
            "00-11111111111111111111111111111111-2222222222222222-01",
            "vendor=value"
        );
        await WriteAsync(
            connection,
            database =>
            {
                NativeDueIntents.Stage(
                    database,
                    Scope,
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    Due,
                    message
                );
                return Task.FromResult(true);
            }
        );
        Assert.Equal(delivery, Assert.Single(await ReleaseAsync(connection, Due)));
        var published = await DispatchAsync(connection);
        Assert.Equal(delivery, published.MessageId);
        Assert.Equal("selected-route", published.RouteKey);
        Assert.Equal("selected.wire-name", published.MessageName);
        Assert.Equal(3, published.SchemaVersion);
        Assert.Equal(Scope, published.TenantKey);
        Assert.Equal("selected-conversation", published.CorrelationId);
        Assert.Equal("selected-cause", published.CausationId);
        Assert.Equal(
            "00-11111111111111111111111111111111-2222222222222222-01",
            published.TraceParent
        );
        Assert.Equal("vendor=value", published.TraceState);
        Assert.True(JsonElement.DeepEquals(message.Payload, published.Payload));
    }

    [Fact]
    public async Task ARescheduleCommandReceiptRecoversItsOriginalGenerationAfterLostCommitResponse()
    {
        string connection = await SetupAsync();
        Guid id = Guid.NewGuid();
        Guid action = Guid.NewGuid();
        await OpenAsync(connection, id);
        Replacement original;
        await using (var database = PersonnelContext(connection, new LoseResponse()))
        {
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            original = await Personnel.ReplaceOnceAsync(
                database,
                Scope,
                action,
                id,
                "new-hr",
                Due.AddDays(7),
                Token
            );
            await database.SaveChangesAsync(Token);
            await Assert.ThrowsAsync<IOException>(() => transaction.CommitAsync(Token));
        }

        var replay = await WriteAsync(
            connection,
            database =>
                Personnel.ReplaceOnceAsync(
                    database,
                    Scope,
                    action,
                    id,
                    "new-hr",
                    Due.AddDays(7),
                    Token
                )
        );
        Assert.Equal(original, replay);
        await Assert.ThrowsAsync<Gloaming.CommandReceipts.EntityFrameworkCore.Postgres.CommandReceiptConflictException>(
            () =>
                WriteAsync(
                    connection,
                    database =>
                        Personnel.ReplaceOnceAsync(
                            database,
                            Scope,
                            action,
                            id,
                            "new-hr",
                            Due.AddDays(8),
                            Token
                        )
                )
        );
        await using (var read = PersonnelContext(connection))
        {
            Assert.Equal(2, await read.Set<DueIntent>().CountAsync(Token));
            Assert.Equal(
                original.Occurrence,
                (await read.Set<ProbationCase>().SingleAsync(Token)).OccurrenceId
            );
        }

        Assert.Empty(await ReleaseAsync(connection, Due));
        Assert.Single(await ReleaseAsync(connection, Due.AddDays(7)));
        Assert.Equal(
            Acceptance.Accepted,
            await AcceptAsync(connection, await DispatchAsync(connection), Due.AddDays(7))
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RolledBackBusinessCreationLeavesNoDueIntentOrOutbox(bool save)
    {
        string connection = await SetupAsync();
        await using (var database = PersonnelContext(connection))
        {
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Personnel.Open(database, Scope, Guid.NewGuid(), "assigned-hr", Due);
            if (save)
                await database.SaveChangesAsync(Token);

            await transaction.RollbackAsync(Token);
        }

        await using var read = PersonnelContext(connection);
        Assert.Equal(0, await read.Set<ProbationCase>().CountAsync(Token));
        Assert.Equal(0, await read.Set<DueIntent>().CountAsync(Token));
        Assert.Equal(0, await read.Set<OutboxMessageRecord>().CountAsync(Token));
    }

    [Fact]
    public async Task ReleaseAndAcceptanceUseTheSelectedInstantWithoutEarlyBusinessEffects()
    {
        string connection = await SetupAsync();
        Guid id = Guid.NewGuid();
        await OpenAsync(connection, id);
        Assert.Empty(await ReleaseAsync(connection, Due.AddTicks(-10)));
        Assert.Single(await ReleaseAsync(connection, Due));
        var message = await DispatchAsync(connection);
        Assert.Equal(Acceptance.NotDue, await AcceptAsync(connection, message, Due.AddTicks(-10)));
        Assert.Equal(Acceptance.Accepted, await AcceptAsync(connection, message, Due));
        Assert.Empty(await ReleaseAsync(connection, Due.AddYears(1)));
        await using var read = PersonnelContext(connection);
        Assert.Equal(
            "assigned-hr",
            (await read.Set<ReminderNotice>().SingleAsync(Token)).Recipient
        );
        Assert.Equal(IntentState.Released, (await read.Set<DueIntent>().SingleAsync(Token)).State);
    }

    [Fact]
    public async Task LateRecoveryReleasesBoundedPersistedWorkOnceWithoutPlanningRecurrences()
    {
        string connection = await SetupAsync();
        for (int index = 0; index < 3; index++)
            await OpenAsync(connection, Guid.NewGuid(), due: Due.AddDays(index));

        Assert.Equal(2, (await ReleaseAsync(connection, Due.AddYears(1), maximum: 2)).Length);
        Assert.Single(await ReleaseAsync(connection, Due.AddYears(1), maximum: 2));
        Assert.Empty(await ReleaseAsync(connection, Due.AddYears(2), maximum: 2));
        await using var read = PersonnelContext(connection);
        Assert.Equal(3, await read.Set<DueIntent>().CountAsync(Token));
        Assert.Equal(3, await read.Set<OutboxMessageRecord>().CountAsync(Token));
        Assert.Equal(0, await read.Set<ReminderNotice>().CountAsync(Token));
    }

    [Fact]
    public async Task AFailedReleaseSaveRollsBackBothTheTransitionAndItsOutbox()
    {
        string connection = await SetupAsync();
        await OpenAsync(connection, Guid.NewGuid());
        await using (var database = PersonnelContext(connection))
        {
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            await NativeDueIntents.ReleaseAsync(database, Scope, Due, 1, Token);
            database.Add(new ProbationCase { ScopeKey = Scope, Id = Guid.NewGuid() });
            await Assert.ThrowsAsync<DbUpdateException>(() => database.SaveChangesAsync(Token));
        }

        await using var read = PersonnelContext(connection);
        Assert.Equal(IntentState.Pending, (await read.Set<DueIntent>().SingleAsync(Token)).State);
        Assert.Equal(0, await read.Set<OutboxMessageRecord>().CountAsync(Token));
        Assert.Single(await ReleaseAsync(connection, Due));
    }

    [Fact]
    public async Task ExtensionAndReassignmentSuppressOldIntentAndRejectItsObsoleteGeneration()
    {
        string connection = await SetupAsync();
        Guid id = Guid.NewGuid();
        Guid old = await OpenAsync(connection, id);
        var replacement = await WriteAsync(
            connection,
            database => Personnel.ReplaceAsync(database, Scope, id, "new-hr", Due.AddDays(7), Token)
        );
        Assert.Equal(Suppression.Suppressed, replacement.Previous);
        var stale = Payloads.Create(Scope, Personnel.Route, id, old, 1);
        Assert.Equal(Acceptance.Stale, await AcceptAsync(connection, stale, Due.AddDays(7)));
        Assert.Empty(await ReleaseAsync(connection, Due));
        Assert.Single(await ReleaseAsync(connection, Due.AddDays(7)));
        Assert.Equal(
            Acceptance.Accepted,
            await AcceptAsync(connection, await DispatchAsync(connection), Due.AddDays(7))
        );
        await using var read = PersonnelContext(connection);
        Assert.Equal("new-hr", (await read.Set<ReminderNotice>().SingleAsync(Token)).Recipient);
        Assert.Equal(2, await read.Set<DueIntent>().CountAsync(Token));
    }

    [Theory]
    [InlineData(2)]
    [InlineData(3)]
    public async Task CompletionOrCancellationRejectsAnAlreadyReleasedOccurrence(int status)
    {
        string connection = await SetupAsync();
        Guid id = Guid.NewGuid();
        await OpenAsync(connection, id);
        await ReleaseAsync(connection, Due);
        Assert.Equal(
            Suppression.AlreadyReleased,
            await WriteAsync(
                connection,
                database => Personnel.FinishAsync(database, Scope, id, (CaseStatus)status, Token)
            )
        );
        var delivered = await DispatchAsync(connection);
        Assert.Equal(Acceptance.Stale, await AcceptAsync(connection, delivered, Due));
        await using var read = PersonnelContext(connection);
        Assert.Equal(0, await read.Set<ReminderNotice>().CountAsync(Token));
    }

    [Fact]
    public async Task CancellationCannotRecallAnAlreadyAcceptedNotificationIntent()
    {
        string connection = await SetupAsync();
        Guid id = Guid.NewGuid();
        await OpenAsync(connection, id);
        await ReleaseAsync(connection, Due);
        var message = await DispatchAsync(connection);
        Assert.Equal(Acceptance.Accepted, await AcceptAsync(connection, message, Due));
        await WriteAsync(
            connection,
            database => Personnel.FinishAsync(database, Scope, id, CaseStatus.Cancelled, Token)
        );
        Assert.Equal(Acceptance.Stale, await AcceptAsync(connection, message, Due));
        await using var read = PersonnelContext(connection);
        Assert.Equal(1, await read.Set<ReminderNotice>().CountAsync(Token));
    }

    [Fact]
    public async Task ReassignmentAfterReleaseUsesTheNewRecipientOnlyForTheNewGeneration()
    {
        string connection = await SetupAsync();
        Guid id = Guid.NewGuid();
        await OpenAsync(connection, id);
        await ReleaseAsync(connection, Due);
        var old = await DispatchAsync(connection);
        Assert.Equal(
            Suppression.AlreadyReleased,
            (
                await WriteAsync(
                    connection,
                    database =>
                        Personnel.ReplaceAsync(database, Scope, id, "new-hr", Due.AddDays(1), Token)
                )
            ).Previous
        );
        Assert.Equal(Acceptance.Stale, await AcceptAsync(connection, old, Due.AddDays(1)));
        await ReleaseAsync(connection, Due.AddDays(1));
        Assert.Equal(
            Acceptance.Accepted,
            await AcceptAsync(connection, await DispatchAsync(connection), Due.AddDays(1))
        );
        await using var read = PersonnelContext(connection);
        Assert.Equal("new-hr", (await read.Set<ReminderNotice>().SingleAsync(Token)).Recipient);
    }

    [Fact]
    public async Task ConcurrentDuplicateDeliveriesCreateOneNotificationIntent()
    {
        string connection = await SetupAsync();
        await OpenAsync(connection, Guid.NewGuid());
        await ReleaseAsync(connection, Due);
        var message = await DispatchAsync(connection);
        var results = await Task.WhenAll(
            AcceptAsync(connection, message, Due),
            AcceptAsync(connection, message, Due)
        );
        Assert.Contains(Acceptance.Accepted, results);
        Assert.Contains(Acceptance.Duplicate, results);
        await using var read = PersonnelContext(connection);
        Assert.Equal(1, await read.Set<ReminderNotice>().CountAsync(Token));
    }

    [Fact]
    public async Task ScopeAndModuleIdentityDoNotShareSelectionOrBusinessEffects()
    {
        string connection = await SetupAsync();
        Guid id = Guid.NewGuid();
        await OpenAsync(connection, id, scope: "Tenant");
        await OpenAsync(connection, id, scope: "tenant");
        await using (var database = GrowthContext(connection))
        {
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            await Growth.ApprovedClosureAsync(database, "Tenant", id, Due.AddYears(-1), Due, Token);
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }

        Assert.Single(await ReleaseAsync(connection, Due, scope: "Tenant"));
        var message = await DispatchAsync(connection);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            AcceptAsync(connection, message, Due, scope: "tenant")
        );
        Assert.Equal(
            Acceptance.Accepted,
            await AcceptAsync(connection, message, Due, scope: "Tenant")
        );
        await using var personnel = PersonnelContext(connection);
        Assert.Equal(
            1,
            await personnel
                .Set<DueIntent>()
                .CountAsync(row => row.State == IntentState.Pending, Token)
        );
        await using var growth = GrowthContext(connection);
        Assert.Equal(IntentState.Pending, (await growth.Set<DueIntent>().SingleAsync(Token)).State);
        Assert.Equal(0, await growth.Set<PreparationIndication>().CountAsync(Token));
        await using var growthTransaction = await growth.Database.BeginTransactionAsync(Token);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            Growth.AcceptAsync(growth, "Tenant", message, Due, Token)
        );
    }

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task UnboundedOrEmptyBatchRequestsAreRejected(int maximum)
    {
        string connection = await SetupAsync();
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            ReleaseAsync(connection, Due, maximum)
        );
    }

    [Fact]
    public async Task NativeClockPrecisionAndTransactionRequirementsAreExplicit()
    {
        string connection = await SetupAsync();
        await using var database = PersonnelContext(connection);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NativeDueIntents.ReleaseAsync(database, Scope, Due, 1, Token)
        );
        await Assert.ThrowsAsync<ArgumentException>(() =>
            ReleaseAsync(connection, Due.AddTicks(1))
        );
        await Assert.ThrowsAsync<ArgumentException>(() =>
            ReleaseAsync(connection, Due.ToOffset(TimeSpan.FromHours(2)))
        );
    }

    private async Task<string> SetupAsync()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using var personnel = PersonnelContext(connection);
        await personnel.Database.EnsureCreatedAsync(Token);
        await using var growth = GrowthContext(connection);
        await growth.Database.ExecuteSqlRawAsync(growth.Database.GenerateCreateScript(), Token);
        return connection;
    }

    private static PersonnelDatabase PersonnelContext(
        string connection,
        params IInterceptor[] interceptors
    ) =>
        new(
            new DbContextOptionsBuilder<PersonnelDatabase>()
                .UseNpgsql(connection)
                .AddInterceptors(interceptors)
                .Options
        );

    private static GrowthDatabase GrowthContext(string connection) =>
        new(new DbContextOptionsBuilder<GrowthDatabase>().UseNpgsql(connection).Options);

    private static async Task<T> WriteAsync<T>(
        string connection,
        Func<PersonnelDatabase, Task<T>> action
    )
    {
        await using var database = PersonnelContext(connection);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        T result = await action(database);
        await database.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
        return result;
    }

    private static Task<Guid> OpenAsync(
        string connection,
        Guid id,
        string scope = Scope,
        DateTimeOffset? due = null,
        int version = 1
    ) =>
        WriteAsync(
            connection,
            database =>
                Task.FromResult(
                    Personnel.Open(database, scope, id, "assigned-hr", due ?? Due, version)
                )
        );

    private static Task<Guid[]> ReleaseAsync(
        string connection,
        DateTimeOffset now,
        int maximum = 1,
        string scope = Scope
    ) =>
        WriteAsync(
            connection,
            database => NativeDueIntents.ReleaseAsync(database, scope, now, maximum, Token)
        );

    private static Task<Acceptance> AcceptAsync(
        string connection,
        OutgoingMessage message,
        DateTimeOffset now,
        string scope = Scope
    ) =>
        WriteAsync(
            connection,
            database => Personnel.AcceptAsync(database, scope, message, now, Token)
        );

    private static async Task<OutgoingMessage> DispatchAsync(string connection)
    {
        await using var database = PersonnelContext(connection);
        var publisher = new RecordingPublisher();
        var dispatcher = new PostgresOutboxDispatcher<PersonnelDatabase>(
            database,
            publisher,
            new(TimeSpan.FromSeconds(30), TimeSpan.Zero)
        );
        Assert.Equal(OutboxDispatchResult.Published, await dispatcher.DispatchNextAsync(Token));
        return Assert.Single(publisher.Messages);
    }

    private static async Task WaitForBlockedAsync(string connection)
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

    private sealed class LoseResponse : DbTransactionInterceptor
    {
        public override Task TransactionCommittedAsync(
            DbTransaction transaction,
            TransactionEndEventData eventData,
            CancellationToken cancellationToken = default
        ) => throw new IOException("The native commit succeeded; its response was lost.");
    }
}
