namespace Gloaming.Evaluations;

/// <summary>Explicit bounded matrix selection. No provider clients, retries or application thresholds are selected.</summary>
public sealed class EvaluationRunOptions
{
    /// <summary>Empty selects all cases; unknown IDs are rejected.</summary>
    public IReadOnlyList<string> CaseIds { get; init; } = [];

    /// <summary>Cases must contain every selected tag.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Empty selects all targets; unknown IDs are rejected.</summary>
    public IReadOnlyList<string> TargetIds { get; init; } = [];

    /// <summary>Independent executions of each case/target pair.</summary>
    public int Repetitions { get; init; } = 1;

    /// <summary>Maximum simultaneous attempts; callbacks must be safe for the selected concurrency.</summary>
    public int MaxConcurrency { get; init; } = 1;

    /// <summary>Reject a larger matrix before invoking any callback.</summary>
    public int MaxOperations { get; init; } = 1000;

    /// <summary>Cooperative deadline for generation and checks together. Cleanup is awaited, never abandoned.</summary>
    public TimeSpan AttemptTimeout { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>Shared physical call cap for wrapped chat clients, including judges. Zero prohibits wrapped calls.</summary>
    public int MaxModelCalls { get; init; }

    /// <summary>Explicitly includes judge checks; it does not configure their clients.</summary>
    public bool IncludeJudges { get; init; }

    /// <summary>Optional selected target for paired status comparisons and numeric candidate-minus-baseline deltas.</summary>
    public string? BaselineTargetId { get; init; }
}
