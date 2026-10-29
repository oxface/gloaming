using System.Text.Json;
using AgentPersistenceDemo;
using Gloaming.Agents.EntityFrameworkCore;
using Gloaming.Agents.Workflows;
using Gloaming.Agents.Workflows.EntityFrameworkCore;
using Gloaming.Conversations.EntityFrameworkCore;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Npgsql;
using DemoProgram = AgentPersistenceDemo.AdoptionJourney;

namespace AgentPersistenceTests;

public sealed partial class PersistenceTests
{
    [Fact]
    public void Packages_SelectedCapabilities_KeepDependenciesIndependent()
    {
        var transcript = typeof(EfConversationTranscript<>)
            .Assembly.GetReferencedAssemblies()
            .Select(x => x.Name!);
        Assert.DoesNotContain(
            transcript,
            x =>
                x.StartsWith("Microsoft.Agents", StringComparison.Ordinal)
                || x.StartsWith("Npgsql", StringComparison.Ordinal)
        );
        Assert.DoesNotContain(
            typeof(EfChatHistoryProvider<>).Assembly.GetReferencedAssemblies(),
            x => x.Name!.Contains("Workflows", StringComparison.Ordinal)
        );
        Assert.DoesNotContain(
            typeof(NativeWorkflowExecution).Assembly.GetReferencedAssemblies(),
            x => x.Name!.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
        );
    }

    [Fact]
    public async Task Writers_MissingTransaction_RejectBeforeStoringRecords()
    {
        await using var database = DemoContext.Open(await DatabaseAsync());
        var payload = JsonSerializer.SerializeToElement("value");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            DemoProgram
                .Transcript(database)
                .AppendAsync(Guid.NewGuid(), "message", 1, payload, DateTimeOffset.UtcNow, Token)
        );
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await new EfCheckpointStore<DemoContext>(
                database,
                "demo",
                "document",
                Token
            ).CreateCheckpointAsync("session", payload)
        );
        var agent = new ChatClientAgent(new RecordingClient());
        var session = await agent.CreateSessionAsync(Token);
#pragma warning disable MAAI001 // Native successful-invocation hook verifies the storage transaction requirement.
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await new EfChatHistoryProvider<DemoContext>(
                database,
                "demo",
                "document",
                "editor"
            ).InvokedAsync(
                new(
                    agent,
                    session,
                    [new(ChatRole.User, "request")],
                    [new(ChatRole.Assistant, "response")]
                ),
                Token
            )
        );
