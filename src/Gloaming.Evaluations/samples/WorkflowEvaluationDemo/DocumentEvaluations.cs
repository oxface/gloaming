using System.Runtime.CompilerServices;
using System.Text.Json;
using Gloaming.Evaluations;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;
using Microsoft.Extensions.AI.Evaluation;
using Microsoft.Extensions.AI.Evaluation.Reporting;
using Microsoft.Extensions.AI.Evaluation.Reporting.Formats.Html;
using Microsoft.Extensions.AI.Evaluation.Reporting.Formats.Json;

namespace WorkflowEvaluationDemo;

public sealed record DocumentInput(string Title);

public sealed record DocumentOutcome(string Title);

public static class DocumentEvaluations
{
    public static EvaluationSuite<DocumentInput, DocumentOutcome> Suite() =>
        new(
            "documents.normalize-title",
            "titles-v1",
            [
                new("trim", () => new("  Meeting notes  "), new("Meeting notes"), ["whitespace"]),
                new(
                    "collapse",
                    () => new("Annual   planning"),
                    new("Annual planning"),
                    ["whitespace"]
                ),
            ],
            [
                new(
                    "title-contract",
                    "v1",
                    ["ExactTitle", "TitleLength"],
                    (observation, _, _) =>
                    {
                        var valid = observation.Actual == observation.Expected;
                        return new(
                            new EvaluationResult(
                                new BooleanMetric("ExactTitle", valid)
                                {
                                    Interpretation = new(
                                        valid
                                            ? EvaluationRating.Good
                                            : EvaluationRating.Unacceptable,
                                        !valid
                                    ),
                                },
                                new NumericMetric("TitleLength", observation.Actual.Title.Length)
                                {
                                    Interpretation = new(EvaluationRating.Good, false),
                                }
                            )
                        );
                    }
                ),
            ]
        );

    public static EvaluationTarget<DocumentInput, DocumentOutcome>[] Targets() =>
        [
            new(
                "rules",
                "rules-v1",
                EvaluationExecutionKind.Deterministic,
                (input, execution, token) => RunWorkflowAsync(RulesGraph(), input, execution, token)
            ),
            new(
                "agent",
                "agent-v1",
                EvaluationExecutionKind.Fixture,
                async (input, execution, token) =>
                {
                    using var client = execution.WrapChatClient(new ScriptedClient(execution));
                    return await RunWorkflowAsync(AgentGraph(client), input, execution, token);
                },
                new Dictionary<string, string>
                {
                    ["prompt"] = "normalize-v1",
                    ["model"] = "scripted-local-v1",
                }
            ),
            new(
                "recorded",
                "recording-v1",
                EvaluationExecutionKind.Recorded,
                (input, _, _) =>
                    new(
                        new DocumentOutcome(
                            input.Title.Contains("Annual", StringComparison.Ordinal)
                                ? "Annual planning"
                                : "Meeting notes"
                        )
                    )
            ),
        ];

    public static Task<EvaluationReport<DocumentOutcome>> RunAsync(
        EvaluationRunOptions options,
        CancellationToken cancellationToken = default
    ) => EvaluationRunner.RunAsync(Suite(), Targets(), options, cancellationToken);

    // This projection is consumer-owned and uses actual recorded agent messages only.
    // Ordinary typed outcomes remain in run.json without synthetic chat messages.
    public static async Task WriteNativeReportsAsync(
        EvaluationReport<DocumentOutcome> report,
        string directory
    )
    {
        var rows = report
            .Attempts.Where(x => x.Artifacts.ContainsKey("conversation"))
            .Select(attempt =>
            {
                var messages = attempt
                    .Artifacts["conversation"]
                    .Deserialize<List<ChatMessage>>(AgentAbstractionsJsonUtilities.DefaultOptions)!;
                return new ScenarioRunResult(
                    $"{report.SuiteId}.{attempt.CaseId}.{attempt.TargetId}",
                    attempt.Repetition.ToString(System.Globalization.CultureInfo.InvariantCulture),
                    report.ExecutionId,
                    report.CompletedAt.UtcDateTime,
                    messages.Take(messages.Count - 1),
                    new ChatResponse(messages[^1]),
                    attempt.Metrics,
                    tags: [$"dataset:{report.DatasetVersion}", "execution:fixture"]
                );
            })
            .ToArray();
        if (rows.Length == 0)
            return;

        await new HtmlReportWriter(Path.Combine(directory, "agent-report.html")).WriteReportAsync(
            rows
        );
        await new JsonReportWriter(Path.Combine(directory, "agent-report.json")).WriteReportAsync(
            rows
        );
    }

