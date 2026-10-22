namespace Gloaming.EventSourcing.EntityFrameworkCore;

/// <summary>Identifies an ordered progress observation, not a durable completion record.</summary>
public enum AggregateRegenerationPhase
{
    /// <summary>A new isolated regeneration attempt has started.</summary>
    Started = 1,

    /// <summary>The attempt has replayed its captured stream; save/commit may still fail.</summary>
    Replayed = 2,

    /// <summary>A recognized native conflict will be retried using a fresh scope and transaction.</summary>
    Retrying = 3,
}

/// <summary>Ordered pre-commit/attempt progress. Terminal outcomes are returned separately.</summary>
public sealed record AggregateRegenerationProgress(
    int Attempt,
    AggregateRegenerationPhase Phase,
    AggregateRebuildResult? CapturedHead = null,
    Exception? Error = null
);
