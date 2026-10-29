# Evaluation sets and execution targets

The optional [Gloaming.Evaluations package](Gloaming.Evaluations/README.md) runs versioned
typed cases against selected implementations, with native Microsoft evaluation metrics.
It requires neither agents nor workflows, EF, Hosting or model-vendor clients.

The [document-title consumer](samples/WorkflowEvaluationDemo/README.md) runs the same set
against a native deterministic workflow, a native agent workflow with a scripted client,
and previously recorded outcomes. The [tests](tests/EvaluationTests/EvaluationTests.csproj)
cover selection, isolation, metrics, reports, budgets, concurrency and cooperative lifecycle.
The [local package proof](../../tools/release/README.md#typed-evaluations) independently installs
the runner and repeats the workflow comparison from exact NuGet archives.

Workflow definitions, dataset content, expected outcomes, evaluation rubrics, provider setup,
report disclosure and application acceptance remain consumer-owned. This runner executes
evaluation attempts; it is not a production workflow host or an experiment scheduling service.
