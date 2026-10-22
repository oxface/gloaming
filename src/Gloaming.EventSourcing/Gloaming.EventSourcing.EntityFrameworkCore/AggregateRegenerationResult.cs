namespace Gloaming.EventSourcing.EntityFrameworkCore;

/// <summary>Terminal regeneration outcome after native attempt cleanup.</summary>
public enum AggregateRegenerationStatus
{
    /// <summary>Commit was acknowledged; a later cleanup error can still be retained separately.</summary>
    Completed = 1,

    /// <summary>The requested stream was not found in the admitted scope.</summary>
    NotFound = 2,

    /// <summary>Recognized competing-writer conflicts exhausted the selected attempt budget.</summary>
    Conflict = 3,

    /// <summary>Requested cancellation ended the operation without a claimed acknowledged completion.</summary>
    Cancelled = 4,

    /// <summary>A terminal failure prevented a claimed acknowledged completion.</summary>
    Failed = 5,

    /// <summary>Commit was attempted but not acknowledged; inspect fresh state before another decision.</summary>
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
