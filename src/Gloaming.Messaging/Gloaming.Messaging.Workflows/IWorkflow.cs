namespace Gloaming.Messaging.Workflows;

/// <summary>Bounded durable execution. Writes require the caller's native transaction and explicit SaveChanges/commit.</summary>
public interface IWorkflow<TStart, TState>
    where TStart : class
    where TState : class
{
    /// <summary>Start once; an equal retained input returns current progress without invoking Start again.</summary>
    Task<WorkflowSnapshot<TState>> StartAsync(
        WorkflowKey key,
        TStart input,
        CancellationToken cancellationToken = default
    );

    /// <summary>Match an admitted reply to its requested operation and invoke only the saved continuation.</summary>
    Task<WorkflowSnapshot<TState>> HandleReplyAsync(
        WorkflowKey key,
        IncomingMessage reply,
        CancellationToken cancellationToken = default
    );

    /// <summary>Invoke the saved typed signal binding while awaiting input; the caller owns admission and save/commit.</summary>
    Task<WorkflowSnapshot<TState>> SignalAsync<TSignal>(
        WorkflowKey key,
        TSignal signal,
        CancellationToken cancellationToken = default
    )
        where TSignal : class;

    /// <summary>Read detached progress without catch-up, execution or persistence.</summary>
    Task<WorkflowSnapshot<TState>?> ReadAsync(
        WorkflowKey key,
        CancellationToken cancellationToken = default
    );
}
