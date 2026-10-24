using System.Text.Json;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.Workflows;
using Gloaming.Messaging.Workflows.EntityFrameworkCore;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Gloaming.Messaging.WorkflowPostgresTests.WorkflowProof;

namespace Gloaming.Messaging.WorkflowPostgresTests;

public sealed class ExecutionTests(PostgreSqlFixture fixture)
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SavedContinuationTakesExistingOrCreatedResourceBranchWithoutReplayingStart(
        bool existing
    )
    {
        var connection = await Database(fixture);
        var methods = new WorkspaceMethods();
        var key = new WorkflowKey(Guid.NewGuid());
        var input = new SetupWorkspace("workspace", existing ? Guid.NewGuid() : null);
        var snapshot = await Start(connection, methods, key, input);
        Assert.Equal(existing ? "inspected" : "created", snapshot.ContinuationName);
        var next = await Continue(connection, methods, snapshot, true);
        Assert.Equal("activated", next.ContinuationName);
        var final = await Continue(connection, methods, next, true);
        Assert.Equal(WorkflowStatus.Completed, final.Status);
        Assert.Equal("ready", final.State.Phase);
        Assert.Equal(3, final.Version);
        Assert.Equal(
            new[] { "start", existing ? "inspected" : "created", "activated" },
            methods.Invocations
        );
        await using var fresh = Context(connection);
        var restored = await Runner(fresh, methods).ReadAsync(key, Token);
        Assert.Equal(final, restored);
        Assert.Equal(2, await fresh.Set<OutboxMessageRecord>().CountAsync(Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task ActivationRefusalCompensatesOnlyResourceCreatedByThisConsumer(bool existing)
    {
        var connection = await Database(fixture);
        var methods = new WorkspaceMethods();
        var snapshot = await Start(
            connection,
            methods,
            new(Guid.NewGuid()),
            new("workspace", existing ? Guid.NewGuid() : null)
        );
        snapshot = await Continue(connection, methods, snapshot, true);
        snapshot = await Continue(connection, methods, snapshot, false);
        if (existing)
        {
            Assert.Equal(WorkflowStatus.Completed, snapshot.Status);
            Assert.Equal("activation-refused", snapshot.State.Phase);
        }
        else
        {
            Assert.Equal("released", snapshot.ContinuationName);
            snapshot = await Continue(connection, methods, snapshot, true);
            Assert.Equal("compensated", snapshot.State.Phase);
        }

        await using var db = Context(connection);
        Assert.Equal(
            existing ? 0 : 1,
            await db.Set<OutboxMessageRecord>()
                .CountAsync(row => row.MessageName == "resource.release", Token)
        );
    }

    [Fact]
    public async Task EqualStartReturnsCurrentStateWithoutRegeneratingBusinessIdsOrRequests()
    {
        var connection = await Database(fixture);
        var methods = new WorkspaceMethods();
        var key = new WorkflowKey(Guid.NewGuid());
        var first = await Start(connection, methods, key);
        var advanced = await Continue(connection, methods, first, true);
        var repeated = await Start(connection, methods, key);
        Assert.Equal(advanced, repeated);
        Assert.Equal(1, methods.Invocations.Count(name => name == "start"));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Start(connection, methods, key, new("different"))
        );
        await using var db = Context(connection);
        Assert.Equal(2, await db.Set<OutboxMessageRecord>().CountAsync(Token));
    }

    [Fact]
    public async Task SemanticDuplicateIgnoresNewDeliveryIdTraceAndJsonObjectOrder()
    {
        var connection = await Database(fixture);
        var methods = new WorkspaceMethods();
        var first = await Start(connection, methods, new(Guid.NewGuid()));
        var advanced = await Continue(connection, methods, first, true);
        using var payload = JsonDocument.Parse(
            $"{{\"succeeded\":true,\"resourceId\":\"{first.State.ResourceId:D}\"}}"
        );
        var duplicate = new IncomingMessage(
            Guid.NewGuid(),
            "resources",
            "resource.created",
            1,
            payload.RootElement,
            correlationId: first.Key.Id.ToString("D"),
            causationId: first.PendingCommandId!.Value.ToString("D"),
            traceParent: "diagnostic-only"
        );
        await using var db = Context(connection);
        await using var tx = await db.Database.BeginTransactionAsync(Token);
        var result = await Runner(db, methods).HandleReplyAsync(first.Key, duplicate, Token);
        await db.SaveChangesAsync(Token);
        await tx.CommitAsync(Token);
        Assert.Equal(advanced, result);
        Assert.Equal(1, methods.Invocations.Count(name => name == "created"));
        Assert.Equal(2, await db.Set<OutboxMessageRecord>().CountAsync(Token));
    }

    [Fact]
    public async Task ConflictingHistoricalOutcomeCannotChangeCurrentState()
    {
        var connection = await Database(fixture);
        var methods = new WorkspaceMethods();
        var first = await Start(connection, methods, new(Guid.NewGuid()));
        var advanced = await Continue(connection, methods, first, true);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            Continue(connection, methods, first, false)
        );
        await using var db = Context(connection);
        Assert.Equal(advanced, await Runner(db, methods).ReadAsync(first.Key, Token));
        Assert.Equal(2, await db.Set<OutboxMessageRecord>().CountAsync(Token));
    }

    [Fact]
    public async Task OperatorRetryKeepsResourceIdAndOlderRefusalCannotOverwriteNewOperation()
    {
        var connection = await Database(fixture);
        var methods = new WorkspaceMethods();
        var first = await Start(connection, methods, new(Guid.NewGuid()));
        var activated = await Continue(connection, methods, first, true);
        var released = await Continue(connection, methods, activated, false);
        var refused = await Continue(connection, methods, released, false);
        Assert.Equal(WorkflowStatus.AwaitingInput, refused.Status);
        WorkflowSnapshot<WorkspaceState> retry;
        await using (var db = Context(connection))
        {
            await using var tx = await db.Database.BeginTransactionAsync(Token);
            retry = await Runner(db, methods).SignalAsync(first.Key, new RetryRelease(), Token);
            await db.SaveChangesAsync(Token);
            await tx.CommitAsync(Token);
        }

        Assert.Equal(released.State.ResourceId, retry.State.ResourceId);
        Assert.NotEqual(released.PendingCommandId, retry.PendingCommandId);
        Assert.Equal(retry, await Continue(connection, methods, released, false));
        Assert.Equal("compensated", (await Continue(connection, methods, retry, true)).State.Phase);
        Assert.Equal(1, methods.Invocations.Count(name => name == "retry"));
    }

    [Theory]
    [InlineData("tenant")]
    [InlineData("correlation")]
    [InlineData("causation")]
    [InlineData("alias")]
    [InlineData("schema")]
    [InlineData("resource")]
    public async Task WrongEnvelopeOrBusinessIdentityLeavesStateAndOutboxUntouched(string mismatch)
    {
        var connection = await Database(fixture);
        var methods = new WorkspaceMethods();
        var first = await Start(connection, methods, new(Guid.NewGuid()));
        var reply = Reply(first, true);
        var wrong = new IncomingMessage(
            reply.MessageId,
            reply.ProducerKey,
            mismatch == "alias" ? "unknown" : reply.MessageName,
            mismatch == "schema" ? 2 : 1,
            mismatch == "resource"
                ? JsonSerializer.SerializeToElement(
                    new ResourceReply(Guid.NewGuid(), true),
                    WorkspaceMethods.Json
                )
                : reply.Payload,
            mismatch == "tenant" ? "other" : null,
            mismatch == "correlation" ? Guid.NewGuid().ToString("D") : reply.CorrelationId,
            mismatch == "causation" ? Guid.NewGuid().ToString("D") : reply.CausationId
        );
        await using var db = Context(connection);
        await using var tx = await db.Database.BeginTransactionAsync(Token);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            Runner(db, methods).HandleReplyAsync(first.Key, wrong, Token)
        );
        Assert.Equal(1, await db.Set<OutboxMessageRecord>().CountAsync(Token));
        Assert.Equal(
            EntityState.Unchanged,
            db.Entry(await db.Set<WorkflowInstanceRecord>().SingleAsync(Token)).State
        );
    }

    [Fact]
    public async Task RequiredReplyFieldFailureDoesNotRecordOutcomeOrEnqueueNextRequest()
    {
        var connection = await Database(fixture);
        var methods = new WorkspaceMethods();
        var first = await Start(connection, methods, new(Guid.NewGuid()));
        var malformed = new IncomingMessage(
            Guid.NewGuid(),
            "resources",
            "resource.created",
            1,
            JsonSerializer.SerializeToElement(
                new { first.State.ResourceId },
                WorkspaceMethods.Json
            ),
            correlationId: first.Key.Id.ToString("D"),
            causationId: first.PendingCommandId!.Value.ToString("D")
        );
        await using var db = Context(connection);
        await using var tx = await db.Database.BeginTransactionAsync(Token);
        await Assert.ThrowsAsync<JsonException>(() =>
            Runner(db, methods).HandleReplyAsync(first.Key, malformed, Token)
        );
        await db.SaveChangesAsync(Token);
        await tx.CommitAsync(Token);
        Assert.Equal(first, await Runner(db, methods).ReadAsync(first.Key, Token));
        Assert.Equal(1, await db.Set<OutboxMessageRecord>().CountAsync(Token));
    }

    [Fact]
    public async Task ForeignTenantCannotReadOrContinueAnInstance()
    {
        var connection = await Database(fixture);
        var methods = new WorkspaceMethods();
        var key = new WorkflowKey(Guid.NewGuid(), "alpha");
        var first = await Start(connection, methods, key);
        await using var db = Context(connection);
        var foreign = new WorkflowKey(key.Id, "beta");
        Assert.Null(await Runner(db, methods).ReadAsync(foreign, Token));
        await using var tx = await db.Database.BeginTransactionAsync(Token);
        var reply = Reply(first, true);
        var foreignReply = new IncomingMessage(
            reply.MessageId,
            reply.ProducerKey,
            reply.MessageName,
            reply.SchemaVersion,
            reply.Payload,
            "beta",
            reply.CorrelationId,
            reply.CausationId
        );
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            Runner(db, methods).HandleReplyAsync(foreign, foreignReply, Token)
        );
    }

    [Fact]
    public async Task DefinitionMismatchRequiresExplicitMigration()
    {
        var connection = await Database(fixture);
        var methods = new WorkspaceMethods();
        var key = new WorkflowKey(Guid.NewGuid());
        await Start(connection, methods, key);
        await using var db = Context(connection);
        await Assert.ThrowsAsync<InvalidDataException>(() =>
            Runner(db, methods, version: 2).ReadAsync(key, Token)
        );
    }
}
