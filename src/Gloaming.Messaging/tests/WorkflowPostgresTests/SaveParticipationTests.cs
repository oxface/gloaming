using System.Data.Common;
using Gloaming.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Gloaming.Messaging.Workflows;
using Gloaming.Messaging.Workflows.EntityFrameworkCore;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using static Gloaming.Messaging.WorkflowPostgresTests.WorkflowProof;

namespace Gloaming.Messaging.WorkflowPostgresTests;

public sealed class SaveParticipationTests(PostgreSqlFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OriginalValueChangesCannotOmitTheProposedContinuationState(bool asynchronous)
    {
        string connection = await Database(fixture);
        var methods = new WorkspaceMethods();
        var first = await Start(connection, methods, new(Guid.NewGuid()));
        await using (var db = Context(connection))
        {
            await using var tx = await db.Database.BeginTransactionAsync(Token);
            await Runner(db, methods).HandleReplyAsync(first.Key, Reply(first, true), Token);
            var process = db.ChangeTracker.Entries<WorkflowInstanceRecord>().Single();
            process.Property(row => row.State).OriginalValue = process.Entity.State;
            process.Property(row => row.State).IsModified = false;

            if (asynchronous)
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    db.SaveChangesAsync(Token)
                );
            else
                Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        }

        await using (var observer = Context(connection))
        {
            Assert.Equal(first, await Runner(observer, methods).ReadAsync(first.Key, Token));
            Assert.Equal(1, await observer.Set<OutboxMessageRecord>().CountAsync(Token));
        }

        var recovered = await Continue(connection, methods, first, true);
        Assert.Equal(2, recovered.Version);
        Assert.Equal("activate", recovered.State.Phase);
        await using var final = Context(connection);
        Assert.Equal(recovered, await Runner(final, methods).ReadAsync(first.Key, Token));
        Assert.Equal(2, await final.Set<OutboxMessageRecord>().CountAsync(Token));
    }

    [Theory]
    [InlineData(false, false, "process")]
    [InlineData(false, true, "process")]
    [InlineData(false, false, "request")]
    [InlineData(false, true, "request")]
    [InlineData(false, false, "all")]
    [InlineData(false, true, "all")]
    [InlineData(true, false, "process")]
    [InlineData(true, true, "process")]
    [InlineData(true, false, "request")]
    [InlineData(true, true, "request")]
    [InlineData(true, false, "all")]
    [InlineData(true, true, "all")]
    public async Task PrematureAcceptanceCannotSplitProcessAndRequest(
        bool continuation,
        bool asynchronous,
        string suppressed
    )
    {
        string connection = await Database(fixture);
        var methods = new WorkspaceMethods();
        var key = new WorkflowKey(Guid.NewGuid());
        var first = continuation ? await Start(connection, methods, key) : null;
        await using (var db = Context(connection))
        {
            await using var tx = await db.Database.BeginTransactionAsync(Token);
            if (first is null)
                await Runner(db, methods).StartAsync(key, new("workspace"), Token);
            else
                await Runner(db, methods).HandleReplyAsync(key, Reply(first, true), Token);

            if (suppressed == "all")
                db.ChangeTracker.AcceptAllChanges();
            else if (suppressed == "process")
                db.ChangeTracker.Entries<WorkflowInstanceRecord>().Single().State =
                    EntityState.Unchanged;
            else
                db.ChangeTracker.Entries<OutboxMessageRecord>().Single().State =
                    EntityState.Unchanged;

            if (asynchronous)
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    db.SaveChangesAsync(Token)
                );
            else
                Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        }

        await using (var observer = Context(connection))
        {
            Assert.Equal(first, await Runner(observer, methods).ReadAsync(key, Token));
            Assert.Equal(
                continuation ? 1 : 0,
                await observer.Set<OutboxMessageRecord>().CountAsync(Token)
            );
        }

        var recovered = first is null
            ? await Start(connection, methods, key)
            : await Continue(connection, methods, first, true);
        Assert.Equal(continuation ? 2 : 1, recovered.Version);
        await using var committed = Context(connection);
        Assert.Equal(
            continuation ? 2 : 1,
            await committed.Set<OutboxMessageRecord>().CountAsync(Token)
        );
    }

    [Theory]
    [InlineData("sync")]
    [InlineData("sync-false")]
    [InlineData("async")]
    [InlineData("async-false")]
    public async Task NativeSaveFormsPreserveRepeatedStartContinuationAndCompletionSaves(
        string save
    )
    {
        string connection = await Database(fixture);
        var methods = new WorkspaceMethods();
        var key = new WorkflowKey(Guid.NewGuid());
        WorkflowSnapshot<WorkspaceState>? previous = null;
        for (int turn = 1; turn <= 3; turn++)
        {
            await using var db = Context(connection);
            await using var tx = await db.Database.BeginTransactionAsync(Token);
            var next = previous is null
                ? await Runner(db, methods).StartAsync(key, new("workspace"), Token)
                : await Runner(db, methods).HandleReplyAsync(key, Reply(previous, true), Token);
            Assert.Equal(turn, next.Version);
            if (save == "sync")
                db.SaveChanges();
            else if (save == "sync-false")
                db.SaveChanges(false);
            else if (save == "async")
                await db.SaveChangesAsync(Token);
            else
                await db.SaveChangesAsync(false, Token);

            if (save.EndsWith("false", StringComparison.Ordinal))
                db.ChangeTracker.AcceptAllChanges();

            Assert.Equal(0, db.SaveChanges());
            Assert.Equal(0, await db.SaveChangesAsync(Token));
            await tx.CommitAsync(Token);
            previous = next;
        }

        await using var observer = Context(connection);
        Assert.Equal(previous, await Runner(observer, methods).ReadAsync(key, Token));
        Assert.Equal(WorkflowStatus.Completed, previous!.Status);
        Assert.Equal(2, await observer.Set<OutboxMessageRecord>().CountAsync(Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task RolledBackSavepointCannotPassLaterParticipationValidation(bool continuation)
    {
        string connection = await Database(fixture);
        var methods = new WorkspaceMethods();
        var key = new WorkflowKey(Guid.NewGuid());
        var first = continuation ? await Start(connection, methods, key) : null;
        await using (var db = Context(connection))
        {
            await using var tx = await db.Database.BeginTransactionAsync(Token);
            await tx.CreateSavepointAsync("before_turn", Token);
            if (first is null)
                await Runner(db, methods).StartAsync(key, new("workspace"), Token);
            else
                await Runner(db, methods).HandleReplyAsync(key, Reply(first, true), Token);

            await db.SaveChangesAsync(Token);
            await tx.RollbackToSavepointAsync("before_turn", Token);
            await Assert.ThrowsAsync<InvalidOperationException>(() => db.SaveChangesAsync(Token));
        }

        await using var observer = Context(connection);
        Assert.Equal(first, await Runner(observer, methods).ReadAsync(key, Token));
        Assert.Equal(
            continuation ? 1 : 0,
            await observer.Set<OutboxMessageRecord>().CountAsync(Token)
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SharedCoordinatorPreservesBothSaveFormsAndRejectsEitherSuppressedWrite(
        bool asynchronous
    )
    {
        string connection = await Database(fixture);
        var methods = new WorkspaceMethods();
        var services = new ServiceCollection();
        services.AddOutboxValidation<CoordinatedDatabase>();
        services.AddInboxValidation<CoordinatedDatabase>();
        services.AddWorkflowValidation<CoordinatedDatabase>();
        services.AddDbContext<CoordinatedDatabase>(
            (provider, options) =>
            {
                options.UseNpgsql(connection);
                if (asynchronous)
                    options.AddInterceptors(new RejectSynchronousReads());

                options.ConfigureGloamingCapabilities<CoordinatedDatabase>(provider);
            }
        );
        await using var provider = services.BuildServiceProvider();
        foreach (string suppressed in new[] { "none", "process", "request" })
        {
            await using var scope = provider.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<CoordinatedDatabase>();
            await using var tx = await db.Database.BeginTransactionAsync(Token);
            var runner = new EfWorkflow<CoordinatedDatabase, SetupWorkspace, WorkspaceState>(
                db,
                new EfOutbox<CoordinatedDatabase>(db),
                methods.Define()
            );
            await runner.StartAsync(new(Guid.NewGuid()), new("workspace"), Token);
            if (suppressed == "process")
                db.ChangeTracker.Entries<WorkflowInstanceRecord>().Single().State =
                    EntityState.Unchanged;
            else if (suppressed == "request")
                db.ChangeTracker.Entries<OutboxMessageRecord>().Single().State =
                    EntityState.Unchanged;

            if (suppressed == "none")
            {
                if (asynchronous)
                    await db.SaveChangesAsync(false, Token);
                else
                    db.SaveChanges(false);

                db.ChangeTracker.AcceptAllChanges();
                Assert.Equal(0, asynchronous ? await db.SaveChangesAsync(Token) : db.SaveChanges());
                await tx.CommitAsync(Token);
            }
            else if (asynchronous)
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    db.SaveChangesAsync(Token)
                );
            else
                Assert.Throws<InvalidOperationException>(() => db.SaveChanges());
        }

        await using var observer = Context(connection);
        Assert.Equal(1, await observer.Set<WorkflowInstanceRecord>().CountAsync(Token));
        Assert.Equal(1, await observer.Set<OutboxMessageRecord>().CountAsync(Token));
    }

    private sealed class CoordinatedDatabase(DbContextOptions<CoordinatedDatabase> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder model)
        {
            model.ConfigureWorkflows("operations", "processes", "jsonb");
            model.ConfigurePostgresOutbox("operations", "outgoing");
            model.ConfigurePostgresInbox("operations", "incoming");
        }
    }

    private sealed class RejectSynchronousReads : DbCommandInterceptor
    {
        public override InterceptionResult<DbDataReader> ReaderExecuting(
            DbCommand command,
            CommandEventData eventData,
            InterceptionResult<DbDataReader> result
        ) =>
            throw new InvalidOperationException("Async saving must not perform synchronous reads.");
    }
}
