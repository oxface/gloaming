using System.Text.Json;
using Microsoft.Extensions.AI.Evaluation;

namespace Gloaming.Evaluations;

/// <summary>Explicit execution outcomes; missing scores and execution errors cannot pass.</summary>
public enum EvaluationStatus
{
    /// <summary>All required native metrics passed.</summary>
    Passed = 1,

    /// <summary>Completed checks failed or omitted required usable metrics.</summary>
    Failed = 2,

    /// <summary>A callback threw or returned invalid evidence.</summary>
    Error = 3,

    /// <summary>Caller cancellation stopped or prevented this attempt.</summary>
    Cancelled = 4,

    /// <summary>The cooperative attempt deadline elapsed.</summary>
    TimedOut = 5,

    /// <summary>A wrapped model call exceeded the shared run cap.</summary>
    CallLimitReached = 6,
}

/// <summary>Stable identity and evidence for one selected matrix cell, including unstarted cancellations.</summary>
public sealed record EvaluationAttempt<TOutput>(
    string CaseId,
    string TargetId,
    int Repetition,
    EvaluationStatus Status,
    bool OutputAvailable,
    TOutput? Output,
    EvaluationResult Metrics,
    TimeSpan Duration,
    int ModelCalls,
    string? ErrorType,
    IReadOnlyDictionary<string, JsonElement> Artifacts
);

/// <summary>Selected target provenance. Metadata such as prompt/model/options versions is consumer-owned.</summary>
public sealed record EvaluationTargetInfo(
    string Id,
    string Version,
    EvaluationExecutionKind Kind,
    IReadOnlyDictionary<string, string> Metadata
);

/// <summary>Selected native metric/check provenance.</summary>
public sealed record EvaluationCheckInfo(
    string Id,
    string Version,
    IReadOnlyList<string> MetricNames,
    bool IsJudge
);

/// <summary>Paired numeric deltas have no built-in direction or acceptance threshold.</summary>
public sealed record EvaluationComparison(
    string CaseId,
    int Repetition,
    string BaselineTargetId,
    string TargetId,
    EvaluationStatus BaselineStatus,
    EvaluationStatus TargetStatus,
    IReadOnlyDictionary<string, double> NumericDeltas
);

/// <summary>A typed run ledger. Callers own storage/disclosure and any business acceptance thresholds.</summary>
public sealed record EvaluationReport<TOutput>(
    string ExecutionId,
    string SuiteId,
    string DatasetVersion,
    DateTimeOffset StartedAt,
    DateTimeOffset CompletedAt,
    EvaluationRunOptions Options,
    IReadOnlyList<EvaluationTargetInfo> Targets,
    IReadOnlyList<EvaluationCheckInfo> Checks,
    IReadOnlyList<EvaluationAttempt<TOutput>> Attempts,
    IReadOnlyList<EvaluationComparison> Comparisons,
    int ModelCalls
)
{
    /// <summary>The JSON ledger schema version; it does not version consumer payloads or native metrics.</summary>
    public int FormatVersion => 1;

    /// <summary>True only when every planned attempt passed.</summary>
    public bool Passed =>
        Attempts.Count > 0 && Attempts.All(x => x.Status == EvaluationStatus.Passed);

    /// <summary>Writes typed outputs, native metrics and caller-selected artifacts to a caller-owned stream.</summary>
    public Task WriteJsonAsync(
        Stream destination,
        JsonSerializerOptions? serializerOptions = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(destination);

        return JsonSerializer.SerializeAsync(
            destination,
            this,
            serializerOptions ?? JsonSerializerOptions.Web,
            cancellationToken
        );
    }
}
