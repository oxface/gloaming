using System.Diagnostics;
using Microsoft.Extensions.AI.Evaluation;

namespace Gloaming.Evaluations;

/// <summary>Runs explicit typed matrices with native metrics, cooperative lifecycle and a shared physical-call cap.</summary>
public static class EvaluationRunner
{
    /// <summary>Returns the complete selected ledger, including cancellations. Callback cleanup is always awaited.</summary>
    public static async Task<EvaluationReport<TOutput>> RunAsync<TInput, TOutput>(
        EvaluationSuite<TInput, TOutput> suite,
        IReadOnlyList<EvaluationTarget<TInput, TOutput>> targets,
        EvaluationRunOptions? options = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(suite);
        ArgumentNullException.ThrowIfNull(targets);
        ArgumentException.ThrowIfNullOrWhiteSpace(suite.Id);
        ArgumentException.ThrowIfNullOrWhiteSpace(suite.DatasetVersion);
        ArgumentNullException.ThrowIfNull(suite.Cases);
        ArgumentNullException.ThrowIfNull(suite.Checks);

        var settings = Snapshot(options ?? new());
        var cases = suite
            .Cases.Select(item =>
            {
                ArgumentNullException.ThrowIfNull(item);
                ArgumentNullException.ThrowIfNull(item.CreateInput);

                return item with
                {
                    Tags = Names(item.Tags ?? [], "case tags"),
                };
            })
            .ToArray();
        var variants = targets
            .Select(item =>
            {
                ArgumentNullException.ThrowIfNull(item);
                ArgumentException.ThrowIfNullOrWhiteSpace(item.Version);
                ArgumentNullException.ThrowIfNull(item.ExecuteAsync);
                if (!Enum.IsDefined(item.Kind))
                    throw new ArgumentException("Unknown execution kind.", nameof(targets));

                var metadata = new Dictionary<string, string>(
                    item.Metadata ?? new Dictionary<string, string>(),
                    StringComparer.Ordinal
                );
                foreach (var (key, value) in metadata)
                {
                    ArgumentException.ThrowIfNullOrWhiteSpace(key);
                    ArgumentException.ThrowIfNullOrWhiteSpace(value);
                }

                return item with
                {
                    Metadata = metadata,
                };
            })
            .ToArray();
        var checks = suite
            .Checks.Select(check =>
            {
                ArgumentNullException.ThrowIfNull(check);
                ArgumentException.ThrowIfNullOrWhiteSpace(check.Version);
                ArgumentNullException.ThrowIfNull(check.EvaluateAsync);
                var names = Names(check.MetricNames, "check metrics");
                if (names.Length == 0)
                    throw new ArgumentException(
                        "Checks must declare their native metrics.",
                        nameof(suite)
                    );

                return check with
                {
                    MetricNames = names,
                };
            })
            .ToArray();
        Names(cases.Select(x => x.Id), "case IDs");
        Names(variants.Select(x => x.Id), "target IDs");
        Names(checks.Select(x => x.Id), "check IDs");
        Names(checks.SelectMany(x => x.MetricNames), "suite metric names");
        RequireKnown(settings.CaseIds, cases.Select(x => x.Id));
        RequireKnown(settings.TargetIds, variants.Select(x => x.Id));
        cases = cases
            .Where(x =>
                (
                    settings.CaseIds.Count == 0
                    || settings.CaseIds.Contains(x.Id, StringComparer.Ordinal)
                ) && settings.Tags.All(tag => x.Tags!.Contains(tag, StringComparer.Ordinal))
            )
            .ToArray();
        variants = variants
            .Where(x =>
                settings.TargetIds.Count == 0
                || settings.TargetIds.Contains(x.Id, StringComparer.Ordinal)
            )
            .ToArray();
        checks = checks
            .Where(x => !x.IsJudge || settings.IncludeJudges)
            .OrderBy(x => x.IsJudge)
            .ToArray();
        if (cases.Length == 0 || variants.Length == 0 || checks.Length == 0)
            throw new ArgumentException("Select nonempty cases, targets and checks.");

        if (settings.BaselineTargetId is { } baseline && !variants.Any(x => x.Id == baseline))
            throw new ArgumentException("The baseline must be a selected target.");

        if (
            checked((long)cases.Length * variants.Length * settings.Repetitions)
            > settings.MaxOperations
        )
            throw new ArgumentException("The selected matrix exceeds MaxOperations.");

        var jobs = (
            from item in cases
            from target in variants
            from repetition in Enumerable.Range(1, settings.Repetitions)
            select (Item: item, Target: target, Repetition: repetition)
        ).ToArray();
        var started = DateTimeOffset.UtcNow;
        var budget = new EvaluationCallBudget(settings.MaxModelCalls);
        var attempts = new EvaluationAttempt<TOutput>[jobs.Length];
        await Parallel.ForEachAsync(
            Enumerable.Range(0, jobs.Length),
            new ParallelOptions { MaxDegreeOfParallelism = settings.MaxConcurrency },
            async (index, _) =>
            {
                var (item, target, repetition) = jobs[index];
                attempts[index] = await Attempt(
                    item,
                    target,
                    repetition,
                    checks,
                    settings,
                    budget,
                    cancellationToken
                );
            }
        );
        return new(
            Guid.NewGuid().ToString("N"),
            suite.Id,
            suite.DatasetVersion,
            started,
            DateTimeOffset.UtcNow,
            settings,
            variants
                .Select(x => new EvaluationTargetInfo(x.Id, x.Version, x.Kind, x.Metadata!))
                .ToArray(),
            checks
                .Select(x => new EvaluationCheckInfo(x.Id, x.Version, x.MetricNames, x.IsJudge))
                .ToArray(),
            attempts,
            Compare(attempts, settings.BaselineTargetId),
            budget.Calls
        );
    }

