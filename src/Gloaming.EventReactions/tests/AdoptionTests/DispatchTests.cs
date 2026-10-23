using Gloaming.EventReactions.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ModulithFoundry.Tests.Infrastructure;

namespace ModulithFoundry.Samples.EventReactions.Consumer.Tests;

public sealed partial class DispatchTests(PostgreSqlFixture postgres)
    : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;
    private static readonly Guid First = Guid.Parse("d21a0000-0000-0000-0000-000000000011");
    private static readonly Guid Second = Guid.Parse("d21a0000-0000-0000-0000-000000000012");

    [Fact]
    public async Task NativeReactionExecutableCommitsTheImmutableModelCascade()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(Token);
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        string connection = await postgres.CreateDatabaseAsync(deadline.Token);
        await using var child = new ChildHost(
            typeof(ReactionExample).Assembly,
            new() { ["DOMAIN_EVENTS_ASSESSMENT_CONNECTION_STRING"] = connection }
        );
        Assert.True(await child.WaitForExitAsync(deadline.Token) == 0, child.Output);
        Assert.Contains(
            "reserved=60, receipt=1, allocation=1",
            child.Output,
            StringComparison.Ordinal
        );
    }

    [Theory]
    [InlineData("direct", "sync")]
    [InlineData("direct", "sync-false")]
    [InlineData("direct", "async")]
    [InlineData("direct", "async-false")]
    [InlineData("mapped", "sync")]
    [InlineData("mapped", "sync-false")]
    [InlineData("mapped", "async")]
    [InlineData("mapped", "async-false")]
    public async Task AllNativeSaveOverloadsApplyTwoHandlersAndTheirCascadeAtomically(
        string shape,
        string save
    )
    {
        if (shape == "direct")
            Assert.Equal(typeof(OwnedEntity), typeof(DirectOrder).BaseType);

        string connection = await SetupAsync(shape);
        await using var operation = new Scenario(shape, connection);
        await using var transaction = await operation.Context.Database.BeginTransactionAsync(Token);
        await operation.SubmitAsync(First, Token);
        await SaveAsync(operation.Context, save);
        Assert.Equal(0, operation.PendingCount);
        await AssertStateAsync(shape, connection, false, 0, 1, 0);
        if (save.EndsWith("false", StringComparison.Ordinal))
            operation.AcceptAllChanges();

        Assert.Equal(0, await operation.Context.SaveChangesAsync(Token));
        await transaction.CommitAsync(Token);
        await AssertStateAsync(shape, connection, true, 40, 2, 1);
    }

    [Theory]
    [InlineData("direct")]
    [InlineData("mapped")]
    public async Task ExplicitStagingRetainsFactsAndLaterSavesProcessOnlyNewDecisions(string shape)
    {
        string connection = await SetupAsync(shape, secondAmount: 30);
        await using var operation = new Scenario(shape, connection);
        await using var transaction = await operation.Context.Database.BeginTransactionAsync(Token);
        await operation.SubmitAsync(First, Token);
        Assert.Equal(1, operation.PendingCount);
        await operation.Context.SaveChangesAsync(Token);
        Assert.Equal(0, await operation.Context.SaveChangesAsync(Token));
        await operation.SubmitAsync(Second, Token);
        await operation.Context.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
        await AssertStateAsync(shape, connection, true, 70, 3, 2);
    }

    [Theory]
    [InlineData("direct", "sync")]
    [InlineData("direct", "sync-false")]
    [InlineData("direct", "async")]
    [InlineData("direct", "async-false")]
    [InlineData("mapped", "sync")]
    [InlineData("mapped", "sync-false")]
    [InlineData("mapped", "async")]
    [InlineData("mapped", "async-false")]
    public async Task DeletionReleasesAllowanceAndAcknowledgesFactsAfterRootDetachment(
        string shape,
        string save
    )
    {
        string connection = await SetupAsync(shape);
        await SubmitAndCommitAsync(shape, connection);
        await using var operation = new Scenario(shape, connection);
        await using var transaction = await operation.Context.Database.BeginTransactionAsync(Token);
        object root = await operation.DeleteAsync(First, Token);
        Assert.Equal(EntityState.Deleted, operation.Context.Entry(root).State);
        await SaveAsync(operation.Context, save);
        if (save.EndsWith("false", StringComparison.Ordinal))
        {
            Assert.Equal(EntityState.Deleted, operation.Context.Entry(root).State);
            operation.AcceptAllChanges();
        }

        Assert.Equal(EntityState.Detached, operation.Context.Entry(root).State);
        Assert.Equal(0, operation.PendingCount);
        await transaction.CommitAsync(Token);
        await using var observer = new Scenario(shape, connection);
        Assert.Null(await observer.ReadAsync(First, Token));
        Assert.Equal(new AllowanceState(0, 3, 0, 0), await observer.StateAsync(Token));
    }

    [Theory]
    [InlineData("direct")]
    [InlineData("mapped")]
    public async Task CapabilitiesDoNotCommitTheCallerTransaction(string shape)
    {
        string connection = await SetupAsync(shape);
        await using var operation = new Scenario(shape, connection);
        await using var transaction = await operation.Context.Database.BeginTransactionAsync(Token);
        await operation.SubmitAsync(First, Token);
        await operation.Context.SaveChangesAsync(Token);
        Assert.Same(transaction, operation.Context.Database.CurrentTransaction);
        await AssertStateAsync(shape, connection, false, 0, 1, 0);
        await transaction.RollbackAsync(CancellationToken.None);
        await AssertStateAsync(shape, connection, false, 0, 1, 0);
    }

    [Theory]
    [InlineData("direct")]
    [InlineData("mapped")]
    public async Task MissingCallerTransactionRejectsHandlingBeforeAnyRequiredEffect(string shape)
    {
        string connection = await SetupAsync(shape);
        await using var operation = new Scenario(shape, connection);
        await operation.SubmitAsync(First, Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            operation.Context.SaveChangesAsync(Token)
        );
        await AssertStateAsync(shape, connection, false, 0, 1, 0);
    }

    [Theory]
    [InlineData("direct")]
    [InlineData("mapped")]
    public async Task FalseAcceptanceUsesNativeConcurrencyWhenIncorrectlySavedTwice(string shape)
    {
        string connection = await SetupAsync(shape);
        await using var operation = new Scenario(shape, connection);
        await using var transaction = await operation.Context.Database.BeginTransactionAsync(Token);
        await operation.SubmitAsync(First, Token);
        await operation.Context.SaveChangesAsync(false, Token);
        await Assert.ThrowsAnyAsync<DbUpdateException>(() =>
            operation.Context.SaveChangesAsync(Token)
        );
        await transaction.RollbackAsync(CancellationToken.None);
        await AssertStateAsync(shape, connection, false, 0, 1, 0);
    }

    [Theory]
    [InlineData("direct")]
    [InlineData("mapped")]
    public async Task SyncSaveRejectsAsyncOnlyHandlersWithoutBlocking(string shape)
    {
        string connection = await SetupAsync(shape);
        await using var operation = new Scenario(shape, connection, asyncOnly: true);
        await using var transaction = await operation.Context.Database.BeginTransactionAsync(Token);
        await operation.SubmitAsync(First, Token);
        Assert.Throws<InvalidOperationException>(() => operation.Context.SaveChanges());
        await transaction.RollbackAsync(CancellationToken.None);
        await AssertStateAsync(shape, connection, false, 0, 1, 0);
    }

    [Theory]
    [InlineData("direct")]
    [InlineData("mapped")]
    public async Task AsyncSavingHandlesAsyncOnlyHandlersWithoutBlocking(string shape)
    {
        string connection = await SetupAsync(shape);
        await using var operation = new Scenario(shape, connection, asyncOnly: true);
        await using var transaction = await operation.Context.Database.BeginTransactionAsync(Token);
        await operation.SubmitAsync(First, Token);
        await operation.Context.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
        await AssertStateAsync(shape, connection, true, 40, 2, 1);
    }

    [Theory]
    [InlineData("direct")]
    [InlineData("mapped")]
    public async Task NativeAcceptanceDoesNotWithdrawAlreadyRaisedFacts(string shape)
    {
        string connection = await SetupAsync(shape);
        await using var operation = new Scenario(shape, connection);
        await using var transaction = await operation.Context.Database.BeginTransactionAsync(Token);
        await operation.SubmitAsync(First, Token);
        operation.Context.ChangeTracker.DetectChanges();
        operation.AcceptAllChanges();
        await operation.Context.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
        // Native acceptance discards the producer edit; its raised fact is still dispatched.
        await AssertStateAsync(shape, connection, false, 40, 2, 1);
    }

    [Theory]
    [InlineData("direct")]
    [InlineData("mapped")]
    public async Task EmptyFalseAcceptanceSaveCanBeAcknowledgedBeforeALaterDecision(string shape)
    {
        string connection = await SetupAsync(shape);
        await using var operation = new Scenario(shape, connection);
        await using var transaction = await operation.Context.Database.BeginTransactionAsync(Token);
        Assert.Equal(0, await operation.Context.SaveChangesAsync(false, Token));
        operation.AcceptAllChanges();
        await operation.SubmitAsync(First, Token);
        await operation.Context.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
        await AssertStateAsync(shape, connection, true, 40, 2, 1);
    }

    private async Task<string> SetupAsync(
        string shape,
        decimal firstAmount = 40,
        decimal? secondAmount = null
    )
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using var setup = new Scenario(shape, connection);
        await setup.Context.Database.MigrateAsync(Token);
        setup.Seed(First, firstAmount);
        if (secondAmount is not null)
            setup.AddDraft(Second, secondAmount.Value);

        await setup.Context.SaveChangesAsync(Token);
        return connection;
    }

    private static async Task SubmitAndCommitAsync(string shape, string connection)
    {
        await using var operation = new Scenario(shape, connection);
        await using var transaction = await operation.Context.Database.BeginTransactionAsync(Token);
        await operation.SubmitAsync(First, Token);
        await operation.Context.SaveChangesAsync(Token);
        await transaction.CommitAsync(Token);
    }

    private static async Task AssertStateAsync(
        string shape,
        string connection,
        bool submitted,
        decimal reserved,
        long version,
        int count
    )
    {
        await using var observer = new Scenario(shape, connection);
        var root = await observer.ReadAsync(First, Token);
        Assert.NotNull(root);
        Assert.Equal(submitted, root.Submitted);
        Assert.Equal(submitted ? 2 : 1, root.Version);
        Assert.Equal(
            new AllowanceState(reserved, version, count, count),
            await observer.StateAsync(Token)
        );
    }

    private static Task SaveAsync(DbContext context, string save)
    {
        if (save == "sync")
            context.SaveChanges();
        else if (save == "sync-false")
            context.SaveChanges(false);
        else if (save == "async")
            return context.SaveChangesAsync(Token);
        else
            return context.SaveChangesAsync(false, Token);

        return Task.CompletedTask;
    }
}
