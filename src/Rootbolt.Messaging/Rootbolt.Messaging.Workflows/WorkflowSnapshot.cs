namespace Rootbolt.Messaging.Workflows;

/// <summary>Detached typed view of persisted or proposed progress. A returned write snapshot is not a commit receipt.</summary>
public sealed record WorkflowSnapshot<TState>(
    WorkflowKey Key,
    int DefinitionVersion,
    long Version,
    WorkflowStatus Status,
    string? ContinuationName,
    Guid? PendingCommandId,
    TState State
)
    where TState : class;
