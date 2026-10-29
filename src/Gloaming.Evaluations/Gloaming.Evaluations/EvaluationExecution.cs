using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Extensions.AI;

namespace Gloaming.Evaluations;

/// <summary>One attempt's evidence and access to the run's shared actual-call budget.</summary>
public sealed class EvaluationExecution
{
    private readonly EvaluationCallBudget budget;
    private readonly CancellationToken cancellationToken;
    private readonly ConcurrentDictionary<string, JsonElement> artifacts = new(
        StringComparer.Ordinal
    );
    private int modelCalls;
    private int limitReached;

    internal EvaluationExecution(EvaluationCallBudget budget, CancellationToken cancellationToken)
    {
        this.budget = budget;
        this.cancellationToken = cancellationToken;
    }

    /// <summary>Physical calls admitted for this attempt, including its judges.</summary>
    public int ModelCalls => Volatile.Read(ref modelCalls);

    internal bool LimitReached => Volatile.Read(ref limitReached) != 0;
    internal IReadOnlyDictionary<string, JsonElement> Artifacts =>
        new Dictionary<string, JsonElement>(artifacts);

    /// <summary>Records caller-selected JSON evidence; it is cloned and duplicate names are rejected.</summary>
    public void RecordArtifact(string name, JsonElement value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (value.ValueKind == JsonValueKind.Undefined)
            throw new ArgumentException("Defined JSON evidence is required.", nameof(value));

        if (!artifacts.TryAdd(name, value.Clone()))
            throw new ArgumentException(
                "Artifact names must be unique within an attempt.",
                nameof(name)
            );
    }

    /// <summary>Wrap the physical chat transport, below tool loops/retries. Disposing the wrapper disposes its client.</summary>
    public IChatClient WrapChatClient(IChatClient client)
    {
        ArgumentNullException.ThrowIfNull(client);

        return new BudgetClient(client, this);
    }

    private void Spend()
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!budget.TrySpend())
        {
            Interlocked.Exchange(ref limitReached, 1);
            throw new EvaluationCallLimitException();
        }

        Interlocked.Increment(ref modelCalls);
    }

    private sealed class BudgetClient(IChatClient inner, EvaluationExecution execution)
        : DelegatingChatClient(inner)
    {
        public override async Task<ChatResponse> GetResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            CancellationToken cancellationToken = default
        )
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                execution.cancellationToken
            );
            linked.Token.ThrowIfCancellationRequested();
            execution.Spend();
            return await base.GetResponseAsync(messages, options, linked.Token);
        }

        public override async IAsyncEnumerable<ChatResponseUpdate> GetStreamingResponseAsync(
            IEnumerable<ChatMessage> messages,
            ChatOptions? options = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default
        )
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                execution.cancellationToken
            );
            linked.Token.ThrowIfCancellationRequested();
            execution.Spend();
            await foreach (
                var update in base.GetStreamingResponseAsync(messages, options, linked.Token)
            )
                yield return update;
        }
    }
}

/// <summary>The runner rejected a wrapped physical call before invoking its transport.</summary>
public sealed class EvaluationCallLimitException()
    : InvalidOperationException("The evaluation model-call budget is exhausted.");

internal sealed class EvaluationCallBudget(int maximum)
{
    private int calls;
    public int Calls => Volatile.Read(ref calls);

    public bool TrySpend()
    {
        while (true)
        {
            var current = Volatile.Read(ref calls);
            if (current >= maximum)
                return false;

            if (Interlocked.CompareExchange(ref calls, current + 1, current) == current)
                return true;
        }
    }
}
