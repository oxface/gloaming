using System.Text.Json;

namespace Rootbolt.Messaging.Workflows;

/// <summary>Explicit method and wire-contract bindings. Construction copies serializer options; Build freezes bindings.</summary>
public sealed class WorkflowBuilder<TStart, TState>
    where TStart : class
    where TState : class
{
    private readonly string workflowType;
    private readonly int definitionVersion;
    private readonly JsonSerializerOptions json;
    private Func<WorkflowContext, TStart, WorkflowTurn<TState>>? start;
    private readonly Dictionary<string, ReplyBinding<TState>> replies = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SignalBinding<TState>> signals = new(
        StringComparer.Ordinal
    );

    /// <summary>Declare a stable persisted type/version and explicit consumer serializer options.</summary>
    public WorkflowBuilder(
        string workflowType,
        int definitionVersion,
        JsonSerializerOptions serializerOptions
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(workflowType);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(definitionVersion);
        ArgumentNullException.ThrowIfNull(serializerOptions);

        this.workflowType = workflowType;
        this.definitionVersion = definitionVersion;
        json = new(serializerOptions);
        json.MakeReadOnly(populateMissingResolver: true);
    }

    /// <summary>Bind initial business state creation; repeated equal starts do not invoke this method.</summary>
    public WorkflowBuilder<TStart, TState> StartWith(
        Func<WorkflowContext, TStart, WorkflowTurn<TState>> start
    )
    {
        ArgumentNullException.ThrowIfNull(start);
        if (this.start is not null)
            throw new InvalidOperationException("Start is already bound.");

        this.start = start;
        return this;
    }

    /// <summary>Bind a named continuation to one wire alias/schema and exact typed reply.</summary>
    public WorkflowBuilder<TStart, TState> OnReply<TReply>(
        string continuationName,
        string messageName,
        int schemaVersion,
        Func<WorkflowContext, TState, TReply, WorkflowTurn<TState>> handler
    )
        where TReply : class
    {
        ValidateName(continuationName);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(schemaVersion);
        ArgumentNullException.ThrowIfNull(handler);

        replies.Add(
            continuationName,
            new(
                messageName,
                schemaVersion,
                (context, state, payload) =>
                    handler(
                        context,
                        state,
                        payload.Deserialize<TReply>(json)
                            ?? throw new JsonException("Reply must not be null.")
                    )
            )
        );
        return this;
    }

    /// <summary>Bind typed external input. The stored continuation selects this method.</summary>
    public WorkflowBuilder<TStart, TState> OnSignal<TSignal>(
        string continuationName,
        Func<WorkflowContext, TState, TSignal, WorkflowTurn<TState>> handler
    )
        where TSignal : class
    {
        ValidateName(continuationName);
        ArgumentNullException.ThrowIfNull(handler);

        signals.Add(
            continuationName,
            new(
                typeof(TSignal),
                (context, state, signal) => handler(context, state, (TSignal)signal)
            )
        );
        return this;
    }

    /// <summary>Freeze bindings; no graph traversal, method discovery or runtime code generation is performed.</summary>
    public WorkflowDefinition<TStart, TState> Build()
    {
        if (start is null)
            throw new InvalidOperationException("A start method is required.");

        return new(
            workflowType,
            definitionVersion,
            json,
            start,
            new(replies, StringComparer.Ordinal),
            new(signals, StringComparer.Ordinal)
        );
    }

    private void ValidateName(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        if (replies.ContainsKey(name) || signals.ContainsKey(name))
            throw new InvalidOperationException("Continuation names must be unique.");
    }
}
