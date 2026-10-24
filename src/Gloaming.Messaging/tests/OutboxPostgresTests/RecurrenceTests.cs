using System.Data.Common;
using System.Text.Json;
using Gloaming.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Gloaming.Messaging.Recurrence.EntityFrameworkCore.Postgres;
using Gloaming.Messaging.RecurrenceProof;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Gloaming.Messaging.Tests;

[Collection("Messaging PostgreSQL")]
public sealed class RecurrenceTests(PostgreSqlFixture postgres)
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly DateTimeOffset Anchor = new(2026, 11, 4, 0, 0, 0, TimeSpan.Zero);
    private static readonly RecurringIntentKey Key = new(
        "maintenance",
        Guid.Parse("00000000-0000-0000-0000-000000000051"),
        Guid.Parse("00000000-0000-0000-0000-000000000052")
    );

    [Fact]
    public async Task ElapsedMaintenanceArmsOnlyOneFutureOccurrenceAcrossRepeatedEarlyPolls()
    {
        var definition = Interval(MissedOccurrences.CatchUp(2));
        string connection = await SetupAsync(definition);
        Assert.Equal(new(1, 0, false), await PlanAsync(connection, definition, Anchor));
        Assert.Equal(new(0, 0, false), await PlanAsync(connection, definition, Anchor));
        Assert.Equal(
            new(0, 0, false),
            await PlanAsync(connection, definition, Anchor.AddMinutes(4))
        );
        await ExpectedAsync(connection, Anchor.AddMinutes(5));
        Assert.Equal(
            new(1, 0, false),
            await PlanAsync(connection, definition, Anchor.AddMinutes(5))
        );
        await ExpectedAsync(connection, Anchor.AddMinutes(5), Anchor.AddMinutes(10));
        await using var read = Open(connection);
        var first = await read.Set<DueIntentRecord>().OrderBy(row => row.DueAt).FirstAsync(Token);
        Assert.Null(first.TenantKey);
        Assert.Equal(Guid.Parse("d9e36c33-cdad-76b7-c7dc-27b1af089f87"), first.MessageId);
        Assert.Equal(Guid.Parse("a8c130de-fc40-fc6b-1971-075f23057b7d"), first.OccurrenceId);
        Assert.Equal(Guid.Parse("22409c99-f391-4b7f-97fb-c3fd557bc02a"), first.ScheduleId);
        Assert.Equal("maintenance.rotate", first.MessageName);
        Assert.Equal("storage", first.Payload.GetProperty("partition").GetString());
    }

    [Fact]
    public async Task CatchUpRestartsFromCommittedProgressAndRespectsItsEmissionBound()
    {
        var definition = Interval(MissedOccurrences.CatchUp(2));
        string connection = await SetupAsync(definition);
        Assert.Equal(
            new(2, 2, true),
            await PlanAsync(connection, definition, Anchor.AddMinutes(25))
        );
        await ExpectedAsync(connection, Anchor.AddMinutes(5), Anchor.AddMinutes(10));
        Assert.Equal(
            new(2, 2, true),
            await PlanAsync(connection, definition, Anchor.AddMinutes(25))
        );
        Assert.Equal(
            new(2, 1, false),
            await PlanAsync(connection, definition, Anchor.AddMinutes(25))
        );
        Assert.Equal(
            new(0, 0, false),
            await PlanAsync(connection, definition, Anchor.AddMinutes(25))
        );
        await ExpectedAsync(
            connection,
            Enumerable.Range(1, 6).Select(index => Anchor.AddMinutes(index * 5)).ToArray()
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SkipOmitsOnlyUnplannedOccurrencesAndRetainsAnAlreadyArmedDelivery(bool armed)
    {
        var definition = Interval(MissedOccurrences.Skip);
        string connection = await SetupAsync(definition);
        if (armed)
            await PlanAsync(connection, definition, Anchor);

        Assert.Equal(
            new(1, 0, false),
            await PlanAsync(connection, definition, Anchor.AddMinutes(25))
        );
        await ExpectedAsync(
            connection,
            armed ? [Anchor.AddMinutes(5), Anchor.AddMinutes(30)] : [Anchor.AddMinutes(30)]
        );
    }

    [Fact]
    public async Task CoalesceFreezesItsWindowAndPublishesNoPartialCandidate()
    {
        var definition = RecurrenceDefinition.Every(
            "one-minute-v1",
            Anchor,
            TimeSpan.FromMinutes(1),
            MissedOccurrences.Coalesce
        );
        string connection = await SetupAsync(definition);
        Assert.Equal(
            new(0, 3, true),
            await PlanAsync(connection, definition, Anchor.AddMinutes(12), 3)
        );
        await ExpectedAsync(connection);
        Assert.Equal(
            new(0, 3, true),
            await PlanAsync(connection, definition, Anchor.AddMinutes(20), 3)
        );
        Assert.Equal(
            new(0, 3, true),
            await PlanAsync(connection, definition, Anchor.AddMinutes(20), 3)
        );
        await ExpectedAsync(connection);
        Assert.Equal(
            new(1, 3, true),
            await PlanAsync(connection, definition, Anchor.AddMinutes(20), 3)
        );
        await ExpectedAsync(connection, Anchor.AddMinutes(12));
        Assert.Equal(
            new(0, 3, true),
            await PlanAsync(connection, definition, Anchor.AddMinutes(20), 3)
        );
        Assert.Equal(
            new(0, 3, true),
            await PlanAsync(connection, definition, Anchor.AddMinutes(20), 3)
        );
        Assert.Equal(
            new(2, 2, false),
            await PlanAsync(connection, definition, Anchor.AddMinutes(20), 3)
        );
        await ExpectedAsync(
            connection,
            Anchor.AddMinutes(12),
            Anchor.AddMinutes(20),
            Anchor.AddMinutes(21)
        );
    }

    public static TheoryData<string, string, string, string[]> DaylightCases =>
        new()
        {
            {
                "America/New_York",
                "30 2 * * *",
                "2026-03-08T00:00:00Z",
                ["2026-03-08T07:00:00Z", "2026-03-09T06:30:00Z"]
            },
            {
                "America/New_York",
                "30 1 * * *",
                "2026-11-01T00:00:00Z",
                ["2026-11-01T05:30:00Z", "2026-11-02T06:30:00Z"]
            },
            {
                "America/New_York",
                "*/30 * * * *",
                "2026-11-01T04:59:00Z",
                [
                    "2026-11-01T05:00:00Z",
                    "2026-11-01T05:30:00Z",
                    "2026-11-01T06:00:00Z",
                    "2026-11-01T06:30:00Z",
                    "2026-11-01T07:00:00Z",
                ]
            },
            {
                "Europe/Kyiv",
                "30 3 * * *",
                "2026-03-29T00:00:00Z",
                ["2026-03-29T01:00:00Z", "2026-03-30T00:30:00Z"]
            },
            {
                "Europe/Kyiv",
                "30 3 * * *",
                "2026-10-25T00:00:00Z",
                ["2026-10-25T00:30:00Z", "2026-10-26T01:30:00Z"]
            },
            {
                "Europe/Kyiv",
                "*/30 * * * *",
                "2026-10-24T23:59:00Z",
                [
                    "2026-10-25T00:00:00Z",
                    "2026-10-25T00:30:00Z",
                    "2026-10-25T01:00:00Z",
                    "2026-10-25T01:30:00Z",
                    "2026-10-25T02:00:00Z",
                ]
            },
        };

    [Theory]
    [MemberData(nameof(DaylightCases))]
    public async Task ZonedIndicationsRetainIndependentUtcExpectationsThroughGapsAndBothOverlapOffsets(
        string zone,
        string expression,
        string anchor,
        string[] expected
    )
    {
        DateTimeOffset start = Parse(anchor);
        var definition = RecurrenceDefinition.Cron(
            "indication-v1",
            start,
            expression,
            RecurrenceCronFormat.FiveFields,
            TimeZoneInfo.FindSystemTimeZoneById(zone),
            "test-tzdata-pinned-by-ci",
            MissedOccurrences.Coalesce
        );
        string connection = await SetupAsync(definition);
        await PlanAsync(connection, definition, start, envelope: Indication);
        foreach (string instant in expected.SkipLast(1))
            await PlanAsync(connection, definition, Parse(instant), envelope: Indication);

        await ExpectedAsync(connection, expected.Select(Parse).ToArray());
        await using var read = Open(connection);
        Assert.All(
            await read.Set<DueIntentRecord>().ToArrayAsync(Token),
            row =>
            {
                Assert.Equal("tenant-indications", row.TenantKey);
                Assert.Equal("growth.review-indicated", row.MessageName);
                Assert.Equal("manager", row.Payload.GetProperty("recipient").GetString());
            }
        );
    }

    [Theory]
    [InlineData("America/New_York", "30 2 * * *", "2026-03-08T00:00:00Z", "2026-03-08T07:00:00Z")]
    [InlineData("Europe/Kyiv", "30 3 * * *", "2026-03-29T00:00:00Z", "2026-03-29T01:00:00Z")]
    public async Task CoalesceUsesTheActualForwardSpringOccurrenceInsteadOfTheInvalidPriorSecond(
        string zone,
        string expression,
        string anchor,
        string expected
    )
    {
        var definition = RecurrenceDefinition.Cron(
            "spring-v1",
            Parse(anchor),
            expression,
            RecurrenceCronFormat.FiveFields,
            TimeZoneInfo.FindSystemTimeZoneById(zone),
            "pinned-test-rules",
            MissedOccurrences.Coalesce
        );
        string connection = await SetupAsync(definition);
        await PlanAsync(connection, definition, Parse(expected).AddMinutes(30));
        await using var read = Open(connection);
        var elapsed = await read.Set<DueIntentRecord>()
            .SingleAsync(row => row.DueAt <= Parse(expected).AddMinutes(30), Token);
        Assert.Equal(Parse(expected), elapsed.DueAt);
    }

    [Fact]
    public async Task ClockRegressionAndChangedZoneRulesRefuseBeforeAnyProgressOrOccurrenceChanges()
    {
        var zone = TimeZoneInfo.CreateCustomTimeZone("pinned-zone", TimeSpan.Zero, "zone", "zone");
        var definition = RecurrenceDefinition.Cron(
            "v1",
            Anchor,
            "0 1 * * *",
            RecurrenceCronFormat.FiveFields,
            zone,
            "dataset-one",
            MissedOccurrences.Skip
        );
        string connection = await SetupAsync(definition);
        await PlanAsync(connection, definition, Anchor);
        string progress;
        await using (var read = Open(connection))
            progress = (await read.Set<RecurringIntentRecord>().SingleAsync(Token)).Progress;

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PlanAsync(connection, definition, Anchor.AddTicks(-10))
        );
        var changed = RecurrenceDefinition.Cron(
            "v1",
            Anchor,
            "0 1 * * *",
            RecurrenceCronFormat.FiveFields,
            TimeZoneInfo.CreateCustomTimeZone("pinned-zone", TimeSpan.FromHours(1), "zone", "zone"),
            "dataset-one",
            MissedOccurrences.Skip
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            PlanAsync(connection, changed, Anchor)
        );
        await using var check = Open(connection);
        Assert.Equal(
            progress,
            (await check.Set<RecurringIntentRecord>().SingleAsync(Token)).Progress
        );
        Assert.Equal(1, await check.Set<DueIntentRecord>().CountAsync(Token));
    }

    [Fact]
    public async Task CompetingPlannersObserveOneCommittedFutureOccurrence()
    {
        var definition = Interval(MissedOccurrences.Skip);
        string connection = await SetupAsync(definition);
        await using var first = Open(connection);
        await using var transaction = await first.Database.BeginTransactionAsync(Token);
        var firstPlan = await new PostgresRecurringIntents<RecurrenceDatabase>(first).PlanAsync(
            Key,
            definition,
            Anchor,
            10,
            Maintenance,
            Token
        );
        var second = PlanAsync(connection, definition, Anchor);
        await WaitForLockAsync(connection);
        Assert.False(second.IsCompleted);
        await first.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
        Assert.Equal(1, firstPlan.Staged);
        Assert.Equal(0, (await second).Staged);
        await ExpectedAsync(connection, Anchor.AddMinutes(5));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeRollbackRestoresBothProgressAndFiniteOccurrences(bool save)
    {
        var definition = Interval(MissedOccurrences.CatchUp(2));
        string connection = await SetupAsync(definition);
        string progress;
        await using (var read = Open(connection))
            progress = (await read.Set<RecurringIntentRecord>().SingleAsync(Token)).Progress;

        await using (var database = Open(connection))
        {
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            await new PostgresRecurringIntents<RecurrenceDatabase>(database).PlanAsync(
                Key,
                definition,
                Anchor.AddMinutes(10),
                100,
                Maintenance,
                Token
            );
            if (save)
                await database.SaveChangesAsync(Token);

            await transaction.RollbackAsync(Token);
        }

        await ExpectedAsync(connection);
        await using (var read = Open(connection))
            Assert.Equal(
                progress,
                (await read.Set<RecurringIntentRecord>().SingleAsync(Token)).Progress
            );

        await PlanAsync(connection, definition, Anchor.AddMinutes(10));
        await ExpectedAsync(connection, Anchor.AddMinutes(5), Anchor.AddMinutes(10));
    }

    [Theory]
    [InlineData(false, "progress")]
    [InlineData(true, "progress")]
    [InlineData(false, "occurrence")]
    [InlineData(true, "occurrence")]
    [InlineData(false, "accept")]
    [InlineData(true, "accept")]
    [InlineData(false, "detach")]
    [InlineData(true, "detach")]
    [InlineData(false, "clear")]
    [InlineData(true, "clear")]
    public async Task NeitherParticipantCanDisappearFromTheSameSave(bool asynchronous, string fault)
    {
        var definition = Interval(MissedOccurrences.Skip);
        string connection = await SetupAsync(definition);
        await using var database = Open(connection);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        await new PostgresRecurringIntents<RecurrenceDatabase>(database).PlanAsync(
            Key,
            definition,
            Anchor,
            10,
            Maintenance,
            Token
        );
        if (fault == "progress")
            database.ChangeTracker.Entries<RecurringIntentRecord>().Single().State =
                EntityState.Unchanged;
        else if (fault == "occurrence")
            database.ChangeTracker.Entries<DueIntentRecord>().Single().State =
                EntityState.Unchanged;
        else if (fault == "accept")
            database.ChangeTracker.AcceptAllChanges();
        else if (fault == "detach")
            database.ChangeTracker.Entries<RecurringIntentRecord>().Single().State =
                EntityState.Detached;
        else
            database.ChangeTracker.Clear();

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SaveAsync(database, asynchronous)
        );
        await transaction.RollbackAsync(Token);
        await ExpectedAsync(connection);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task UndoneNativeSavepointCannotMasqueradeAsSavedProgress(bool asynchronous)
    {
        var definition = Interval(MissedOccurrences.Skip);
        string connection = await SetupAsync(definition);
        await using var database = Open(connection);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        await transaction.CreateSavepointAsync("before", Token);
        await new PostgresRecurringIntents<RecurrenceDatabase>(database).PlanAsync(
            Key,
            definition,
            Anchor,
            10,
            Maintenance,
            Token
        );
        await SaveAsync(database, asynchronous);
        await transaction.RollbackToSavepointAsync("before", Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SaveAsync(database, asynchronous)
        );
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task GenuineNativeSavedProgressSupportsExplicitAcceptanceAndRepeatedSaves(
        bool asynchronous,
        bool accept
    )
    {
        var definition = Interval(MissedOccurrences.Skip);
        string connection = await SetupAsync(definition);
        await using var database = Open(connection);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        await new PostgresRecurringIntents<RecurrenceDatabase>(database).PlanAsync(
            Key,
            definition,
            Anchor,
            10,
            Maintenance,
            Token
        );
        Assert.Equal(2, await SaveAsync(database, asynchronous, accept));
        if (!accept)
            database.ChangeTracker.AcceptAllChanges();

        Assert.Equal(0, await SaveAsync(database, asynchronous));
        await transaction.CommitAsync(Token);
        await ExpectedAsync(connection, Anchor.AddMinutes(5));
    }

    [Fact]
    public async Task LostCommitResponseIsRecoveredByReadingRetainedProgressWithStableIdentities()
    {
        var definition = Interval(MissedOccurrences.CatchUp(2));
        string connection = await SetupAsync(definition);
        Guid[] proposed;
        await using (var database = Open(connection, new LoseResponse()))
        {
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            await new PostgresRecurringIntents<RecurrenceDatabase>(database).PlanAsync(
                Key,
                definition,
                Anchor.AddMinutes(10),
                100,
                Maintenance,
                Token
            );
            proposed = database
                .ChangeTracker.Entries<DueIntentRecord>()
                .Select(entry => entry.Entity.MessageId)
                .Order()
                .ToArray();
            await database.SaveChangesAsync(Token);
            await Assert.ThrowsAsync<IOException>(() => transaction.CommitAsync(Token));
        }

        // Fresh observation advances from committed progress rather than re-enqueuing either retained occurrence.
        await PlanAsync(connection, definition, Anchor.AddMinutes(10));
        await using var read = Open(connection);
        Assert.Equal(3, await read.Set<DueIntentRecord>().CountAsync(Token));
        Assert.Equal(
            proposed,
            await read.Set<DueIntentRecord>()
                .Where(row => row.DueAt <= Anchor.AddMinutes(10))
                .Select(row => row.MessageId)
                .Order()
                .ToArrayAsync(Token)
        );
        await ExpectedAsync(
            connection,
            Anchor.AddMinutes(5),
            Anchor.AddMinutes(10),
            Anchor.AddMinutes(15)
        );
    }

    [Fact]
    public async Task StopIsBoundedResumableAndReleasedWorkRemainsIrrevocable()
    {
        var definition = Interval(MissedOccurrences.CatchUp(3));
        string connection = await SetupAsync(definition);
        await PlanAsync(connection, definition, Anchor.AddMinutes(15));
        await WriteAsync(
            connection,
            async database =>
            {
                Assert.Single(
                    await new PostgresDueIntents<RecurrenceDatabase>(database).ReleaseAsync(
                        Key.ScopeKey,
                        Anchor.AddMinutes(5),
                        1,
                        Token
                    )
                );
                return true;
            }
        );
        Assert.Equal(new(1, true), await StopAsync(connection, 1));
        Assert.Equal(new(0, 0, false), await PlanAsync(connection, definition, Anchor.AddDays(1)));
        Assert.Equal(new(1, false), await StopAsync(connection, 1));
        Assert.Equal(new(0, false), await StopAsync(connection, 1));
        await using var read = Open(connection);
        Assert.Equal(
            RecurringIntentState.Stopped,
            (await read.Set<RecurringIntentRecord>().SingleAsync(Token)).State
        );
        Assert.Equal(
            2,
            await read.Set<DueIntentRecord>()
                .CountAsync(row => row.State == DueIntentState.Cancelled, Token)
        );
        Assert.Equal(
            1,
            await read.Set<DueIntentRecord>()
                .CountAsync(row => row.State == DueIntentState.Released, Token)
        );
        Assert.Equal(1, await read.Set<OutboxMessageRecord>().CountAsync(Token));
    }

    [Fact]
    public async Task ChangedGenerationRetainsOldIdentityAndRejectsOldPlanning()
    {
        var definition = Interval(MissedOccurrences.Skip);
        string connection = await SetupAsync(definition);
        await PlanAsync(connection, definition, Anchor);
        await StopAsync(connection, 100);
        var replacement = new RecurringIntentKey(
            Key.ScopeKey,
            Key.ScheduleId,
            Guid.Parse("00000000-0000-0000-0000-000000000053")
        );
        await WriteAsync(
            connection,
            database =>
            {
                new PostgresRecurringIntents<RecurrenceDatabase>(database).Register(
                    replacement,
                    definition
                );
                return Task.FromResult(true);
            }
        );
        await WriteAsync(
            connection,
            database =>
                new PostgresRecurringIntents<RecurrenceDatabase>(database).PlanAsync(
                    replacement,
                    definition,
                    Anchor,
                    100,
                    Maintenance,
                    Token
                )
        );
        Assert.Equal(new(0, 0, false), await PlanAsync(connection, definition, Anchor));
        await using var read = Open(connection);
        var rows = await read.Set<DueIntentRecord>().ToArrayAsync(Token);
        Assert.Equal(2, rows.Length);
        Assert.Equal(2, rows.Select(row => row.MessageId).Distinct().Count());
        Assert.Equal(2, rows.Select(row => row.OccurrenceId).Distinct().Count());
        Assert.Equal(2, rows.Select(row => row.ScheduleId).Distinct().Count());
        Assert.Equal(1, rows.Count(row => row.State == DueIntentState.Cancelled));
        Assert.Equal(1, rows.Count(row => row.State == DueIntentState.Pending));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4097)]
    public async Task UnboundedPlanningIsRejectedBeforeProgress(int budget)
    {
        var definition = Interval(MissedOccurrences.Skip);
        string connection = await SetupAsync(definition);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            PlanAsync(connection, definition, Anchor, budget)
        );
        await ExpectedAsync(connection);
    }

    [Theory]
    [InlineData("staged", false)]
    [InlineData("saved", false)]
    [InlineData("committed", true)]
    public async Task ProcessDeathReleasesLocksAndRecoversFromDurableProgress(
        string phase,
        bool committed
    )
    {
        var definition = Interval(MissedOccurrences.Skip);
        string connection = await SetupAsync(definition);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(25));
        await using (
            var child = new ChildHost(
                typeof(Application).Assembly,
                new() { ["RECURRENCE_CONNECTION"] = connection, ["RECURRENCE_PAUSE"] = phase },
                Anchor.ToString("O", System.Globalization.CultureInfo.InvariantCulture)
            )
        )
        {
            await child.WaitForOutputAsync("PAUSED:" + phase, deadline.Token);
            await child.KillAsync(deadline.Token);
        }

        await ExpectedAsync(connection, committed ? [Anchor.AddMinutes(5)] : []);
        Assert.Equal(committed ? 0 : 1, (await PlanAsync(connection, definition, Anchor)).Staged);
        await ExpectedAsync(connection, Anchor.AddMinutes(5));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeConstraintFailureRollsBackPlanningAndFreshRecoveryStagesTheOriginalIdentity(
        bool asynchronous
    )
    {
        var definition = Interval(MissedOccurrences.Skip);
        string connection = await SetupAsync(definition);
        Guid proposed;
        await using (var database = Open(connection))
        {
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            await new PostgresRecurringIntents<RecurrenceDatabase>(database).PlanAsync(
                Key,
                definition,
                Anchor,
                10,
                Maintenance,
                Token
            );
            proposed = database.ChangeTracker.Entries<DueIntentRecord>().Single().Entity.MessageId;
            database.Add(new MaintenanceProbe { Id = 1 });
            var failure = await Assert.ThrowsAsync<DbUpdateException>(() =>
                SaveAsync(database, asynchronous)
            );
            Assert.Equal(
                PostgresErrorCodes.NotNullViolation,
                Assert.IsType<PostgresException>(failure.InnerException).SqlState
            );
            await transaction.RollbackAsync(Token);
        }

        await ExpectedAsync(connection);
        await PlanAsync(connection, definition, Anchor);
        await using var read = Open(connection);
        Assert.Equal(proposed, (await read.Set<DueIntentRecord>().SingleAsync(Token)).MessageId);
        Assert.Equal(0, await read.Set<MaintenanceProbe>().CountAsync(Token));
    }

    [Fact]
    public async Task CancellationOfAnActuallyBlockedPlannerLeavesNoProgressAndFreshRecoveryWorks()
    {
        var definition = Interval(MissedOccurrences.Skip);
        string connection = await SetupAsync(definition);
        await using var first = Open(connection);
        await using var transaction = await first.Database.BeginTransactionAsync(Token);
        await new PostgresRecurringIntents<RecurrenceDatabase>(first).PlanAsync(
            Key,
            definition,
            Anchor,
            10,
            Maintenance,
            Token
        );
        await using var second = Open(connection);
        await using var secondTransaction = await second.Database.BeginTransactionAsync(Token);
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var blocked = new PostgresRecurringIntents<RecurrenceDatabase>(second).PlanAsync(
            Key,
            definition,
            Anchor,
            10,
            Maintenance,
            cancelled.Token
        );
        await WaitForLockAsync(connection);
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => blocked);
        await secondTransaction.RollbackAsync(Token);
        await transaction.RollbackAsync(Token);
        await ExpectedAsync(connection);
        Assert.Equal(1, (await PlanAsync(connection, definition, Anchor)).Staged);
    }

    [Fact]
    public async Task StopWaitsForTheLastPlannerAndCancelsItsCommittedOccurrence()
    {
        var definition = Interval(MissedOccurrences.Skip);
        string connection = await SetupAsync(definition);
        await using var first = Open(connection);
        await using var transaction = await first.Database.BeginTransactionAsync(Token);
        await new PostgresRecurringIntents<RecurrenceDatabase>(first).PlanAsync(
            Key,
            definition,
            Anchor,
            10,
            Maintenance,
            Token
        );
        var stop = StopAsync(connection, 1);
        await WaitForLockAsync(connection);
        await first.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
        Assert.Equal(new(1, false), await stop);
        Assert.Equal(new(0, 0, false), await PlanAsync(connection, definition, Anchor.AddDays(1)));
        await using var read = Open(connection);
        Assert.Equal(
            DueIntentState.Cancelled,
            (await read.Set<DueIntentRecord>().SingleAsync(Token)).State
        );
    }

    [Fact]
    public async Task StopRacingALockedReleaseRetainsTheIrrevocableOutbox()
    {
        var definition = Interval(MissedOccurrences.Skip);
        string connection = await SetupAsync(definition);
        await PlanAsync(connection, definition, Anchor);
        await using var release = Open(connection);
        await using var transaction = await release.Database.BeginTransactionAsync(Token);
        Assert.Single(
            await new PostgresDueIntents<RecurrenceDatabase>(release).ReleaseAsync(
                Key.ScopeKey,
                Anchor.AddMinutes(5),
                1,
                Token
            )
        );
        var stop = StopAsync(connection, 1);
        await WaitForLockAsync(connection);
        await release.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
        Assert.Equal(new(0, false), await stop);
        await using var read = Open(connection);
        Assert.Equal(
            DueIntentState.Released,
            (await read.Set<DueIntentRecord>().SingleAsync(Token)).State
        );
        Assert.Equal(
            RecurringIntentState.Stopped,
            (await read.Set<RecurringIntentRecord>().SingleAsync(Token)).State
        );
        Assert.Equal(1, await read.Set<OutboxMessageRecord>().CountAsync(Token));
    }

    [Fact]
    public async Task ReplacingADisposableHostsActualTzifDatasetRefusesNewPlanningUntilThePinnedDataIsRestored()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using (var database = Open(connection))
            await database.Database.EnsureCreatedAsync(Token);

        string directory = Path.Combine(
            Path.GetTempPath(),
            "gloaming-recurrence-zones-" + Guid.NewGuid().ToString("N")
        );
        string target = Path.Combine(directory, "Gloaming", "Test");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(30));
        try
        {
            File.Copy("/usr/share/zoneinfo/Etc/UTC", target);
            await RunAsync("zone-register", 0, "ZONE_OFFSET:0");
            await RunAsync("zone-plan", 0, "ZONE_STAGED:1");
            await ExpectedAsync(connection, Anchor.AddDays(1));
            string progress;
            await using (var read = Open(connection))
                progress = (await read.Set<RecurringIntentRecord>().SingleAsync(Token)).Progress;

            File.Copy("/usr/share/zoneinfo/Etc/GMT-1", target, overwrite: true);
            await RunAsync("zone-plan", 42, "ZONE_OFFSET:60", "INCOMPATIBLE_CALENDAR");
            await using (var read = Open(connection))
                Assert.Equal(
                    progress,
                    (await read.Set<RecurringIntentRecord>().SingleAsync(Token)).Progress
                );

            await ExpectedAsync(connection, Anchor.AddDays(1));
            File.Copy("/usr/share/zoneinfo/Etc/UTC", target, overwrite: true);
            await RunAsync("zone-plan", 0, "ZONE_STAGED:0");
            await ExpectedAsync(connection, Anchor.AddDays(1));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }

        async Task RunAsync(string operation, int code, params string[] expected)
        {
            await using var child = new ChildHost(
                typeof(Application).Assembly,
                new() { ["RECURRENCE_CONNECTION"] = connection, ["TZDIR"] = directory },
                operation
            );
            Assert.Equal(code, await child.WaitForExitAsync(deadline.Token));
            foreach (string value in expected)
                Assert.Contains(value, child.Output, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("generation-filter")]
    [InlineData("finite-filter")]
    [InlineData("outbox-filter")]
    [InlineData("key")]
    [InlineData("revision-token")]
    [InlineData("revision-type")]
    [InlineData("progress-type")]
    [InlineData("column")]
    [InlineData("scope-collation")]
    public void UnsupportedRecurrenceModelsFailBeforeAnyConnection(string fault)
    {
        using var database = new FaultyDatabase(
            new DbContextOptionsBuilder<FaultyDatabase>()
                .UseNpgsql("Host=unused;Database=unused;Username=unused")
                .ReplaceService<IModelCacheKeyFactory, FaultyModelKey>()
                .Options,
            fault
        );
        Assert.Throws<InvalidOperationException>(() =>
            new PostgresRecurringIntents<FaultyDatabase>(database)
        );
    }

    [Fact]
    public void SupportedRecurrenceModelNeedsNoConnection()
    {
        using var database = Open("Host=unused;Database=unused;Username=unused");
        _ = new PostgresRecurringIntents<RecurrenceDatabase>(database);
    }

    [Fact]
    public async Task RegisteredCapabilityUsesOnlyCancellableAsyncStoredEvidenceReads()
    {
        var definition = Interval(MissedOccurrences.Skip);
        string connection = await SetupAsync(definition);
        var services = new ServiceCollection();
        services.AddPostgresRecurrence<RegisteredDatabase>();
        await using var provider = services.BuildServiceProvider();
        var interceptor = new AsyncReadsOnly();
        var options = new DbContextOptionsBuilder<RegisteredDatabase>()
            .UseNpgsql(connection)
            .AddInterceptors(interceptor);
        options.ConfigureGloamingCapabilities<RegisteredDatabase>(provider);
        await using var database = new RegisteredDatabase(options.Options);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        await new PostgresRecurringIntents<RegisteredDatabase>(database).PlanAsync(
            Key,
            definition,
            Anchor,
            10,
            Maintenance,
            Token
        );
        await database.SaveChangesAsync(Token);
        int before = interceptor.Reads;
        Assert.Equal(0, await database.SaveChangesAsync(Token));
        Assert.True(interceptor.Reads >= before + 2);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            database.SaveChangesAsync(cancelled.Token)
        );
        await transaction.RollbackAsync(Token);
        await ExpectedAsync(connection);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ReplacingOriginalProgressCannotOmitItFromTheNativeUpdate(bool asynchronous)
    {
        var definition = Interval(MissedOccurrences.Skip);
        string connection = await SetupAsync(definition);
        await using var database = Open(connection);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        await new PostgresRecurringIntents<RecurrenceDatabase>(database).PlanAsync(
            Key,
            definition,
            Anchor,
            10,
            Maintenance,
            Token
        );
        var progress = database
            .ChangeTracker.Entries<RecurringIntentRecord>()
            .Single()
            .Property(row => row.Progress);
        progress.OriginalValue = progress.CurrentValue;
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SaveAsync(database, asynchronous)
        );
        await transaction.RollbackAsync(Token);
        await ExpectedAsync(connection);
    }

    private async Task<string> SetupAsync(RecurrenceDefinition definition)
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using (var database = Open(connection))
            await database.Database.EnsureCreatedAsync(Token);

        await WriteAsync(
            connection,
            database =>
            {
                new PostgresRecurringIntents<RecurrenceDatabase>(database).Register(
                    Key,
                    definition
                );
                return Task.FromResult(true);
            }
        );
        return connection;
    }

    private static RecurrenceDefinition Interval(MissedOccurrences missed) =>
        RecurrenceDefinition.Every("rotation-v1", Anchor, TimeSpan.FromMinutes(5), missed);

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, System.Globalization.CultureInfo.InvariantCulture);

    private static OutgoingMessage Maintenance(RecurringOccurrence occurrence) =>
        new(
            occurrence.MessageId,
            "storage-worker",
            "maintenance.rotate",
            1,
            JsonSerializer.SerializeToElement(
                new { partition = "storage", nominal = occurrence.DueAt }
            )
        );

    private static OutgoingMessage Indication(RecurringOccurrence occurrence) =>
        new(
            occurrence.MessageId,
            "growth",
            "growth.review-indicated",
            2,
            JsonSerializer.SerializeToElement(
                new { recipient = "manager", nominal = occurrence.DueAt }
            ),
            "tenant-indications"
        );

    private static RecurrenceDatabase Open(string connection, params IInterceptor[] interceptors) =>
        new(
            new DbContextOptionsBuilder<RecurrenceDatabase>()
                .UseNpgsql(connection)
                .AddInterceptors(interceptors)
                .Options
        );

    private static Task<RecurrencePlan> PlanAsync(
        string connection,
        RecurrenceDefinition definition,
        DateTimeOffset observed,
        int budget = 100,
        Func<RecurringOccurrence, OutgoingMessage>? envelope = null
    ) =>
        WriteAsync(
            connection,
            database =>
                new PostgresRecurringIntents<RecurrenceDatabase>(database).PlanAsync(
                    Key,
                    definition,
                    observed,
                    budget,
                    envelope ?? Maintenance,
                    Token
                )
        );

    private static Task<RecurrenceStop> StopAsync(string connection, int maximum) =>
        WriteAsync(
            connection,
            database =>
                new PostgresRecurringIntents<RecurrenceDatabase>(database).StopAsync(
                    Key,
                    maximum,
                    Token
                )
        );

    private static async Task<T> WriteAsync<T>(
        string connection,
        Func<RecurrenceDatabase, Task<T>> operation
    )
    {
        await using var database = Open(connection);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        var result = await operation(database);
        await database.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
        return result;
    }

    private static Task<int> SaveAsync(
        RecurrenceDatabase database,
        bool asynchronous,
        bool accept = true
    ) =>
        asynchronous
            ? database.SaveChangesAsync(accept, Token)
            : Task.FromResult(database.SaveChanges(accept));

    private static async Task ExpectedAsync(string connection, params DateTimeOffset[] expected)
    {
        await using var read = Open(connection);
        var rows = await read.Set<DueIntentRecord>().OrderBy(row => row.DueAt).ToArrayAsync(Token);
        Assert.Equal(expected, rows.Select(row => row.DueAt));
        Assert.Equal(rows.Length, rows.Select(row => row.MessageId).Distinct().Count());
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

    private sealed class RegisteredDatabase(DbContextOptions<RegisteredDatabase> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model)
        {
            model.ConfigurePostgresOutbox("recurrence", "outbox");
            model.ConfigurePostgresDueIntents("recurrence");
            model.ConfigurePostgresRecurrence("recurrence");
        }
    }

    private sealed class FaultyDatabase(DbContextOptions<FaultyDatabase> options, string fault)
        : DbContext(options)
    {
        internal string Fault => fault;

        protected override void OnModelCreating(ModelBuilder model)
        {
            var outgoing = model.ConfigurePostgresOutbox("recurrence", "outbox");
            var finite = model.ConfigurePostgresDueIntents("recurrence");
            var row = model.ConfigurePostgresRecurrence("recurrence");
            switch (fault)
            {
                case "generation-filter":
                    row.HasQueryFilter(
                        "wrong-generation",
                        value => value.State == RecurringIntentState.Active
                    );
                    break;
                case "finite-filter":
                    finite.HasQueryFilter(
                        "wrong-finite",
                        value => value.State == DueIntentState.Pending
                    );
                    break;
                case "outbox-filter":
                    outgoing.HasQueryFilter("wrong-outbox", value => value.TenantKey != null);
                    break;
                case "key":
                    row.HasKey(value => new { value.ScopeKey, value.ScheduleId });
                    break;
                case "revision-token":
                    row.Property(value => value.Revision).IsConcurrencyToken(false);
                    break;
                case "revision-type":
                    row.Property(value => value.Revision).HasColumnType("numeric");
                    break;
                case "progress-type":
                    row.Property(value => value.Progress).HasColumnType("jsonb");
                    break;
                case "column":
                    row.Property(value => value.Progress).HasColumnName("different");
                    break;
                case "scope-collation":
                    row.Property(value => value.ScopeKey).UseCollation("default");
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
        ) => throw new InvalidOperationException("This consumer allows async native reads only.");

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
        ) => throw new IOException("Native commit completed; acknowledgement was lost.");
    }
}
