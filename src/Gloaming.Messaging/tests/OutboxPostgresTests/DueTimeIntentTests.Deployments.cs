using System.Globalization;
using System.Reflection;
using Gloaming.Messaging.DueTimeProof.Current;
using Gloaming.Messaging.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ModulithFoundry.Tests.Infrastructure;
using CurrentApplication = Gloaming.Messaging.DueTimeProof.Current.Application;
using EarlierApplication = Gloaming.Messaging.DueTimeProof.Earlier.Application;

namespace Gloaming.Messaging.Tests;

public sealed partial class DueTimeIntentTests
{
    [Fact]
    public async Task AnEarlyReceiverRetainsTheOccurrenceForACompatibleDueRetry()
    {
        string connection = await SetupAsync();
        await OpenAsync(connection, Guid.NewGuid());
        await ReleaseAsync(connection, Due);
        var message = await DispatchAsync(connection);
        Assert.Contains(
            "DECISION:NotDue",
            await RunAsync(
                connection,
                typeof(CurrentApplication).Assembly,
                "receive-personnel",
                message.MessageId,
                expectedExit: 12,
                now: Due.AddMinutes(-1)
            ),
            StringComparison.Ordinal
        );
        await using (var read = PersonnelContext(connection))
            Assert.Equal(0, await read.Set<ReminderNotice>().CountAsync(Token));

        Assert.Contains(
            "DECISION:Accepted",
            await RunAsync(
                connection,
                typeof(CurrentApplication).Assembly,
                "receive-personnel",
                message.MessageId
            ),
            StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData("staged", false)]
    [InlineData("saved", false)]
    [InlineData("committed", true)]
    public async Task KilledReleaserRecoversOneOutboxIntentAcrossProcessReplacement(
        string phase,
        bool committed
    )
    {
        string connection = await SetupAsync();
        await OpenAsync(connection, Guid.NewGuid());
        var environment = EnvironmentFor(connection);
        environment["DUE_INTENT_PAUSE"] = phase;
        await using (
            var old = Start(
                connection,
                typeof(EarlierApplication).Assembly,
                "release-personnel",
                environment: environment
            )
        )
        {
            await old.WaitForOutputAsync("PAUSED:" + phase, Token);
            await using var read = PersonnelContext(connection);
            Assert.Equal(
                committed ? 1 : 0,
                await read.Set<OutboxMessageRecord>().CountAsync(Token)
            );
            Assert.Equal(
                committed ? IntentState.Released : IntentState.Pending,
                (await read.Set<DueIntent>().SingleAsync(Token)).State
            );
            await old.KillAsync(Token);
        }

        string recovered = await RunAsync(
            connection,
            typeof(CurrentApplication).Assembly,
            "release-personnel",
            now: Due.AddDays(30)
        );
        Assert.Contains(committed ? "STAGED:0" : "STAGED:1", recovered, StringComparison.Ordinal);
        await RunAsync(
            connection,
            typeof(CurrentApplication).Assembly,
            "release-personnel",
            now: Due.AddDays(60)
        );
        await using var final = PersonnelContext(connection);
        Assert.Equal(1, await final.Set<OutboxMessageRecord>().CountAsync(Token));
        Assert.Equal(IntentState.Released, (await final.Set<DueIntent>().SingleAsync(Token)).State);
        Assert.Equal(Due, (await final.Set<DueIntent>().SingleAsync(Token)).DueAt);
    }

    [Theory]
    [InlineData("saved", false)]
    [InlineData("committed", true)]
    public async Task KilledReceiverRecoversOneNotificationAfterAnUncertainAcceptance(
        string phase,
        bool committed
    )
    {
        string connection = await SetupAsync();
        await OpenAsync(connection, Guid.NewGuid());
        await ReleaseAsync(connection, Due);
        var message = await DispatchAsync(connection);
        var environment = EnvironmentFor(connection);
        environment["DUE_INTENT_PAUSE"] = phase;
        await using (
            var old = Start(
                connection,
                typeof(EarlierApplication).Assembly,
                "receive-personnel",
                message.MessageId,
                environment
            )
        )
        {
            await old.WaitForOutputAsync("PAUSED:" + phase, Token);
            await using var read = PersonnelContext(connection);
            Assert.Equal(committed ? 1 : 0, await read.Set<ReminderNotice>().CountAsync(Token));
            await old.KillAsync(Token);
        }

        string recovered = await RunAsync(
            connection,
            typeof(CurrentApplication).Assembly,
            "receive-personnel",
            message.MessageId
        );
        Assert.Contains(
            committed ? "DECISION:Duplicate" : "DECISION:Accepted",
            recovered,
            StringComparison.Ordinal
        );
        await using var final = PersonnelContext(connection);
        Assert.Equal(1, await final.Set<ReminderNotice>().CountAsync(Token));
    }

    [Fact]
    public async Task OldReleasePreservesOpaqueV2PayloadButOldReceiverRefusesItWithoutEffects()
    {
        string connection = await SetupAsync();
        await OpenAsync(connection, Guid.NewGuid(), version: 2);
        Assert.Contains(
            "STAGED:1",
            await RunAsync(connection, typeof(EarlierApplication).Assembly, "release-personnel"),
            StringComparison.Ordinal
        );
        var message = await DispatchAsync(connection);
        Assert.Equal(2, message.SchemaVersion);
        Assert.Equal("compact", message.Payload.GetProperty("presentation").GetString());
        Assert.Contains(
            "INCOMPATIBLE_READER",
            await RunAsync(
                connection,
                typeof(EarlierApplication).Assembly,
                "receive-personnel",
                message.MessageId,
                expectedExit: 10
            ),
            StringComparison.Ordinal
        );
        await using (var read = PersonnelContext(connection))
            Assert.Equal(0, await read.Set<ReminderNotice>().CountAsync(Token));

        Assert.Contains(
            "DECISION:Accepted",
            await RunAsync(
                connection,
                typeof(CurrentApplication).Assembly,
                "receive-personnel",
                message.MessageId
            ),
            StringComparison.Ordinal
        );
        Assert.Contains(
            "DECISION:Duplicate",
            await RunAsync(
                connection,
                typeof(CurrentApplication).Assembly,
                "receive-personnel",
                message.MessageId
            ),
            StringComparison.Ordinal
        );
    }

    [Fact]
    public async Task NewConsumerReadsRetainedV1AndOldAndNewReleaseProcessesDoNotDuplicateIt()
    {
        string connection = await SetupAsync();
        await OpenAsync(connection, Guid.NewGuid());
        var releases = await Task.WhenAll(
            RunAsync(connection, typeof(EarlierApplication).Assembly, "release-personnel"),
            RunAsync(connection, typeof(CurrentApplication).Assembly, "release-personnel")
        );
        Assert.Single(releases, value => value.Contains("STAGED:1", StringComparison.Ordinal));
        var message = await DispatchAsync(connection);
        Assert.Contains(
            "DECISION:Accepted",
            await RunAsync(
                connection,
                typeof(CurrentApplication).Assembly,
                "receive-personnel",
                message.MessageId
            ),
            StringComparison.Ordinal
        );
        await using var final = PersonnelContext(connection);
        Assert.Equal(1, await final.Set<OutboxMessageRecord>().CountAsync(Token));
        Assert.Equal(1, await final.Set<ReminderNotice>().CountAsync(Token));
    }

    private static Dictionary<string, string> EnvironmentFor(string connection) =>
        new() { ["DUE_INTENT_CONNECTION"] = connection };

    private static ChildHost Start(
        string connection,
        Assembly binary,
        string role,
        Guid? messageId = null,
        Dictionary<string, string>? environment = null,
        DateTimeOffset? now = null
    )
    {
        var arguments = new List<string>
        {
            role,
            Scope,
            (now ?? Due).ToString("O", CultureInfo.InvariantCulture),
        };
        if (messageId is { } id)
            arguments.Add(id.ToString("D"));

        return new ChildHost(
            binary,
            environment ?? EnvironmentFor(connection),
            arguments.ToArray()
        );
    }

    private static async Task<string> RunAsync(
        string connection,
        Assembly binary,
        string role,
        Guid? messageId = null,
        int expectedExit = 0,
        DateTimeOffset? now = null
    )
    {
        await using var child = Start(connection, binary, role, messageId, now: now);
        Assert.Equal(expectedExit, await child.WaitForExitAsync(Token));
        return child.Output;
    }
}
