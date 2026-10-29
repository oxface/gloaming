using Gloaming.Evaluations;

namespace EvaluationTests;

public sealed partial class RunnerTests
{
    [Fact]
    public async Task BoundedConcurrency_AdmitsOnlyConfiguredWorkers_AndAwaitsAllScopes()
    {
        var active = 0;
        var maximum = 0;
        var observations = new object();
        var finished = 0;
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var target = Target(
            "bounded",
            async (input, _, token) =>
            {
                var current = Interlocked.Increment(ref active);
                lock (observations)
                    maximum = Math.Max(current, maximum);

                if (current == 2)
                    entered.TrySetResult();

                try
                {
                    await release.Task.WaitAsync(token);
                    return input;
                }
                finally
                {
                    Interlocked.Decrement(ref active);
                    Interlocked.Increment(ref finished);
                }
            }
        );
        var run = EvaluationRunner.RunAsync(
            Suite(),
            [target],
            new() { Repetitions = 6, MaxConcurrency = 2 },
            Token
        );
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
            Assert.Equal(2, Volatile.Read(ref active));
        }
        finally
        {
            release.TrySetResult();
        }

        var report = await run;
        Assert.True(report.Passed);
        Assert.Equal(2, maximum);
        Assert.Equal(0, active);
        Assert.Equal(6, finished);
    }

    [Fact]
    public async Task SelectionAndMetadata_AreSnapshottedBeforeCallbacks()
    {
        var metadata = new Dictionary<string, string> { ["prompt"] = "v1" };
        string[] selected = ["one"];
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var target = Target(
            "one",
            async (input, _, token) =>
            {
                entered.TrySetResult();
                await release.Task.WaitAsync(token);
                return input;
            }
        ) with
        {
            Metadata = metadata,
        };
        var run = EvaluationRunner.RunAsync(
            Suite(),
            [target],
            new() { TargetIds = selected },
            Token
        );
        try
        {
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), Token);
            metadata["prompt"] = "changed";
            selected[0] = "changed";
        }
        finally
        {
            release.TrySetResult();
        }

        var report = await run;
        Assert.True(report.Passed);
        Assert.Equal("v1", report.Targets[0].Metadata["prompt"]);
        Assert.Equal("one", report.Options.TargetIds[0]);
    }

    [Fact]
    public async Task ProducedDefaultOutput_IsDistinctFromNoOutput_AndIndependentCancellationIsError()
    {
        var suite = Suite() with { Cases = [new("zero", () => 0, 0)] };
        var report = await EvaluationRunner.RunAsync(
            suite,
            [
                Target("default"),
                Target("cancelled-elsewhere", (_, _, _) => throw new OperationCanceledException()),
            ],
            cancellationToken: Token
        );
        var produced = report.Attempts[0];
        Assert.True(produced.OutputAvailable);
        Assert.Equal(0, produced.Output);
        Assert.Equal(EvaluationStatus.Passed, produced.Status);
        Assert.False(report.Attempts[1].OutputAvailable);
        Assert.Equal(EvaluationStatus.Error, report.Attempts[1].Status);
    }
}
