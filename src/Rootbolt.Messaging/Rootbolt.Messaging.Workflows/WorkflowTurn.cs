using System.Diagnostics.CodeAnalysis;

namespace Rootbolt.Messaging.Workflows;

/// <summary>Candidate state and at most one outgoing request. Persistence belongs to the runner and caller transaction.</summary>
[SuppressMessage(
    "Design",
    "CA1000:Do not declare static members on generic types",
    Justification = "Typed factories retain the consumer state type on approved return instructions."
)]
public sealed class WorkflowTurn<TState>
    where TState : class
{
    private WorkflowTurn(
        TState state,
        WorkflowStatus status,
        OutgoingMessage? command,
        string? continuation
    )
    {
        ArgumentNullException.ThrowIfNull(state);

        State = state;
        Status = status;
        Command = command;
        ContinuationName = continuation;
    }

    internal TState State { get; }
    internal WorkflowStatus Status { get; }
    internal OutgoingMessage? Command { get; }
    internal string? ContinuationName { get; }

    /// <summary>Wait for the named reply continuation after atomically enqueueing this command.</summary>
    public static WorkflowTurn<TState> Request(
        TState state,
        OutgoingMessage command,
        string continueAt
    )
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentException.ThrowIfNullOrWhiteSpace(continueAt);

        return new(state, WorkflowStatus.Waiting, command, continueAt);
    }

    /// <summary>Persist final state without enqueueing another request.</summary>
    public static WorkflowTurn<TState> Complete(TState state) =>
        new(state, WorkflowStatus.Completed, null, null);

    /// <summary>Persist paused state awaiting typed external input at the named continuation.</summary>
    public static WorkflowTurn<TState> WaitForInput(TState state, string continueAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(continueAt);

        return new(state, WorkflowStatus.AwaitingInput, null, continueAt);
    }
}
