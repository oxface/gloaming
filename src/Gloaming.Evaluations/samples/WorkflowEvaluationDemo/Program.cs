using Gloaming.Evaluations;
using WorkflowEvaluationDemo;

// Explicit consumer registry and selection, without library-owned CLI/host discovery.
var targets = args.Length == 0 ? Array.Empty<string>() : args;
var report = await DocumentEvaluations.RunAsync(
    new()
    {
        TargetIds = targets,
        BaselineTargetId = targets.Length == 0 || targets.Contains("rules") ? "rules" : null,
        MaxModelCalls = 2,
        MaxConcurrency = 2,
    }
);
var directory = Path.Combine(".artifacts", "evaluations", report.ExecutionId);
Directory.CreateDirectory(directory);
await using (var output = File.Create(Path.Combine(directory, "run.json")))
    await report.WriteJsonAsync(output);

await DocumentEvaluations.WriteNativeReportsAsync(report, directory);
Console.WriteLine(
    $"{report.Attempts.Count} attempts; passed={report.Passed}; fixture transport calls={report.ModelCalls}; reports={directory}"
);
return report.Passed ? 0 : 1;
