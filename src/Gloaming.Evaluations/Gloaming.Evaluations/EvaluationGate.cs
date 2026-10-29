using Microsoft.Extensions.AI.Evaluation;

namespace Gloaming.Evaluations;

/// <summary>Requires complete usable native metrics and their consumer-supplied passing interpretations.</summary>
public static class EvaluationGate
{
    /// <summary>Missing, unknown, inconclusive, errored or null-valued standard metrics do not pass.</summary>
    public static bool Passed(EvaluationResult result, IEnumerable<string> expectedMetrics)
    {
        ArgumentNullException.ThrowIfNull(result);
        ArgumentNullException.ThrowIfNull(expectedMetrics);

        var names = expectedMetrics.ToArray();
        return names.Length > 0
            && names.All(name =>
                result.Metrics.TryGetValue(name, out var metric)
                && metric.Interpretation
                    is {
                        Failed: false,
                        Rating: not (EvaluationRating.Unknown or EvaluationRating.Inconclusive)
                    }
                && Enum.IsDefined(metric.Interpretation.Rating)
                && metric.Diagnostics?.All(x => x.Severity != EvaluationDiagnosticSeverity.Error)
                    != false
                && metric is not BooleanMetric { Value: not true }
                && (
                    metric is not NumericMetric numeric
                    || numeric.Value is { } value && double.IsFinite(value)
                )
            );
    }
}
