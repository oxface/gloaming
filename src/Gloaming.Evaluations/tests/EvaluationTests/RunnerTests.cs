using System.Text.Json;
using Gloaming.Evaluations;
using Microsoft.Extensions.AI.Evaluation;
using WorkflowEvaluationDemo;

namespace EvaluationTests;

public sealed partial class RunnerTests
{
    [Fact]
    public async Task NativeWorkflows_SameCases_WithAgentWithoutAgentAndRecorded_ProduceTypedReports()
    {
        var report = await DocumentEvaluations.RunAsync(
            new()
            {
                MaxModelCalls = 2,
                MaxConcurrency = 3,
                BaselineTargetId = "rules",
            },
            Token
        );
        Assert.True(report.Passed);
        Assert.Equal(6, report.Attempts.Count);
        Assert.Equal(2, report.ModelCalls);
        Assert.Equal(4, report.Comparisons.Count);
        Assert.All(report.Comparisons, x => Assert.Equal(0, x.NumericDeltas["TitleLength"]));
        Assert.All(
            report.Attempts.Where(x => x.TargetId == "rules"),
            x => Assert.False(x.Artifacts.ContainsKey("conversation"))
        );
        Assert.All(
            report.Attempts.Where(x => x.TargetId == "agent"),
            x => Assert.True(x.Artifacts.ContainsKey("conversation"))
        );
        Assert.DoesNotContain(
            typeof(EvaluationRunner).Assembly.GetReferencedAssemblies(),
            x =>
                x.Name!.StartsWith("Microsoft.Agents", StringComparison.Ordinal)
                || x.Name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
        );
        await using var output = new MemoryStream();
        await report.WriteJsonAsync(output, cancellationToken: Token);
        output.Position = 0;
        var restored = await JsonSerializer.DeserializeAsync<EvaluationReport<DocumentOutcome>>(
            output,
            JsonSerializerOptions.Web,
            Token
        );
        Assert.NotNull(restored);
        Assert.True(restored.Passed);
        Assert.Equal("titles-v1", restored.DatasetVersion);
        Assert.Contains(
            restored.Targets,
            x => x.Id == "agent" && x.Metadata["prompt"] == "normalize-v1"
        );
        Assert.IsType<BooleanMetric>(restored.Attempts[0].Metrics.Metrics["ExactTitle"]);
        Assert.NotNull(restored.Attempts[0].Output);

        var directory = Path.Combine(
            Path.GetTempPath(),
            "gloaming-eval-report-" + Guid.NewGuid().ToString("N")
        );
        Directory.CreateDirectory(directory);
        try
        {
            await DocumentEvaluations.WriteNativeReportsAsync(report, directory);
            Assert.True(File.Exists(Path.Combine(directory, "agent-report.html")));
            Assert.Contains(
                "documents.normalize-title",
                await File.ReadAllTextAsync(Path.Combine(directory, "agent-report.json"), Token)
            );
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    [Fact]
    public async Task Selection_RepetitionsAndBaseline_KeepStableMatrixAndMetricDeltas()
    {
        var suite = Suite() with
        {
            Cases = [new("one", () => 1, 1, ["regression"]), new("two", () => 2, 2, ["other"])],
        };
        var report = await EvaluationRunner.RunAsync(
            suite,
            [
                Target("baseline"),
                Target("candidate", (value, _, _) => new(value + 1)),
                Target("excluded"),
            ],
            new()
            {
                CaseIds = ["one", "two"],
                Tags = ["regression"],
                TargetIds = ["candidate", "baseline"],
                Repetitions = 2,
                BaselineTargetId = "baseline",
            },
            Token
        );
        Assert.Equal(4, report.Attempts.Count);
        Assert.All(report.Attempts, x => Assert.Equal("one", x.CaseId));
        Assert.Equal(
            [1, 2],
            report.Attempts.Where(x => x.TargetId == "candidate").Select(x => x.Repetition)
        );
        Assert.All(
            report.Comparisons,
            x =>
            {
                Assert.Equal(EvaluationStatus.Passed, x.BaselineStatus);
                Assert.Equal(EvaluationStatus.Failed, x.TargetStatus);
                Assert.Equal(1, x.NumericDeltas["Value"]);
            }
        );
        Assert.False(report.Passed);
    }

    [Fact]
    public async Task FreshInputs_EachAttemptGetsIndependentMutableInput()
    {
        var creates = 0;
        var suite = new EvaluationSuite<List<int>, int>(
            "mutable",
            "v1",
            [
                new(
                    "one",
                    () =>
                    {
                        Interlocked.Increment(ref creates);
                        return [1];
                    },
                    2
                ),
            ],
            [
                new(
                    "size",
                    "v1",
                    ["Size"],
                    (observation, _, _) =>
                        Metrics(Boolean("Size", observation.Actual == observation.Expected))
                ),
            ]
        );
        ValueTask<int> Execute(List<int> input, EvaluationExecution _, CancellationToken token)
        {
            token.ThrowIfCancellationRequested();
            input.Add(2);
            return new(input.Count);
        }
        var report = await EvaluationRunner.RunAsync(
            suite,
            [
                new("a", "v1", EvaluationExecutionKind.Deterministic, Execute),
                new("b", "v1", EvaluationExecutionKind.Deterministic, Execute),
            ],
            new() { MaxConcurrency = 4, Repetitions = 2 },
            Token
        );
        Assert.True(report.Passed);
        Assert.Equal(4, creates);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("unknown")]
    [InlineData("inconclusive")]
    [InlineData("diagnostic")]
    [InlineData("null-value")]
    [InlineData("false-value")]
    public async Task InvalidRequiredMetric_CannotPass(string condition)
    {
        var metric = Boolean("Exact", true);
        switch (condition)
        {
            case "unknown":
                metric.Interpretation = new(EvaluationRating.Unknown, false);
                break;
            case "inconclusive":
                metric.Interpretation = new(EvaluationRating.Inconclusive, false);
                break;
            case "diagnostic":
                metric.AddDiagnostics(
                    new EvaluationDiagnostic(
                        EvaluationDiagnosticSeverity.Error,
                        "invalid judge result"
                    )
                );
                break;
            case "null-value":
                metric.Value = null;
                break;
            case "false-value":
                metric.Value = false;
                break;
        }

        var suite = Suite() with
        {
            Checks =
            [
                new(
                    "required",
                    "v1",
                    ["Exact"],
                    (_, _, _) => condition == "missing" ? Metrics() : Metrics(metric)
                ),
            ],
        };
        var report = await EvaluationRunner.RunAsync(
            suite,
            [Target("target")],
            cancellationToken: Token
        );
        Assert.Equal(EvaluationStatus.Failed, Assert.Single(report.Attempts).Status);
        Assert.False(report.Passed);
    }

    [Fact]
    public async Task Preflight_InvalidSelectionAndOversizedMatrix_ExecuteNoCallbacks()
    {
        var calls = 0;
        var target = Target(
            "one",
            (value, _, _) =>
            {
                calls++;
                return new(value);
            }
        );
        foreach (
            var options in new EvaluationRunOptions[]
            {
                new() { CaseIds = ["missing"] },
                new() { TargetIds = ["missing"] },
                new() { BaselineTargetId = "missing" },
                new() { Tags = ["missing"] },
                new() { Repetitions = 2, MaxOperations = 1 },
                new() { CaseIds = ["one", "one"] },
            }
        )
            await Assert.ThrowsAsync<ArgumentException>(() =>
                EvaluationRunner.RunAsync(Suite(), [target], options, Token)
            );

        var duplicate = Suite() with
        {
            Checks = [Suite().Checks[0], Suite().Checks[0] with { Id = "other" }],
        };
        await Assert.ThrowsAsync<ArgumentException>(() =>
            EvaluationRunner.RunAsync(duplicate, [target], cancellationToken: Token)
        );
        Assert.Equal(0, calls);
    }

    [Fact]
    public async Task CallbackFailure_IsReportedWithoutSensitiveExceptionText_AndOtherAttemptsContinue()
    {
        var report = await EvaluationRunner.RunAsync(
            Suite(),
            [
                Target(
                    "bad",
                    (_, _, _) =>
                        throw new InvalidOperationException("private prompt and credentials")
                ),
                Target("good"),
            ],
            cancellationToken: Token
        );
        var error = Assert.Single(report.Attempts, x => x.TargetId == "bad");
        Assert.Equal(EvaluationStatus.Error, error.Status);
        Assert.False(error.OutputAvailable);
        Assert.Equal(typeof(InvalidOperationException).FullName, error.ErrorType);
        Assert.Equal(
            EvaluationStatus.Passed,
            Assert.Single(report.Attempts, x => x.TargetId == "good").Status
        );
        Assert.DoesNotContain(
            "private prompt",
            JsonSerializer.Serialize(report, JsonSerializerOptions.Web)
        );
    }

    [Fact]
    public async Task Cancellation_StopsAdmission_WaitsForCleanup_AndRetainsCompleteLedger()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var calls = 0;
        var disposed = false;
        var target = Target(
            "blocked",
            async (_, _, token) =>
            {
                calls++;
                try
                {
                    entered.SetResult();
                    await Task.Delay(Timeout.Infinite, token);
                    return 1;
                }
                finally
                {
                    await Task.Yield();
                    disposed = true;
                }
            }
        );
        var run = EvaluationRunner.RunAsync(
            Suite(),
            [target],
            new() { Repetitions = 3 },
            cancellation.Token
        );
        await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
        await cancellation.CancelAsync();
        var report = await run;
        Assert.True(disposed);
        Assert.Equal(1, calls);
        Assert.Equal(3, report.Attempts.Count);
        Assert.All(report.Attempts, x => Assert.Equal(EvaluationStatus.Cancelled, x.Status));
    }

    [Fact]
    public async Task Timeout_UsesAttemptToken_AndAwaitsCallbackDisposal()
    {
        var disposed = false;
        var report = await EvaluationRunner.RunAsync(
            Suite(),
            [
                Target(
                    "slow",
                    async (_, _, token) =>
                    {
                        try
                        {
                            await Task.Delay(Timeout.Infinite, token);
                            return 1;
                        }
                        finally
                        {
                            disposed = true;
                        }
                    }
                ),
            ],
            new() { AttemptTimeout = TimeSpan.FromMilliseconds(50) },
            Token
        );
        Assert.True(disposed);
        Assert.Equal(EvaluationStatus.TimedOut, Assert.Single(report.Attempts).Status);
    }

    [Fact]
    public async Task UndeclaredMetric_IsExecutionError_NotPartialSuccess()
    {
        var suite = Suite() with
        {
            Checks =
            [
                new("wrong", "v1", ["Expected"], (_, _, _) => Metrics(Boolean("Other", true))),
            ],
        };
        var report = await EvaluationRunner.RunAsync(
            suite,
            [Target("one")],
            cancellationToken: Token
        );
        Assert.Equal(EvaluationStatus.Error, Assert.Single(report.Attempts).Status);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static BooleanMetric Boolean(string name, bool value) =>
        new(name, value)
        {
            Interpretation = new(
                value ? EvaluationRating.Good : EvaluationRating.Unacceptable,
                !value
            ),
        };

    private static ValueTask<EvaluationResult> Metrics(params EvaluationMetric[] metrics) =>
        new(new EvaluationResult(metrics));

    private static EvaluationSuite<int, int> Suite() =>
        new(
            "numbers",
            "dataset-v1",
            [new("one", () => 1, 1)],
            [
                new(
                    "contract",
                    "check-v1",
                    ["Exact", "Value"],
                    (observation, _, _) =>
                        Metrics(
                            Boolean("Exact", observation.Actual == observation.Expected),
                            new NumericMetric("Value", observation.Actual)
                            {
                                Interpretation = new(EvaluationRating.Good, false),
                            }
                        )
                ),
            ]
        );

    private static EvaluationTarget<int, int> Target(
        string id,
        Func<int, EvaluationExecution, CancellationToken, ValueTask<int>>? execute = null
    ) =>
        new(
            id,
            "implementation-v1",
            EvaluationExecutionKind.Deterministic,
            execute ?? ((input, _, _) => new(input))
        );
}
