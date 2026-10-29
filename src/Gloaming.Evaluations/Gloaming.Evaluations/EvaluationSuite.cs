using Microsoft.Extensions.AI.Evaluation;

namespace Gloaming.Evaluations;

/// <summary>A consumer-owned versioned dataset and native metric checks, independent of execution technology.</summary>
public sealed record EvaluationSuite<TInput, TOutput>(
    string Id,
    string DatasetVersion,
    IReadOnlyList<EvaluationCase<TInput, TOutput>> Cases,
    IReadOnlyList<EvaluationCheck<TInput, TOutput>> Checks
);

/// <summary>Creates fresh input for each attempt. Expected values and captured dependencies must be safe to share.</summary>
public sealed record EvaluationCase<TInput, TOutput>(
    string Id,
    Func<TInput> CreateInput,
    TOutput Expected,
    IReadOnlyList<string>? Tags = null
);

/// <summary>An explicitly selected implementation. The callback owns fresh application scopes and their disposal.</summary>
public sealed record EvaluationTarget<TInput, TOutput>(
    string Id,
    string Version,
    EvaluationExecutionKind Kind,
    Func<TInput, EvaluationExecution, CancellationToken, ValueTask<TOutput>> ExecuteAsync,
    IReadOnlyDictionary<string, string>? Metadata = null
);

/// <summary>The provenance of execution evidence, independent of whether its implementation uses agents.</summary>
public enum EvaluationExecutionKind
{
    /// <summary>Ordinary deterministic execution.</summary>
    Deterministic = 1,

    /// <summary>Execution against scripted fixtures.</summary>
    Fixture = 2,

    /// <summary>Scoring previously recorded output.</summary>
    Recorded = 3,

    /// <summary>Execution against live dependencies.</summary>
    Live = 4,
}

/// <summary>Typed business evidence supplied to a check without fabricating a chat conversation.</summary>
public sealed record EvaluationObservation<TInput, TOutput>(
    TInput Input,
    TOutput Actual,
    TOutput Expected
);

/// <summary>A consumer-owned check returning native metrics. Judge checks run only when explicitly selected.</summary>
public sealed record EvaluationCheck<TInput, TOutput>(
    string Id,
    string Version,
    IReadOnlyList<string> MetricNames,
    Func<
        EvaluationObservation<TInput, TOutput>,
        EvaluationExecution,
        CancellationToken,
        ValueTask<EvaluationResult>
    > EvaluateAsync,
    bool IsJudge = false
);
