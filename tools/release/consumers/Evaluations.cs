using Gloaming.Evaluations;
using Microsoft.Extensions.AI.Evaluation;

var suite = new EvaluationSuite<int, int>(
    "double",
    "dataset-v1",
    [new("two", () => 2, 4)],
    [
        new(
            "equal",
            "v1",
            ["Equal"],
            (observation, _, _) =>
                new(
                    new EvaluationResult(
                        new BooleanMetric("Equal", observation.Actual == observation.Expected)
                        {
                            Interpretation = new(EvaluationRating.Good, false),
                        }
                    )
                )
        ),
    ]
);
var report = await EvaluationRunner.RunAsync(
    suite,
    [new("function", "v1", EvaluationExecutionKind.Deterministic, (input, _, _) => new(input * 2))]
);
if (!report.Passed || report.ModelCalls != 0)
    throw new InvalidOperationException(
        "The independently packaged ordinary-function evaluation failed."
    );

var dependencies = typeof(EvaluationRunner).Assembly.GetReferencedAssemblies();
if (
    dependencies.Any(x =>
        x.Name!.StartsWith("Microsoft.Agents", StringComparison.Ordinal)
        || x.Name.StartsWith("Microsoft.EntityFrameworkCore", StringComparison.Ordinal)
    )
)
    throw new InvalidOperationException("Ordinary evaluation must not select MAF or EF.");

await using var output = new MemoryStream();
await report.WriteJsonAsync(output);
Console.WriteLine(
    "PASS independently packaged typed evaluations without agent/workflow/provider dependencies"
);
