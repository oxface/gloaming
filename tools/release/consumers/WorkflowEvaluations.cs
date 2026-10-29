using WorkflowEvaluationDemo;

var report = await DocumentEvaluations.RunAsync(
    new()
    {
        BaselineTargetId = "rules",
        MaxModelCalls = 2,
        MaxConcurrency = 3,
    }
);
if (
    !report.Passed
    || report.Attempts.Count != 6
    || report.ModelCalls != 2
    || report.Comparisons.Count != 4
)
    throw new InvalidOperationException("Package-only workflow evaluation comparison failed.");

await using var output = new MemoryStream();
await report.WriteJsonAsync(output);
Console.WriteLine(
    "PASS package-only evaluation of native workflows with/without agent and retained outputs"
);