#pragma warning restore MAAI001
    }

    [Fact]
    public async Task Guards_TrackedMutationAndForeignPartition_RejectBothSavePaths()
    {
        var connection = await DatabaseAsync();
        await DemoProgram.RunJourneyAsync(connection, Token);
        await using var database = DemoContext.Open(connection);
        foreach (
            var entity in new object[]
            {
                await database.Set<AgentCheckpoint>().FirstAsync(Token),
                await database.Set<AgentHistorySnapshot>().FirstAsync(Token),
                await database.Set<ConversationEntry>().FirstAsync(Token),
            }
        )
        {
            database.Entry(entity).State = EntityState.Modified;
            Assert.Throws<InvalidOperationException>(() => database.SaveChanges(false));
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                database.SaveChangesAsync(false, Token)
            );
            database.Entry(entity).State = EntityState.Deleted;
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                database.SaveChangesAsync(Token)
            );
            database.Entry(entity).State = EntityState.Unchanged;
        }

        foreach (
            var entity in new object[]
            {
                new AgentCheckpoint
                {
                    PartitionKey = "foreign",
                    ConversationKey = "document",
                    CheckpointId = "bad",
                    SessionId = "session",
                    Sequence = 1,
                    Payload = "{}",
                },
                new AgentHistorySnapshot
                {
                    PartitionKey = "foreign",
                    ConversationKey = "document",
                    AgentKey = "editor",
                    SessionKey = "session",
                    Id = "bad",
                    Messages = "[]",
                },
                new ConversationEntry
                {
                    PartitionKey = "foreign",
                    ConversationKey = "document",
                    Id = Guid.NewGuid(),
                    Sequence = 1,
                    Type = "message",
                    SchemaVersion = 1,
                    Payload = "{}",
                },
            }
        )
        {
            database.Add(entity);
            Assert.Throws<InvalidOperationException>(() => database.SaveChanges());
            await Assert.ThrowsAsync<InvalidOperationException>(() =>
                database.SaveChangesAsync(Token)
            );
            database.Entry(entity).State = EntityState.Detached;
        }
    }

    [Fact]
    public async Task RuntimeRole_AppendReadAllowed_UpdateDeleteAndSchemaAlterDenied()
    {
        var connection = await DatabaseAsync();
        string role = "agent_" + Guid.NewGuid().ToString("N");
        string password = Guid.NewGuid().ToString("N");
        await using var setup = new NpgsqlConnection(connection);
        await setup.OpenAsync(Token);
        await using (
            var command = new NpgsqlCommand(
                $"CREATE ROLE {role} LOGIN PASSWORD '{password}'; GRANT USAGE ON SCHEMA documents TO {role}; GRANT SELECT, INSERT ON documents.agent_checkpoints, documents.agent_history, documents.conversation_entries TO {role}",
                setup
            )
        )
            await command.ExecuteNonQueryAsync(Token);

        try
        {
            var runtime = new NpgsqlConnectionStringBuilder(connection)
            {
                Username = role,
                Password = password,
                Pooling = false,
            }.ConnectionString;
            await using var database = DemoContext.Open(runtime);
            await using (var transaction = await database.Database.BeginTransactionAsync(Token))
            {
                await new EfCheckpointStore<DemoContext>(
                    database,
                    "demo",
                    "document",
                    Token
                ).CreateCheckpointAsync("session", JsonSerializer.SerializeToElement("value"));
                await DemoProgram
                    .Transcript(database)
                    .AppendAsync(
                        Guid.NewGuid(),
                        "message",
                        1,
                        JsonSerializer.SerializeToElement("visible"),
                        DateTimeOffset.UtcNow,
                        Token
                    );
                var agent = Agent(database, new RecordingClient());
                await agent.RunAsync(
                    "working",
                    await agent.CreateSessionAsync(Token),
                    cancellationToken: Token
                );
                await transaction.CommitAsync(Token);
            }

            Assert.Single(
                await DemoProgram.Transcript(database).ReadAsync(cancellationToken: Token)
            );
            await using var native = new NpgsqlConnection(runtime);
            await native.OpenAsync(Token);
            foreach (
                string table in new[]
                {
                    "agent_checkpoints",
                    "agent_history",
                    "conversation_entries",
                }
            )
            {
                foreach (
                    string sql in new[]
                    {
                        $"DELETE FROM documents.{table}",
                        $"UPDATE documents.{table} SET \"PartitionKey\" = 'rewritten'",
                        $"ALTER TABLE documents.{table} ADD COLUMN forbidden int",
                    }
                )
                {
                    await using var command = new NpgsqlCommand(sql, native);
                    var error = await Assert.ThrowsAsync<PostgresException>(async () =>
                        await command.ExecuteNonQueryAsync(Token)
                    );
                    Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, error.SqlState);
                }
            }
        }
        finally
        {
            await using var cleanup = new NpgsqlCommand(
                $"DROP OWNED BY {role}; DROP ROLE {role}",
                setup
            );
            await cleanup.ExecuteNonQueryAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task Lifecycle_MultiplePendingRequests_AnswersOnlySelectedIdAndType()
    {
        var connection = await DatabaseAsync();
        string rightId;
        CheckpointInfo checkpoint;
        await using (var database = DemoContext.Open(connection))
        {
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            var events = await NativeWorkflowExecution.StartAsync(
                MultiRequestGraph(),
                new DraftInput("typed-input"),
                DemoProgram.Manager(database, Token),
                cancellationToken: Token
            );
            Assert.Equal(2, events.OfType<RequestInfoEvent>().Count());
            var left = events
                .OfType<RequestInfoEvent>()
                .Single(x => x.Request.IsDataOfType<LeftRequest>())
                .Request;
            rightId = events
                .OfType<RequestInfoEvent>()
                .Single(x => x.Request.IsDataOfType<RightRequest>())
                .Request.RequestId;
            checkpoint = events
                .OfType<SuperStepCompletedEvent>()
                .Last()
                .CompletionInfo!.Checkpoint!;
            var resumed = await NativeWorkflowExecution.ResumeAsync<LeftRequest, ReviewResponse>(
                MultiRequestGraph(),
                checkpoint,
                DemoProgram.Manager(database, Token),
                left.RequestId,
                new(true, "left-only"),
                cancellationToken: Token
            );
            Assert.Contains(
                resumed.OfType<WorkflowOutputEvent>(),
                x => Equals(x.Data, "left:left-only")
            );
            Assert.DoesNotContain(
                resumed.OfType<WorkflowOutputEvent>(),
                x => x.Data?.ToString()?.StartsWith("right:", StringComparison.Ordinal) == true
            );
            checkpoint = resumed
                .OfType<SuperStepCompletedEvent>()
                .Last()
                .CompletionInfo!.Checkpoint!;
            await transaction.CommitAsync(Token);
        }

        await using var fresh = DemoContext.Open(connection);
        await using var next = await fresh.Database.BeginTransactionAsync(Token);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            NativeWorkflowExecution.ResumeAsync<LeftRequest, ReviewResponse>(
                MultiRequestGraph(),
                checkpoint,
                DemoProgram.Manager(fresh, Token),
                rightId,
                new(true),
                cancellationToken: Token
            )
        );
        var observed = new List<WorkflowEvent>();
        var result = await NativeWorkflowExecution.ResumeAsync<RightRequest, ReviewResponse>(
            MultiRequestGraph(),
            checkpoint,
            DemoProgram.Manager(fresh, Token),
            rightId,
            new(true, "right-only"),
            observed.Add,
            Token
        );
        Assert.Contains(observed.OfType<RequestInfoEvent>(), x => x.Request.RequestId == rightId);
        Assert.Contains(
            result.OfType<WorkflowOutputEvent>(),
            x => Equals(x.Data, "right:right-only")
        );
        await next.CommitAsync(Token);
    }

    [Fact]
    public async Task Lifecycle_ProviderFailureAndCancellation_ExposeFailureAndLeaveNoCommittedTurn()
    {
        var connection = await DatabaseAsync();
        await using (var database = DemoContext.Open(connection))
        {
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            var client = new RecordingClient
            {
                BeforeResponse = _ =>
                    throw new InvalidOperationException("deterministic-provider-failure"),
            };
            var observed = new List<WorkflowEvent>();
            await Assert.ThrowsAnyAsync<Exception>(() =>
                NativeWorkflowExecution.StartAsync(
                    DemoWorkflow.Build(database, client),
                    new DraftInput("failure"),
                    DemoProgram.Manager(database, Token),
                    observed.Add,
                    Token
                )
            );
            Assert.Contains(observed, x => x is WorkflowErrorEvent or ExecutorFailedEvent);
            await transaction.RollbackAsync(Token);
        }

        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using (var database = DemoContext.Open(connection))
        {
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            var client = new RecordingClient
            {
                BeforeResponse = async token =>
                {
                    entered.TrySetResult();
                    await Task.Delay(Timeout.Infinite, token);
                },
            };
            var run = NativeWorkflowExecution.StartAsync(
                DemoWorkflow.Build(database, client),
                new DraftInput("cancel"),
                DemoProgram.Manager(database, cancellation.Token),
                cancellationToken: cancellation.Token
            );
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(10), Token);
            await cancellation.CancelAsync();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
                await run.WaitAsync(TimeSpan.FromSeconds(10), Token)
            );
            await transaction.RollbackAsync(Token);
        }

        await using var fresh = DemoContext.Open(connection);
        Assert.Empty(await fresh.Set<AgentHistorySnapshot>().ToArrayAsync(Token));
        Assert.Empty(await fresh.Set<AgentCheckpoint>().ToArrayAsync(Token));
    }

    private sealed record LeftRequest(string Text);

    private sealed record RightRequest(string Text);

    private static Workflow MultiRequestGraph()
    {
        var start = new Fork();
        var left = RequestPort.Create<LeftRequest, ReviewResponse>("left-port");
        var right = RequestPort.Create<RightRequest, ReviewResponse>("right-port");
        var leftOutput = new Answer("left");
        var rightOutput = new Answer("right");
        return new WorkflowBuilder(start)
            .WithName("two-pending-ports")
            .AddEdge(start, left)
            .AddEdge(start, right)
            .AddEdge(left, leftOutput)
            .AddEdge(right, rightOutput)
            .WithOutputFrom(leftOutput, rightOutput)
            .Build();
    }

    [SendsMessage(typeof(LeftRequest))]
    [SendsMessage(typeof(RightRequest))]
    private sealed class Fork() : Executor<DraftInput>("fork")
    {
        public override async ValueTask HandleAsync(
            DraftInput input,
            IWorkflowContext context,
            CancellationToken cancellationToken = default
        )
        {
            await context.SendMessageAsync(
                new LeftRequest(input.Text),
                cancellationToken: cancellationToken
            );
            await context.SendMessageAsync(
                new RightRequest(input.Text),
                cancellationToken: cancellationToken
            );
        }
    }

    [YieldsOutput(typeof(string))]
    private sealed class Answer : Executor<ReviewResponse>
    {
        private readonly string label;

        public Answer(string label)
            : base(label) => this.label = label;

        public override ValueTask HandleAsync(
            ReviewResponse response,
            IWorkflowContext context,
            CancellationToken cancellationToken = default
        ) => context.YieldOutputAsync(label + ":" + response.Feedback, cancellationToken);
    }
}
