using System.Text.Json;
using System.Text.Json.Serialization;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.Workflows;
using Gloaming.Messaging.Workflows.EntityFrameworkCore;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using static Gloaming.Messaging.WorkflowPostgresTests.WorkflowProof;

namespace Gloaming.Messaging.WorkflowPostgresTests;

public sealed class SerializationTests(PostgreSqlFixture fixture)
{
    [Fact]
    public async Task DefinitionFreezesBindingsAndCopiesConsumerJsonOptions()
    {
        var connection = await Database(fixture);
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        var builder = new WorkflowBuilder<SetupWorkspace, WorkspaceState>(
            "frozen",
            1,
            json
        ).StartWith(
            (context, input) =>
                WorkflowTurn<WorkspaceState>.Request(
                    new(Guid.NewGuid(), true, "pending"),
                    Command(context, input),
                    "late"
                )
        );
        var frozen = builder.Build();
        // Changes to the builder or original options cannot change a definition already used for persistence.
        builder.OnReply<ResourceReply>(
            "late",
            "ready",
            1,
            (context, state, reply) => WorkflowTurn<WorkspaceState>.Complete(state)
        );
        json.PropertyNamingPolicy = null;
        await using var db = Context(connection);
        await using var tx = await db.Database.BeginTransactionAsync(Token);
        var frozenRunner = new EfWorkflow<WorkflowDatabase, SetupWorkspace, WorkspaceState>(
            db,
            new EfOutbox<WorkflowDatabase>(db),
            frozen
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            frozenRunner.StartAsync(new(Guid.NewGuid()), new("workspace"), Token)
        );
        Assert.Empty(db.ChangeTracker.Entries());
        var laterRunner = new EfWorkflow<WorkflowDatabase, SetupWorkspace, WorkspaceState>(
            db,
            new EfOutbox<WorkflowDatabase>(db),
            builder.Build()
        );
        await laterRunner.StartAsync(new(Guid.NewGuid()), new("workspace"), Token);
        await db.SaveChangesAsync(Token);
        await tx.CommitAsync(Token);
        var stored = await db.Set<WorkflowInstanceRecord>().SingleAsync(Token);
        Assert.Equal("workspace", stored.InitialInput.GetProperty("name").GetString());
        Assert.False(stored.InitialInput.TryGetProperty("Name", out _));
    }

    [Fact]
    public async Task StateSerializationFailureLeavesLoadedProcessAndOutgoingWorkUntouched()
    {
        var connection = await Database(fixture);
        var json = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        json.Converters.Add(new RejectChangedState());
        var definition = new WorkflowBuilder<SetupWorkspace, WorkspaceState>(
            "serialization",
            1,
            json
        )
            .StartWith(
                (context, input) =>
                    WorkflowTurn<WorkspaceState>.Request(
                        new(Guid.NewGuid(), true, "initial"),
                        Command(context, input),
                        "ready"
                    )
            )
            .OnReply<ResourceReply>(
                "ready",
                "ready",
                1,
                (context, state, reply) =>
                    WorkflowTurn<WorkspaceState>.Request(
                        state with
                        {
                            Phase = "changed",
                        },
                        Command(context, new SetupWorkspace("next")),
                        "ready"
                    )
            )
            .Build();
        var key = new WorkflowKey(Guid.NewGuid());
        WorkflowSnapshot<WorkspaceState> first;
        await using (var db = Context(connection))
        {
            await using var tx = await db.Database.BeginTransactionAsync(Token);
            first = await new EfWorkflow<WorkflowDatabase, SetupWorkspace, WorkspaceState>(
                db,
                new EfOutbox<WorkflowDatabase>(db),
                definition
            ).StartAsync(key, new("workspace"), Token);
            await db.SaveChangesAsync(Token);
            await tx.CommitAsync(Token);
        }

        await using var fresh = Context(connection);
        await using var write = await fresh.Database.BeginTransactionAsync(Token);
        var runner = new EfWorkflow<WorkflowDatabase, SetupWorkspace, WorkspaceState>(
            fresh,
            new EfOutbox<WorkflowDatabase>(fresh),
            definition
        );
        var reply = new IncomingMessage(
            Guid.NewGuid(),
            "resources",
            "ready",
            1,
            JsonSerializer.SerializeToElement(
                new ResourceReply(first.State.ResourceId, true),
                WorkspaceMethods.Json
            ),
            correlationId: key.Id.ToString("D"),
            causationId: first.PendingCommandId!.Value.ToString("D")
        );
        await Assert.ThrowsAsync<JsonException>(() => runner.HandleReplyAsync(key, reply, Token));
        await fresh.SaveChangesAsync(Token);
        await write.CommitAsync(Token);
        Assert.Equal(first, await runner.ReadAsync(key, Token));
        Assert.Equal(1, await fresh.Set<OutboxMessageRecord>().CountAsync(Token));
    }

    private static OutgoingMessage Command(WorkflowContext context, SetupWorkspace input) =>
        OutgoingMessage.FromPayload(
            context.CommandId,
            "resource",
            "resource",
            1,
            input,
            WorkspaceMethods.Json,
            correlationId: context.Key.Id.ToString("D")
        );

    private sealed class RejectChangedState : JsonConverter<WorkspaceState>
    {
        public override WorkspaceState Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        ) => JsonSerializer.Deserialize<WorkspaceState>(ref reader, WorkspaceMethods.Json)!;

        public override void Write(
            Utf8JsonWriter writer,
            WorkspaceState value,
            JsonSerializerOptions options
        )
        {
            if (value.Phase == "changed")
                throw new JsonException("Consumer serialization rejected the proposed state.");

            JsonSerializer.Serialize(writer, value, WorkspaceMethods.Json);
        }
    }
}
