using System.Text.Json;
using AgentPersistenceDemo;
using Gloaming.Agents.EntityFrameworkCore;
using Gloaming.Agents.Workflows;
using Gloaming.Agents.Workflows.EntityFrameworkCore;
using Gloaming.Conversations.EntityFrameworkCore;
using Gloaming.Tests.Infrastructure;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.AI;
using Npgsql;
using DemoProgram = AgentPersistenceDemo.AdoptionJourney;

namespace AgentPersistenceTests;

public sealed partial class PersistenceTests(PostgreSqlFixture postgres)
    : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private async Task<string> DatabaseAsync()
    {
        var connection = await postgres.CreateDatabaseAsync(Token);
        await using var database = DemoContext.Open(connection);
        await database.Database.EnsureCreatedAsync(Token);
        return connection;
    }

    [Fact]
    public async Task Workflow_ReconstructRefineAccept_PreservesNativeHistoryAndAvoidsProviderCallOnAccept()
    {
        await DemoProgram.RunJourneyAsync(await DatabaseAsync(), Token);
    }

    [Fact]
    public async Task Workflow_EarlierCheckpointBranch_ExcludesLaterNativeContext()
    {
        var connection = await DatabaseAsync();
        var client = new RecordingClient();
        CheckpointInfo earlier;
        string earlierRequest;
        await using (var database = DemoContext.Open(connection))
        {
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            var head = new DocumentHead();
            database.Add(head);
            var events = await NativeWorkflowExecution.StartAsync(
                DemoWorkflow.Build(database, client),
                new DraftInput("original-workflow-marker"),
                DemoProgram.Manager(database, Token),
                cancellationToken: Token
            );
            DemoWorkflow.Capture(head, events);
            earlier = new(head.SessionId, head.CheckpointId);
            earlierRequest = head.RequestId;
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }

        await using (var database = DemoContext.Open(connection))
        {
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            var head = await database.Set<DocumentHead>().SingleAsync(Token);
            var events = await NativeWorkflowExecution.ResumeAsync<ReviewRequest, ReviewResponse>(
                DemoWorkflow.Build(database, client),
                earlier,
                DemoProgram.Manager(database, Token),
                earlierRequest,
                new(false, "later-workflow-marker"),
                cancellationToken: Token
            );
            DemoWorkflow.Capture(head, events);
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }

        await using (var database = DemoContext.Open(connection))
        {
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            var head = await database.Set<DocumentHead>().SingleAsync(Token);
            var events = await NativeWorkflowExecution.ResumeAsync<ReviewRequest, ReviewResponse>(
                DemoWorkflow.Build(database, client),
                earlier,
                DemoProgram.Manager(database, Token),
                earlierRequest,
                new(false, "independent-branch"),
                cancellationToken: Token
            );
            DemoWorkflow.Capture(head, events);
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
            Assert.Contains(
                "original-workflow-marker",
                client.Inputs[^1],
                StringComparison.Ordinal
            );
            Assert.DoesNotContain(
                "later-workflow-marker",
                client.Inputs[^1],
                StringComparison.Ordinal
            );
            Assert.Equal(3, await database.Set<AgentHistorySnapshot>().CountAsync(Token));
        }
    }

    [Fact]
    public async Task History_OlderSessionAndBranch_ExcludeLaterContextAndForeignScopes()
    {
        var connection = await DatabaseAsync();
        var client = new RecordingClient();
        JsonElement earlier;
        string sessionKey;
        await using (var database = DemoContext.Open(connection))
        {
            var agent = Agent(database, client);
            var session = await agent.CreateSessionAsync(Token);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            await agent.RunAsync("original-marker", session, cancellationToken: Token);
            earlier = await agent.SerializeSessionAsync(session, cancellationToken: Token);
            Assert.DoesNotContain(
                "original-marker",
                earlier.GetRawText(),
                StringComparison.Ordinal
            );
            sessionKey = (
                await database.Set<AgentHistorySnapshot>().AsNoTracking().SingleAsync(Token)
            ).SessionKey;
            await agent.RunAsync("later-marker", session, cancellationToken: Token);
            await transaction.CommitAsync(Token);
        }

        await using (var database = DemoContext.Open(connection))
        {
            var agent = Agent(database, client);
            var session = await agent.DeserializeSessionAsync(earlier, cancellationToken: Token);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            await agent.RunAsync("branch-marker", session, cancellationToken: Token);
            await transaction.CommitAsync(Token);
            Assert.Contains("original-marker", client.Inputs[^1], StringComparison.Ordinal);
            Assert.DoesNotContain("later-marker", client.Inputs[^1], StringComparison.Ordinal);
            Assert.Equal(3, await database.Set<AgentHistorySnapshot>().CountAsync(Token));
        }

        foreach (var scope in new[] { ("foreign", "document"), ("demo", "foreign") })
        {
            await using var database = DemoContext.Open(connection, scope.Item1);
            var agent = Agent(database, client, scope.Item2);
            var session = await agent.DeserializeSessionAsync(earlier, cancellationToken: Token);
            await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                agent.RunAsync("denied", session, cancellationToken: Token)
            );
        }

        await using (var database = DemoContext.Open(connection))
        {
            var wrongSession = JsonDocument.Parse(
                earlier.GetRawText().Replace(sessionKey, "wrong-session", StringComparison.Ordinal)
            );
            var agent = Agent(database, client);
            var session = await agent.DeserializeSessionAsync(
                wrongSession.RootElement,
                cancellationToken: Token
            );
            await Assert.ThrowsAsync<KeyNotFoundException>(() =>
                agent.RunAsync("denied", session, cancellationToken: Token)
            );
            wrongSession.Dispose();
        }

        await using (var database = DemoContext.Open(connection))
        {
            var agent = Agent(database, client, agentKey: "other-agent");
            var session = await agent.DeserializeSessionAsync(earlier, cancellationToken: Token);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            await agent.RunAsync("new-agent", session, cancellationToken: Token);
            Assert.DoesNotContain("original-marker", client.Inputs[^1], StringComparison.Ordinal);
            await transaction.RollbackAsync(Token);
        }
    }

    [Fact]
    public async Task History_NativeToolsAndReducer_PreserveProtocolWithoutReducingVisibleEntries()
    {
        var connection = await DatabaseAsync();
        await using var database = DemoContext.Open(connection);
        var client = new RecordingClient();
        var provider = new EfChatHistoryProvider<DemoContext>(database, "demo", "tools", "editor");
        var agent = new ChatClientAgent(client);
        var session = await agent.CreateSessionAsync(Token);
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
#pragma warning disable MAAI001 // Deliberately exercise the native typed-message persistence boundary.
        await provider.InvokedAsync(
            new(
                agent,
                session,
                [new(ChatRole.User, "Find it")],
                [
                    new(
                        ChatRole.Assistant,
                        [
                            new FunctionCallContent(
                                "call-1",
                                "lookup",
                                new Dictionary<string, object?> { ["key"] = "item" }
                            ),
                        ]
                    ),
                    new(ChatRole.Tool, [new FunctionResultContent("call-1", "found")]),
                    new(ChatRole.Assistant, "Found it"),
                ]
            ),
            Token
        );
        var messages = (await provider.InvokingAsync(new(agent, session, []), Token)).ToArray();
#pragma warning restore MAAI001
        Assert.Equal(4, messages.Length);
        Assert.Equal(
            "lookup",
            Assert.IsType<FunctionCallContent>(messages[1].Contents.Single()).Name
        );
        Assert.Equal(
            "call-1",
            Assert.IsType<FunctionResultContent>(messages[2].Contents.Single()).CallId
        );
        Assert.Equal("Found it", messages[3].Text);

        var transcript = new EfConversationTranscript<DemoContext>(database, "demo", "reduction");
        var entry = await transcript.AppendAsync(
            Guid.NewGuid(),
            "proposal",
            7,
            JsonSerializer.SerializeToElement(new { text = "retained artifact" }),
            DateTimeOffset.UtcNow,
            Token
        );
        var reducedAgent = Agent(database, client, "reduction", reducer: new LastTurnReducer());
        var reducedSession = await reducedAgent.CreateSessionAsync(Token);
        await reducedAgent.RunAsync("old-context", reducedSession, cancellationToken: Token);
        await reducedAgent.RunAsync("new-context", reducedSession, cancellationToken: Token);
        await reducedAgent.RunAsync("next-context", reducedSession, cancellationToken: Token);
        Assert.DoesNotContain("old-context", client.Inputs[^1], StringComparison.Ordinal);
        Assert.Contains("new-context", client.Inputs[^1], StringComparison.Ordinal);
        await transaction.CommitAsync(Token);

        await using var fresh = DemoContext.Open(connection);
        var visible = Assert.Single(
            await new EfConversationTranscript<DemoContext>(fresh, "demo", "reduction").ReadAsync(
                cancellationToken: Token
            )
        );
        Assert.Equal(entry.Id, visible.Id);
        Assert.Equal(7, visible.SchemaVersion);
        Assert.Contains("retained artifact", visible.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Checkpoints_IndexParentAndOpaquePayload_PreserveOrderAndIsolation()
    {
        var connection = await DatabaseAsync();
        await using var database = DemoContext.Open(connection);
        var store = new EfCheckpointStore<DemoContext>(database, "demo", "document", Token);
        using var value = JsonDocument.Parse(
            "{\"z\":1,\"nested\":{\"$type\":\"opaque\",\"a\":2},\"a\":3}"
        );
        await using var transaction = await database.Database.BeginTransactionAsync(Token);
        var first = await store.CreateCheckpointAsync("session", value.RootElement);
        var second = await store.CreateCheckpointAsync("session", value.RootElement, first);
        var third = await store.CreateCheckpointAsync("session", value.RootElement, first);
        await transaction.CommitAsync(Token);
        await using var fresh = DemoContext.Open(connection);
        var reader = new EfCheckpointStore<DemoContext>(fresh, "demo", "document", Token);
        Assert.Equal(
            new[] { first.CheckpointId, second.CheckpointId, third.CheckpointId },
            (await reader.RetrieveIndexAsync("session")).Select(x => x.CheckpointId)
        );
        Assert.Equal(
            new[] { second.CheckpointId, third.CheckpointId },
            (await reader.RetrieveIndexAsync("session", first)).Select(x => x.CheckpointId)
        );
        Assert.Equal(
            value.RootElement.GetRawText(),
            (await reader.RetrieveCheckpointAsync("session", first)).GetRawText()
        );
        await Assert.ThrowsAsync<KeyNotFoundException>(async () =>
            await reader.RetrieveCheckpointAsync("other-session", first)
        );
        foreach (var scope in new[] { ("other", "document"), ("demo", "other") })
        {
            var foreign = new EfCheckpointStore<DemoContext>(
                fresh,
                scope.Item1,
                scope.Item2,
                Token
            );
            Assert.Empty(await foreign.RetrieveIndexAsync("session"));
            await Assert.ThrowsAsync<KeyNotFoundException>(async () =>
                await foreign.RetrieveCheckpointAsync("session", first)
            );
        }
    }

    [Fact]
    public async Task Transaction_Rollback_RemovesNativeContextCheckpointTranscriptAndBusinessHead()
    {
        var connection = await DatabaseAsync();
        await using (var database = DemoContext.Open(connection))
        {
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            var head = new DocumentHead();
            database.Add(head);
            var events = await NativeWorkflowExecution.StartAsync(
                DemoWorkflow.Build(database, new RecordingClient()),
                new DraftInput("uncommitted"),
                DemoProgram.Manager(database, Token),
                cancellationToken: Token
            );
            DemoWorkflow.Capture(head, events);
            await DemoProgram
                .Transcript(database)
                .AppendAsync(
                    Guid.NewGuid(),
                    "proposal",
                    3,
                    JsonSerializer.SerializeToElement(head.Proposal),
                    DateTimeOffset.UtcNow,
                    Token
                );
            await transaction.RollbackAsync(Token);
        }

        await using var fresh = DemoContext.Open(connection);
        Assert.Empty(await fresh.Set<AgentHistorySnapshot>().ToArrayAsync(Token));
        Assert.Empty(await fresh.Set<AgentCheckpoint>().ToArrayAsync(Token));
        Assert.Empty(await DemoProgram.Transcript(fresh).ReadAsync(cancellationToken: Token));
        Assert.Empty(await fresh.Set<DocumentHead>().ToArrayAsync(Token));
        await DemoProgram.RunJourneyAsync(connection, Token);
    }

    [Fact]
    public async Task Transcript_PagingVersionsAndDuplicateIdentity_KeepCommittedEntriesUnchanged()
    {
        var connection = await DatabaseAsync();
        Guid id = Guid.NewGuid();
        await using (var database = DemoContext.Open(connection))
        {
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            var store = DemoProgram.Transcript(database);
            await store.AppendAsync(
                id,
                "proposal",
                2,
                JsonSerializer.SerializeToElement(new { value = "first" }),
                DateTimeOffset.UtcNow,
                Token
            );
            await store.AppendAsync(
                Guid.NewGuid(),
                "proposal",
                3,
                JsonSerializer.SerializeToElement(new { value = "second" }),
                DateTimeOffset.UtcNow,
                Token
            );
            await transaction.CommitAsync(Token);
        }

        await using (var database = DemoContext.Open(connection))
        {
            var store = DemoProgram.Transcript(database);
            var first = Assert.Single(await store.ReadAsync(maxCount: 1, cancellationToken: Token));
            var second = Assert.Single(await store.ReadAsync(first.Sequence, 1, Token));
            Assert.Equal(2, first.SchemaVersion);
            Assert.Equal(3, second.SchemaVersion);
            Assert.Empty(
                await new EfConversationTranscript<DemoContext>(
                    database,
                    "foreign",
                    "document"
                ).ReadAsync(cancellationToken: Token)
            );
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                store.AppendAsync(
                    id,
                    "proposal",
                    2,
                    JsonSerializer.SerializeToElement("duplicate"),
                    DateTimeOffset.UtcNow,
                    Token
                )
            );
            await transaction.RollbackAsync(Token);
        }

        await using var fresh = DemoContext.Open(connection);
        Assert.Equal(
            2,
            (await DemoProgram.Transcript(fresh).ReadAsync(cancellationToken: Token)).Count
        );
    }

    [Fact]
    public async Task ConsumerLock_CompetingTransactions_SerializeCheckpointAndTranscriptSequences()
    {
        var connection = await DatabaseAsync();
        await using var first = DemoContext.Open(connection);
        await using var second = DemoContext.Open(connection);
        await using var firstTransaction = await first.Database.BeginTransactionAsync(Token);
        await DemoProgram.LockAsync(first, Token);
        await using var secondTransaction = await second.Database.BeginTransactionAsync(Token);
        var secondNative = (NpgsqlConnection)second.Database.GetDbConnection();
        await using var attempt = new NpgsqlCommand(
            "SELECT pg_try_advisory_xact_lock(48201029)",
            secondNative
        );
        Assert.Equal(false, await attempt.ExecuteScalarAsync(Token));
        var one = await new EfCheckpointStore<DemoContext>(
            first,
            "demo",
            "document",
            Token
        ).CreateCheckpointAsync("session", JsonSerializer.SerializeToElement("first"));
        await DemoProgram
            .Transcript(first)
            .AppendAsync(
                Guid.NewGuid(),
                "message",
                1,
                JsonSerializer.SerializeToElement("first"),
                DateTimeOffset.UtcNow,
                Token
            );
        await firstTransaction.CommitAsync(Token);
        Assert.Equal(true, await attempt.ExecuteScalarAsync(Token));
        var two = await new EfCheckpointStore<DemoContext>(
            second,
            "demo",
            "document",
            Token
        ).CreateCheckpointAsync("session", JsonSerializer.SerializeToElement("second"), one);
        await DemoProgram
            .Transcript(second)
            .AppendAsync(
                Guid.NewGuid(),
                "message",
                1,
                JsonSerializer.SerializeToElement("second"),
                DateTimeOffset.UtcNow,
                Token
            );
        await secondTransaction.CommitAsync(Token);
        await using var fresh = DemoContext.Open(connection);
        Assert.Equal(
            new[] { one.CheckpointId, two.CheckpointId },
            (
                await new EfCheckpointStore<DemoContext>(
                    fresh,
                    "demo",
                    "document",
                    Token
                ).RetrieveIndexAsync("session")
            ).Select(x => x.CheckpointId)
        );
        Assert.Equal(
            new long[] { 1, 2 },
            (await DemoProgram.Transcript(fresh).ReadAsync(cancellationToken: Token)).Select(x =>
                x.Sequence
            )
        );
    }

    private static ChatClientAgent Agent(
        DemoContext database,
        RecordingClient client,
        string conversation = "document",
        string agentKey = "editor",
        IChatReducer? reducer = null
    ) =>
        new(
            client,
            new ChatClientAgentOptions
            {
                Id = "independent-agent",
                ChatHistoryProvider = new EfChatHistoryProvider<DemoContext>(
                    database,
                    database.Partition,
                    conversation,
                    agentKey,
                    reducer
                ),
            }
        );

    private sealed class LastTurnReducer : IChatReducer
    {
        public Task<IEnumerable<ChatMessage>> ReduceAsync(
            IEnumerable<ChatMessage> messages,
            CancellationToken cancellationToken = default
        ) => Task.FromResult(messages.TakeLast(2));
    }
}
