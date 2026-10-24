using System.Text.Json;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.Workflows;
using Gloaming.Messaging.Workflows.EntityFrameworkCore;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Gloaming.Messaging.WorkflowPostgresTests.WorkflowProof;

namespace Gloaming.Messaging.WorkflowPostgresTests;

public sealed class TransactionTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task SaveGuardRejectsChangedOriginalConcurrencyToken()
    {
        var connection = await Database(fixture);
        var methods = new WorkspaceMethods();
        var first = await Start(connection, methods, new(Guid.NewGuid()));
        await using var db = Context(connection);
        await using var tx = await db.Database.BeginTransactionAsync(Token);
        await Runner(db, methods).HandleReplyAsync(first.Key, Reply(first, true), Token);
        var row = db.ChangeTracker.Entries<WorkflowInstanceRecord>().Single();
        row.Property(value => value.Version).OriginalValue = 2;
        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync(Token));
        await tx.RollbackAsync(Token);
        await using var observer = Context(connection);
        Assert.Equal(first, await Runner(observer, methods).ReadAsync(first.Key, Token));
        Assert.Equal(1, await observer.Set<OutboxMessageRecord>().CountAsync(Token));
    }

    [Fact]
    public async Task UnknownReturnedBindingAndAmbientTransactionFailBeforeTracking()
    {
        var connection = await Database(fixture);
        await using var db = Context(connection);
        var key = new WorkflowKey(Guid.NewGuid());
        var definition = new WorkflowBuilder<SetupWorkspace, WorkspaceState>(
            "workspace",
            1,
            WorkspaceMethods.Json
        )
            .StartWith(
                (context, input) =>
                    WorkflowTurn<WorkspaceState>.Request(
                        new(Guid.NewGuid(), true, "create"),
                        OutgoingMessage.FromPayload(
                            context.CommandId,
                            "create",
                            "create",
                            1,
                            input,
                            WorkspaceMethods.Json,
                            correlationId: context.Key.Id.ToString("D")
                        ),
                        "not-bound"
                    )
            )
            .Build();
        var runner = new EfWorkflow<WorkflowDatabase, SetupWorkspace, WorkspaceState>(
            db,
            new EfOutbox<WorkflowDatabase>(db),
            definition
        );
        using (
            var ambient = new System.Transactions.TransactionScope(
                System.Transactions.TransactionScopeAsyncFlowOption.Enabled
            )
        )
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                runner.StartAsync(key, new("workspace"), Token)
            );

        await using var tx = await db.Database.BeginTransactionAsync(Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.StartAsync(key, new("workspace"), Token)
        );
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task SignalCannotSelectAnArbitraryMethodOrRunWhileWaiting()
    {
        var connection = await Database(fixture);
        var methods = new WorkspaceMethods();
        var first = await Start(connection, methods, new(Guid.NewGuid()));
        await using (var db = Context(connection))
        {
            await using var tx = await db.Database.BeginTransactionAsync(Token);
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                Runner(db, methods).SignalAsync(first.Key, new RetryRelease(), Token)
            );
        }

        var activate = await Continue(connection, methods, first, true);
        var release = await Continue(connection, methods, activate, false);
        await Continue(connection, methods, release, false);
        await using var intervention = Context(connection);
        await using var interventionTx = await intervention.Database.BeginTransactionAsync(Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Runner(intervention, methods)
                .SignalAsync(first.Key, new SetupWorkspace("wrong-signal"), Token)
        );
        Assert.DoesNotContain("retry", methods.Invocations);
    }

    [Fact]
    public async Task CandidateMutationBeforeHandlerFailureDoesNotChangeTrackedState()
    {
        var connection = await Database(fixture);
        var methods = new WorkspaceMethods();
        var first = await Start(connection, methods, new(Guid.NewGuid()));
        methods.ThrowAfterCandidateMutation = true;
        await using var db = Context(connection);
        await using var tx = await db.Database.BeginTransactionAsync(Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Runner(db, methods).HandleReplyAsync(first.Key, Reply(first, true), Token)
        );
        await db.SaveChangesAsync(Token);
        await tx.CommitAsync(Token);
        Assert.Equal(first, await Runner(db, methods).ReadAsync(first.Key, Token));
        Assert.Equal(1, await db.Set<OutboxMessageRecord>().CountAsync(Token));
    }

    [Fact]
    public async Task PooledContextLeaseDoesNotCarryAnotherScopesProposal()
    {
        var connection = await Database(fixture);
        var methods = new WorkspaceMethods();
        var services = new ServiceCollection();
        services.AddDbContextPool<WorkflowDatabase>(
            options => options.UseNpgsql(connection),
            poolSize: 1
        );
        await using var provider = services.BuildServiceProvider();
        Guid objectId = Guid.Empty;
        for (int attempt = 0; attempt < 2; attempt++)
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<WorkflowDatabase>();
            if (attempt == 0)
                objectId = db.ContextId.InstanceId;
            else
                Assert.Equal(objectId, db.ContextId.InstanceId);

            await using var tx = await db.Database.BeginTransactionAsync(Token);
            await Runner(db, methods).StartAsync(new(Guid.NewGuid()), new("workspace"), Token);
            await db.SaveChangesAsync(Token);
            await tx.CommitAsync(Token);
        }

        await using var observer = Context(connection);
        Assert.Equal(2, await observer.Set<WorkflowInstanceRecord>().CountAsync(Token));
    }

    [Fact]
    public async Task WritesRequireNativeTransactionAndReturnedRequestMustRetainExecutionIdentity()
    {
        var connection = await Database(fixture);
        var methods = new WorkspaceMethods { InvalidRequest = true };
        var key = new WorkflowKey(Guid.NewGuid());
        await using var db = Context(connection);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Runner(db, methods).StartAsync(key, new("workspace"), Token)
        );
        await using var tx = await db.Database.BeginTransactionAsync(Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Runner(db, methods).StartAsync(key, new("workspace"), Token)
        );
        Assert.Empty(db.ChangeTracker.Entries());
    }

    [Fact]
    public async Task RolledBackStartLeavesNoProcessOrRequestAndFreshStartCanRecover()
    {
        var connection = await Database(fixture);
        var methods = new WorkspaceMethods();
        var key = new WorkflowKey(Guid.NewGuid());
        await using (var db = Context(connection))
        {
            await using var tx = await db.Database.BeginTransactionAsync(Token);
            await Runner(db, methods).StartAsync(key, new("workspace"), Token);
            await db.SaveChangesAsync(Token);
            await tx.RollbackAsync(Token);
        }

        await using (var observer = Context(connection))
        {
            Assert.Null(await Runner(observer, methods).ReadAsync(key, Token));
            Assert.Empty(await observer.Set<OutboxMessageRecord>().ToArrayAsync(Token));
        }

        Assert.Equal(1, (await Start(connection, methods, key)).Version);
    }

    [Fact]
    public async Task RolledBackContinuationRetainsPendingOperationAndFreshContextCanRetry()
    {
        var connection = await Database(fixture);
        var methods = new WorkspaceMethods();
        var first = await Start(connection, methods, new(Guid.NewGuid()));
        await using (var db = Context(connection))
        {
            await using var tx = await db.Database.BeginTransactionAsync(Token);
            await Runner(db, methods).HandleReplyAsync(first.Key, Reply(first, true), Token);
            await db.SaveChangesAsync(Token);
            await tx.RollbackAsync(Token);
        }

        await using (var observer = Context(connection))
        {
            Assert.Equal(first, await Runner(observer, methods).ReadAsync(first.Key, Token));
            Assert.Equal(1, await observer.Set<OutboxMessageRecord>().CountAsync(Token));
        }

        Assert.Equal(2, (await Continue(connection, methods, first, true)).Version);
    }

    [Fact]
    public async Task CompetingNativeTransactionsCommitOneTurnAndLosingRequestRollsBack()
    {
        var connection = await Database(fixture);
        var methods = new WorkspaceMethods();
        var first = await Start(connection, methods, new(Guid.NewGuid()));
        await using var winner = Context(connection);
        await using var loser = Context(connection);
        await using var winningTx = await winner.Database.BeginTransactionAsync(Token);
        await using var losingTx = await loser.Database.BeginTransactionAsync(Token);
        var committed = await Runner(winner, methods)
            .HandleReplyAsync(first.Key, Reply(first, true), Token);
        var rejected = await Runner(loser, methods)
            .HandleReplyAsync(first.Key, Reply(first, true), Token);
        Assert.NotEqual(committed.PendingCommandId, rejected.PendingCommandId);
        await winner.SaveChangesAsync(Token);
        await winningTx.CommitAsync(Token);
        await Assert.ThrowsAsync<DbUpdateConcurrencyException>(() => loser.SaveChangesAsync(Token));
        await losingTx.RollbackAsync(Token);
        await using var observer = Context(connection);
        Assert.Equal(committed, await Runner(observer, methods).ReadAsync(first.Key, Token));
        Assert.Equal(2, await observer.Set<OutboxMessageRecord>().CountAsync(Token));
        Assert.False(
            await observer
                .Set<OutboxMessageRecord>()
                .AnyAsync(row => row.MessageId == rejected.PendingCommandId, Token)
        );
    }

    [Theory]
    [InlineData("remove-request")]
    [InlineData("change-process")]
    [InlineData("change-request")]
    public async Task SaveGuardRejectsAlteredProposals(string fault)
    {
        var connection = await Database(fixture);
        var methods = new WorkspaceMethods();
        await using var db = Context(connection);
        await using var tx = await db.Database.BeginTransactionAsync(Token);
        await Runner(db, methods).StartAsync(new(Guid.NewGuid()), new("workspace"), Token);
        var request = db.ChangeTracker.Entries<OutboxMessageRecord>().Single();
        if (fault == "remove-request")
            request.State = EntityState.Detached;
        else if (fault == "change-request")
            request.Property(row => row.RouteKey).CurrentValue = "other";
        else
            db
                .ChangeTracker.Entries<WorkflowInstanceRecord>()
                .Single()
                .Property(row => row.Version)
                .CurrentValue = 100;

        await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync(Token));
        await tx.RollbackAsync(Token);
        await using var observer = Context(connection);
        Assert.Empty(await observer.Set<WorkflowInstanceRecord>().ToArrayAsync(Token));
        Assert.Empty(await observer.Set<OutboxMessageRecord>().ToArrayAsync(Token));
    }

    [Fact]
    public async Task SaveGuardRejectsTransactionSwitchAndDirectRowMutation()
    {
        var connection = await Database(fixture);
        var methods = new WorkspaceMethods();
        var key = new WorkflowKey(Guid.NewGuid());
        await using (var db = Context(connection))
        {
            await using (var tx = await db.Database.BeginTransactionAsync(Token))
            {
                await Runner(db, methods).StartAsync(key, new("workspace"), Token);
                await tx.RollbackAsync(Token);
            }

            await using var different = await db.Database.BeginTransactionAsync(Token);
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync(Token));
        }

        await Start(connection, methods, key);
        await using var manual = Context(connection);
        await using var manualTx = await manual.Database.BeginTransactionAsync(Token);
        var row = await manual.Set<WorkflowInstanceRecord>().SingleAsync(Token);
        manual.Entry(row).Property(value => value.Version).CurrentValue = 9;
        await Assert.ThrowsAsync<InvalidOperationException>(() => manual.SaveChangesAsync(Token));
    }

    [Fact]
    public async Task SameScopeCannotExecuteAnotherTurnAfterAProposal()
    {
        var connection = await Database(fixture);
        var methods = new WorkspaceMethods();
        await using var db = Context(connection);
        await using var tx = await db.Database.BeginTransactionAsync(Token);
        var runner = Runner(db, methods);
        var first = await runner.StartAsync(new(Guid.NewGuid()), new("workspace"), Token);
        await db.SaveChangesAsync(Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.HandleReplyAsync(first.Key, Reply(first, true), Token)
        );
    }

    [Fact]
    public async Task CancellationLeavesPersistedProgressUnchanged()
    {
        var connection = await Database(fixture);
        var methods = new WorkspaceMethods();
        var first = await Start(connection, methods, new(Guid.NewGuid()));
        await using var db = Context(connection);
        await using var tx = await db.Database.BeginTransactionAsync(Token);
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Runner(db, methods).HandleReplyAsync(first.Key, Reply(first, true), cancelled.Token)
        );
        Assert.Equal(first, await Runner(db, methods).ReadAsync(first.Key, Token));
    }

    [Fact]
    public async Task MissingModelConfigurationFailsBeforeDatabaseAccess()
    {
        await using var db = new UnconfiguredDatabase(
            new DbContextOptionsBuilder<UnconfiguredDatabase>()
                .UseNpgsql("Host=unreachable;Database=none")
                .Options
        );
        var runner = new EfWorkflow<UnconfiguredDatabase, SetupWorkspace, WorkspaceState>(
            db,
            new NeverOutbox(),
            new WorkspaceMethods().Define()
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runner.ReadAsync(new(Guid.NewGuid()), Token)
        );
    }

    private sealed class UnconfiguredDatabase(DbContextOptions<UnconfiguredDatabase> options)
        : DbContext(options);

    private sealed class NeverOutbox : IOutbox<UnconfiguredDatabase>
    {
        public void Enqueue(OutgoingMessage message) =>
            throw new InvalidOperationException("Must not be reached.");
    }
}
