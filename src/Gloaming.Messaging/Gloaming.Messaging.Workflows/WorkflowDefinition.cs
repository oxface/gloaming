using System.Text.Json;

namespace Gloaming.Messaging.Workflows;

/// <summary>Immutable consumer definition; stable names are persisted, delegates and CLR names are not.</summary>
public sealed class WorkflowDefinition<TStart, TState>
    where TStart : class
    where TState : class
{
    internal WorkflowDefinition(
        string workflowType,
        int definitionVersion,
        JsonSerializerOptions json,
        Func<WorkflowContext, TStart, WorkflowTurn<TState>> start,
        Dictionary<string, ReplyBinding<TState>> replies,
        Dictionary<string, SignalBinding<TState>> signals
    )
    {
        WorkflowType = workflowType;
        DefinitionVersion = definitionVersion;
        Json = json;
        Start = start;
        Replies = replies;
        Signals = signals;
    }

    /// <summary>Stable technical process type, independent of CLR type names.</summary>
    public string WorkflowType { get; }

    /// <summary>Persisted definition version; mismatches require an explicit consumer migration.</summary>
    public int DefinitionVersion { get; }
    internal JsonSerializerOptions Json { get; }
    internal Func<WorkflowContext, TStart, WorkflowTurn<TState>> Start { get; }
    internal IReadOnlyDictionary<string, ReplyBinding<TState>> Replies { get; }
    internal IReadOnlyDictionary<string, SignalBinding<TState>> Signals { get; }
}

internal sealed record ReplyBinding<TState>(
    string MessageName,
    int SchemaVersion,
    Func<WorkflowContext, TState, JsonElement, WorkflowTurn<TState>> Invoke
)
    where TState : class;

internal sealed record SignalBinding<TState>(
    Type SignalType,
    Func<WorkflowContext, TState, object, WorkflowTurn<TState>> Invoke
)
    where TState : class;
