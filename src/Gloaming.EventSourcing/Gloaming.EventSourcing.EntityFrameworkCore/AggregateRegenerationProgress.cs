namespace Gloaming.EventSourcing.EntityFrameworkCore;

public enum AggregateRegenerationPhase
{
    Started = 1,
    Replayed = 2,
    Retrying = 3,
}

/// <summary>Ordered pre-commit/attempt progress. Terminal outcomes are returned separately.</summary>
public sealed record AggregateRegenerationProgress(
    int Attempt,
    AggregateRegenerationPhase Phase,
    AggregateRebuildResult? CapturedHead = null,
    Exception? Error = null
);
