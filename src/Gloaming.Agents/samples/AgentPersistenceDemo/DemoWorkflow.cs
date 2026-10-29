using System.Runtime.CompilerServices;
using Gloaming.Agents.EntityFrameworkCore;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

namespace AgentPersistenceDemo;

public sealed record DraftInput(string Text);

public sealed record ReviewRequest(string Proposal);

public sealed record ReviewResponse(bool Accept, string Feedback = "");

public static class DemoWorkflow
{
    public static Workflow Build(
        DemoContext database,
        RecordingClient client,
        string conversation = "document"
    )
    {
        var prepare = new Prepare();
        var agent = new AIAgentBinding(
            new ChatClientAgent(
                client,
                new ChatClientAgentOptions
                {
                    Id = "document-editor-v1",
                    Name = "DocumentEditor",
                    ChatHistoryProvider = new EfChatHistoryProvider<DemoContext>(
                        database,
                        database.Partition,
                        conversation,
                        "editor"
                    ),
                }
            ),
            new AIAgentHostOptions
            {
                ForwardIncomingMessages = false,
                EmitAgentUpdateEvents = false,
                EmitAgentResponseEvents = true,
            }
        );
        var proposal = new Propose();
        var review = RequestPort.Create<ReviewRequest, ReviewResponse>("document-review");
        var decide = new Decide();
        return new WorkflowBuilder(prepare)
            .WithName("document-editor-v1")
            .AddEdge(prepare, agent)
            .AddEdge(agent, proposal)
            .AddEdge(proposal, review)
            .AddEdge(review, decide)
            .AddEdge(decide, prepare)
            .WithOutputFrom(decide)
            .Build();
    }

    public static void Capture(DocumentHead head, IReadOnlyList<WorkflowEvent> events)
    {
        var request = events.OfType<RequestInfoEvent>().Single().Request;
        if (!request.TryGetDataAs<ReviewRequest>(out var data))
            throw new InvalidOperationException("Unexpected review request.");

        var checkpoint =
            events.OfType<SuperStepCompletedEvent>().Last().CompletionInfo?.Checkpoint
            ?? throw new InvalidOperationException("No checkpoint completed.");
        head.SessionId = checkpoint.SessionId;
        head.CheckpointId = checkpoint.CheckpointId;
        head.RequestId = request.RequestId;
        head.Proposal = data.Proposal;
        head.Version++;
    }

    [SendsMessage(typeof(List<ChatMessage>))]
    [SendsMessage(typeof(TurnToken))]
    private sealed class Prepare() : Executor<DraftInput>("prepare")
    {
        public override async ValueTask HandleAsync(
            DraftInput input,
            IWorkflowContext context,
            CancellationToken cancellationToken = default
        )
        {
            await context.SendMessageAsync(
                new List<ChatMessage> { new(ChatRole.User, input.Text) },
                cancellationToken: cancellationToken
            );
            await context.SendMessageAsync(
                new TurnToken(emitEvents: false),
                cancellationToken: cancellationToken
            );
        }
    }

    private sealed class Propose()
        : ChatProtocolExecutor("propose", new() { AutoSendTurnToken = false })
    {
        protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder) =>
            base.ConfigureProtocol(protocolBuilder).SendsMessage<ReviewRequest>();

        protected override async ValueTask TakeTurnAsync(
            List<ChatMessage> messages,
            IWorkflowContext context,
            bool? emitEvents,
            CancellationToken cancellationToken = default
        )
        {
            var proposal = string.Join("\n", messages.Select(x => x.Text));
            await context.QueueStateUpdateAsync(
                "proposal",
                proposal,
                "document",
                cancellationToken
            );
            await context.SendMessageAsync(
                new ReviewRequest(proposal),
                cancellationToken: cancellationToken
            );
        }
    }

    [SendsMessage(typeof(DraftInput))]
    [YieldsOutput(typeof(string))]
    private sealed class Decide() : Executor<ReviewResponse>("decide")
    {
        public override async ValueTask HandleAsync(
            ReviewResponse response,
            IWorkflowContext context,
            CancellationToken cancellationToken = default
        )
        {
            if (response.Accept)
                await context.YieldOutputAsync(
                    await context.ReadStateAsync<string>("proposal", "document", cancellationToken)
                        ?? throw new InvalidOperationException("No proposal is retained."),
                    cancellationToken
                );
            else
                await context.SendMessageAsync(
                    new DraftInput(response.Feedback),
                    cancellationToken: cancellationToken
                );
        }
    }
}

public sealed class RecordingClient : IChatClient
{
    public List<string> Inputs { get; } = [];
    public Func<CancellationToken, Task>? BeforeResponse { get; set; }

    public async Task<ChatResponse> GetResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        if (BeforeResponse is not null)
            await BeforeResponse(cancellationToken);

        cancellationToken.ThrowIfCancellationRequested();
        var values = messages.ToArray();
        Inputs.Add(string.Join("\n", values.Select(x => x.Text)));
        return new(new ChatMessage(ChatRole.Assistant, "Draft: " + values.Last().Text))
        {
            FinishReason = ChatFinishReason.Stop,
        };
    }

    public async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
        IEnumerable<ChatMessage> messages,
        ChatOptions? options = null,
        [EnumeratorCancellation] CancellationToken cancellationToken = default
    )
    {
        foreach (
            var update in (
                await GetResponseAsync(messages, options, cancellationToken)
            ).ToChatResponseUpdates()
        )
            yield return update;
    }

    public object? GetService(Type serviceType, object? serviceKey = null) => null;

    public void Dispose() { }
}
