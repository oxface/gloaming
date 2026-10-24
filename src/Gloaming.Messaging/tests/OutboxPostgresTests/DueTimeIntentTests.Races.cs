using Gloaming.Messaging.DueTimeProof.Current;
using Gloaming.Messaging.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Gloaming.Messaging.Tests;

public sealed partial class DueTimeIntentTests
{
    [Fact]
    public async Task CompetingReleasersSkipLockedWorkWithoutDuplicatingItsOutbox()
    {
        string connection = await SetupAsync();
        await OpenAsync(connection, Guid.NewGuid());
        await OpenAsync(connection, Guid.NewGuid());
        await using var first = PersonnelContext(connection);
        await using var transaction = await first.Database.BeginTransactionAsync(Token);
        Guid selected = Assert.Single(
            await NativeDueIntents.ReleaseAsync(first, Scope, Due, 1, Token)
        );
        Guid other = Assert.Single(await ReleaseAsync(connection, Due));
        Assert.NotEqual(selected, other);
        await first.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
        Assert.Empty(await ReleaseAsync(connection, Due));
        await using var read = PersonnelContext(connection);
        Assert.Equal(2, await read.Set<OutboxMessageRecord>().CountAsync(Token));
        Assert.Equal(
            2,
            await read.Set<DueIntent>().CountAsync(row => row.State == IntentState.Released, Token)
        );
    }

    [Fact]
    public async Task CancellationHoldingTheIntentLockSuppressesSelectionBeforeCommit()
    {
        string connection = await SetupAsync();
        Guid id = Guid.NewGuid();
        await OpenAsync(connection, id);
        await using var canceller = PersonnelContext(connection);
        await using var transaction = await canceller.Database.BeginTransactionAsync(Token);
        Assert.Equal(
            Suppression.Suppressed,
            await Personnel.FinishAsync(canceller, Scope, id, CaseStatus.Cancelled, Token)
        );
        Assert.Empty(await ReleaseAsync(connection, Due));
        await canceller.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
        Assert.Empty(await ReleaseAsync(connection, Due));
        await using var read = PersonnelContext(connection);
        Assert.Equal(0, await read.Set<OutboxMessageRecord>().CountAsync(Token));
        Assert.Equal(IntentState.Cancelled, (await read.Set<DueIntent>().SingleAsync(Token)).State);
    }

    [Fact]
    public async Task CancellationRacingLockedReleaseReportsTooLateAndReceiverRejectsItsGeneration()
    {
        string connection = await SetupAsync();
        Guid id = Guid.NewGuid();
        await OpenAsync(connection, id);
        await using var releaser = PersonnelContext(connection);
        await using var transaction = await releaser.Database.BeginTransactionAsync(Token);
        Assert.Single(await NativeDueIntents.ReleaseAsync(releaser, Scope, Due, 1, Token));
        var cancellation = WriteAsync(
            connection,
            database => Personnel.FinishAsync(database, Scope, id, CaseStatus.Cancelled, Token)
        );
        await WaitForBlockedAsync(connection);
        Assert.False(cancellation.IsCompleted);
        await releaser.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
        Assert.Equal(Suppression.AlreadyReleased, await cancellation);
        Assert.Equal(
            Acceptance.Stale,
            await AcceptAsync(connection, await DispatchAsync(connection), Due)
        );
        await using var read = PersonnelContext(connection);
        Assert.Equal(0, await read.Set<ReminderNotice>().CountAsync(Token));
    }

    [Fact]
    public async Task CancelledWaitingCallerDoesNotPretendToSuppressTheOwnersOccurrence()
    {
        string connection = await SetupAsync();
        Guid id = Guid.NewGuid();
        await OpenAsync(connection, id);
        await using var releaser = PersonnelContext(connection);
        await using var transaction = await releaser.Database.BeginTransactionAsync(Token);
        await NativeDueIntents.ReleaseAsync(releaser, Scope, Due, 1, Token);
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var cancellation = WriteAsync(
            connection,
            database => Personnel.FinishAsync(database, Scope, id, CaseStatus.Cancelled, stop.Token)
        );
        await WaitForBlockedAsync(connection);
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => cancellation);
        await releaser.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
        Assert.Equal(
            Acceptance.Accepted,
            await AcceptAsync(connection, await DispatchAsync(connection), Due)
        );
        await using var read = PersonnelContext(connection);
        Assert.Equal(
            CaseStatus.Active,
            (await read.Set<ProbationCase>().SingleAsync(Token)).Status
        );
    }

    [Fact]
    public async Task ReceiverAndCancellationSerializeAtTheOwningBusinessRow()
    {
        string connection = await SetupAsync();
        Guid id = Guid.NewGuid();
        await OpenAsync(connection, id);
        await ReleaseAsync(connection, Due);
        var message = await DispatchAsync(connection);
        await using var receiver = PersonnelContext(connection);
        await using var transaction = await receiver.Database.BeginTransactionAsync(Token);
        Assert.Equal(
            Acceptance.Accepted,
            await Personnel.AcceptAsync(receiver, Scope, message, Due, Token)
        );
        var cancellation = WriteAsync(
            connection,
            database => Personnel.FinishAsync(database, Scope, id, CaseStatus.Cancelled, Token)
        );
        await WaitForBlockedAsync(connection);
        Assert.False(cancellation.IsCompleted);
        await receiver.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
        Assert.Equal(Suppression.AlreadyReleased, await cancellation);
        await using var read = PersonnelContext(connection);
        Assert.Equal(1, await read.Set<ReminderNotice>().CountAsync(Token));
        Assert.Equal(
            CaseStatus.Cancelled,
            (await read.Set<ProbationCase>().SingleAsync(Token)).Status
        );
    }

    [Fact]
    public async Task ExtensionRacingLockedReleaseInvalidatesTheDeliveredOldOccurrence()
    {
        string connection = await SetupAsync();
        Guid id = Guid.NewGuid();
        await OpenAsync(connection, id);
        await using var releaser = PersonnelContext(connection);
        await using var transaction = await releaser.Database.BeginTransactionAsync(Token);
        await NativeDueIntents.ReleaseAsync(releaser, Scope, Due, 1, Token);
        var extension = WriteAsync(
            connection,
            database => Personnel.ReplaceAsync(database, Scope, id, "new-hr", Due.AddDays(7), Token)
        );
        await WaitForBlockedAsync(connection);
        await releaser.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
        Assert.Equal(Suppression.AlreadyReleased, (await extension).Previous);
        Assert.Equal(
            Acceptance.Stale,
            await AcceptAsync(connection, await DispatchAsync(connection), Due.AddDays(7))
        );
        Assert.Empty(await ReleaseAsync(connection, Due));
        Assert.Single(await ReleaseAsync(connection, Due.AddDays(7)));
    }
}