    private static string Normalize(string value) =>
        string.Join(' ', value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static Workflow RulesGraph()
    {
        var rules = new NormalizeTitle();
        return new WorkflowBuilder(rules)
            .WithName("rules-normalization")
            .WithOutputFrom(rules)
            .Build();
    }

    private static Workflow AgentGraph(IChatClient client)
    {
        var prepare = new Prepare();
        var agent = new AIAgentBinding(
            new ChatClientAgent(
                client,
                "Normalize whitespace in the document title. Return only the title."
            ),
            new AIAgentHostOptions
            {
                ForwardIncomingMessages = false,
                EmitAgentUpdateEvents = false,
                EmitAgentResponseEvents = true,
            }
        );
        var finish = new Finish();
        return new WorkflowBuilder(prepare)
            .WithName("agent-normalization")
            .AddEdge(prepare, agent)
            .AddEdge(agent, finish)
            .WithOutputFrom(finish)
            .Build();
    }

    private static async ValueTask<DocumentOutcome> RunWorkflowAsync(
        Workflow graph,
        DocumentInput input,
        EvaluationExecution execution,
        CancellationToken token
    )
    {
        await using var run = await InProcessExecution.RunStreamingAsync(
            graph,
            input,
            cancellationToken: token
        );
        var events = new List<WorkflowEvent>();
        await foreach (var item in run.WatchStreamAsync(cancellationToken: token))
        {
            if (item is WorkflowErrorEvent or ExecutorFailedEvent)
                throw new InvalidOperationException("The sample workflow failed.");

            events.Add(item);
        }

        execution.RecordArtifact(
            "workflow-events",
            JsonSerializer.SerializeToElement(events.Select(x => x.GetType().Name))
        );
        return events
            .OfType<WorkflowOutputEvent>()
            .Select(x => x.Data)
            .OfType<DocumentOutcome>()
            .Single();
    }

    [YieldsOutput(typeof(DocumentOutcome))]
    private sealed class NormalizeTitle() : Executor<DocumentInput>("rules")
    {
        public override ValueTask HandleAsync(
            DocumentInput input,
            IWorkflowContext context,
            CancellationToken cancellationToken = default
        ) =>
            context.YieldOutputAsync(
                new DocumentOutcome(Normalize(input.Title)),
                cancellationToken
            );
    }

    [SendsMessage(typeof(List<ChatMessage>))]
    [SendsMessage(typeof(TurnToken))]
    private sealed class Prepare() : Executor<DocumentInput>("prepare")
    {
        public override async ValueTask HandleAsync(
            DocumentInput input,
            IWorkflowContext context,
            CancellationToken cancellationToken = default
        )
        {
            await context.SendMessageAsync(
                new List<ChatMessage> { new(ChatRole.User, input.Title) },
                cancellationToken: cancellationToken
            );
            await context.SendMessageAsync(
                new TurnToken(emitEvents: false),
                cancellationToken: cancellationToken
            );
        }
    }

    private sealed class Finish()
        : ChatProtocolExecutor("finish", new() { AutoSendTurnToken = false })
    {
        protected override ProtocolBuilder ConfigureProtocol(ProtocolBuilder protocolBuilder) =>
            base.ConfigureProtocol(protocolBuilder).YieldsOutput<DocumentOutcome>();

        protected override ValueTask TakeTurnAsync(
            List<ChatMessage> messages,
            IWorkflowContext context,
            bool? emitEvents,
            CancellationToken cancellationToken = default
        ) =>
            context.YieldOutputAsync(new DocumentOutcome(messages.Last().Text!), cancellationToken);
    }

    private sealed class ScriptedClient(EvaluationExecution execution) : IChatClient
    {
        public Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            cancellationToken.ThrowIfCancellationRequested();

            var conversation = messages.ToList();
            var response = new ChatMessage(
                ChatRole.Assistant,
                Normalize(conversation.Last().Text!)
            );
            execution.RecordArtifact(
                "conversation",
                JsonSerializer.SerializeToElement(
                    conversation.Append(response).ToArray(),
                    AgentAbstractionsJsonUtilities.DefaultOptions
                )
            );
            return Task.FromResult(
                new ChatResponse(response) { FinishReason = ChatFinishReason.Stop }
            );
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
}
