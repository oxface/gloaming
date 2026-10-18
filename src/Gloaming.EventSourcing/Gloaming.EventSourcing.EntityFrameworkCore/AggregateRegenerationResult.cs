namespace Gloaming.EventSourcing.EntityFrameworkCore;

public enum AggregateRegenerationStatus
{
    Completed = 1,
    NotFound = 2,
    Conflict = 3,
    Cancelled = 4,
    Failed = 5,
    Indeterminate = 6,
}

/// <summary>
/// A terminal operation outcome after cleanup. CapturedHead alone does not establish commit.
/// Error may include cleanup failure without changing an acknowledged Completed outcome.
/// </summary>
public sealed record AggregateRegenerationResult(
    AggregateRegenerationStatus Status,
    int Attempts,
    AggregateRebuildResult? CapturedHead,
    Exception? Error
);
