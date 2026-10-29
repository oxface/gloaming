# One evaluation set across native workflows

The [consumer](DocumentEvaluations.cs) evaluates two document-title cases against three targets:
a deterministic native MAF workflow, a native agent workflow using a scripted `IChatClient`,
and previously recorded outcomes. All return the same typed business result. The suite owns
exact-output and numeric length checks, dataset identity and implementation/prompt versions.
No paid inference, credentials, services or database are required.

```sh
dotnet run --project src/Gloaming.Evaluations/samples/WorkflowEvaluationDemo/WorkflowEvaluationDemo.csproj
# Select only the workflow without an agent:
dotnet run --project src/Gloaming.Evaluations/samples/WorkflowEvaluationDemo/WorkflowEvaluationDemo.csproj -- rules
```

Optional positional arguments explicitly select target IDs (`rules`, `agent`, `recorded`).
Each attempt creates a fresh graph and agent/client where needed. Code callers select cases,
tags, repetitions, judges, concurrency, timeout and call cap through native runner options.
The executable exits nonzero unless every selected attempt passes. Report files go to the
consumer-selected `.artifacts/evaluations/<execution>/` directory.

`run.json` contains all typed attempts and baseline comparisons. Native `agent-report.html`
and `agent-report.json` project actual agent conversation evidence only; ordinary workflow
outcomes retain their own type. The default run makes two scripted chat calls and scores six
attempts. Its passing fixtures demonstrate runner wiring and regression checks, not live quality.
Supplying live clients, prompt/model/parameter provenance, quality rubrics, calibrated judges
and acceptance thresholds belongs to the adopting application.

The [tests](../../tests/EvaluationTests/EvaluationTests.csproj) add cancellation/disposal,
concurrency, report round trips, fresh inputs, incomplete/invalid metrics, selection errors,
budgets across streaming and judges, and failure evidence. Run:

```sh
dotnet test --project src/Gloaming.Evaluations/tests/EvaluationTests/EvaluationTests.csproj
```

The [package proof](../../../../tools/release/README.md#typed-evaluations) compiles this
consumer separately against exact archives and performs a second empty-cache locked restore.