    private static async Task<EvaluationAttempt<TOutput>> Attempt<TInput, TOutput>(
        EvaluationCase<TInput, TOutput> item,
        EvaluationTarget<TInput, TOutput> target,
        int repetition,
        EvaluationCheck<TInput, TOutput>[] checks,
        EvaluationRunOptions options,
        EvaluationCallBudget budget,
        CancellationToken cancellationToken
    )
    {
        using var deadline = new CancellationTokenSource(options.AttemptTimeout);
        using var operation = CancellationTokenSource.CreateLinkedTokenSource(
            cancellationToken,
            deadline.Token
        );
        var execution = new EvaluationExecution(budget, operation.Token);
        var watch = Stopwatch.StartNew();
        var metrics = new EvaluationResult();
        var outputAvailable = false;
        TOutput? output = default;
        Exception? failure = null;
        try
        {
            operation.Token.ThrowIfCancellationRequested();
            var input = item.CreateInput();
            output = await target.ExecuteAsync(input, execution, operation.Token);
            outputAvailable = true;
            operation.Token.ThrowIfCancellationRequested();
            foreach (var check in checks)
            {
                var result = await check.EvaluateAsync(
                    new(input, output, item.Expected),
                    execution,
                    operation.Token
                );
                ArgumentNullException.ThrowIfNull(result);
                foreach (var (name, metric) in result.Metrics)
                {
                    if (
                        !check.MetricNames.Contains(name, StringComparer.Ordinal)
                        || metric.Name != name
                    )
                        throw new InvalidOperationException(
                            "A check returned an undeclared metric identity."
                        );

                    metrics.Metrics.Add(name, metric);
                }

                operation.Token.ThrowIfCancellationRequested();
            }
        }
        catch (Exception error)
        {
            failure = error;
        }

        var status =
            cancellationToken.IsCancellationRequested ? EvaluationStatus.Cancelled
            : deadline.IsCancellationRequested ? EvaluationStatus.TimedOut
            : execution.LimitReached ? EvaluationStatus.CallLimitReached
            : failure is not null ? EvaluationStatus.Error
            : EvaluationGate.Passed(metrics, checks.SelectMany(x => x.MetricNames))
                ? EvaluationStatus.Passed
            : EvaluationStatus.Failed;
        return new(
            item.Id,
            target.Id,
            repetition,
            status,
            outputAvailable,
            output,
            metrics,
            watch.Elapsed,
            execution.ModelCalls,
            failure?.GetType().FullName,
            execution.Artifacts
        );
    }

    private static EvaluationRunOptions Snapshot(EvaluationRunOptions options)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.Repetitions);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(options.MaxOperations);
        ArgumentOutOfRangeException.ThrowIfNegative(options.MaxModelCalls);
        if (
            options.MaxConcurrency is < 1 or > 64
            || options.AttemptTimeout <= TimeSpan.Zero
            || options.AttemptTimeout.TotalMilliseconds > uint.MaxValue - 1
        )
            throw new ArgumentOutOfRangeException(nameof(options));

        if (options.BaselineTargetId is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(options.BaselineTargetId);

        return new()
        {
            CaseIds = Names(options.CaseIds, "selected case IDs"),
            TargetIds = Names(options.TargetIds, "selected target IDs"),
            Tags = Names(options.Tags, "selected tags"),
            Repetitions = options.Repetitions,
            MaxOperations = options.MaxOperations,
            MaxConcurrency = options.MaxConcurrency,
            AttemptTimeout = options.AttemptTimeout,
            MaxModelCalls = options.MaxModelCalls,
            IncludeJudges = options.IncludeJudges,
            BaselineTargetId = options.BaselineTargetId,
        };
    }

    private static string[] Names(IEnumerable<string> names, string label)
    {
        ArgumentNullException.ThrowIfNull(names);

        var values = names.ToArray();
        if (
            values.Any(string.IsNullOrWhiteSpace)
            || values.Distinct(StringComparer.Ordinal).Count() != values.Length
        )
            throw new ArgumentException($"Nonblank unique {label} are required.");

        return values;
    }

    private static void RequireKnown(IEnumerable<string> selected, IEnumerable<string> available)
    {
        if (selected.Except(available, StringComparer.Ordinal).Any())
            throw new ArgumentException("Unknown evaluation selection ID.");
    }

    private static EvaluationComparison[] Compare<TOutput>(
        EvaluationAttempt<TOutput>[] attempts,
        string? baseline
    )
    {
        if (baseline is null)
            return [];

        var originals = attempts
            .Where(x => x.TargetId == baseline)
            .ToDictionary(x => (x.CaseId, x.Repetition));
        return attempts
            .Where(x => x.TargetId != baseline)
            .Select(candidate =>
            {
                var original = originals[(candidate.CaseId, candidate.Repetition)];
                var deltas = new Dictionary<string, double>(StringComparer.Ordinal);
                if (Scored(original.Status) && Scored(candidate.Status))
                {
                    foreach (var (name, metric) in candidate.Metrics.Metrics)
                    {
                        if (
                            metric is NumericMetric { Value: { } value }
                            && original.Metrics.Metrics.TryGetValue(name, out var other)
                            && other is NumericMetric { Value: { } prior }
                            && double.IsFinite(value - prior)
                        )
                            deltas.Add(name, value - prior);
                    }
                }

                return new EvaluationComparison(
                    candidate.CaseId,
                    candidate.Repetition,
                    baseline,
                    candidate.TargetId,
                    original.Status,
                    candidate.Status,
                    deltas
                );
            })
            .ToArray();
    }

    private static bool Scored(EvaluationStatus status) =>
        status is EvaluationStatus.Passed or EvaluationStatus.Failed;
}
