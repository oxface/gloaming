namespace Gloaming.Messaging.Workflows;

/// <summary>Technical execution lifecycle, independent of the consumer's business status.</summary>
public enum WorkflowStatus
{
    /// <summary>A single outgoing operation is awaiting its bound reply.</summary>
    Waiting = 1,

    /// <summary>No further continuation is scheduled.</summary>
    Completed = 2,

    /// <summary>Typed external input may invoke the saved signal binding.</summary>
    AwaitingInput = 3,
}
