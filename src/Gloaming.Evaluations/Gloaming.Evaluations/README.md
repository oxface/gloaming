# Typed evaluation runner

Versioned consumer-owned cases and checks run against explicitly selected typed targets.
This optional .NET 10 package depends on native `Microsoft.Extensions.AI.Evaluation` 10.11.0,
without MAF, EF, Hosting, native reporting or a model vendor. Use it for ordinary code, chat
clients, agents, workflows with or without agents, and re-scoring recorded outputs.

## Explicit composition

```csharp
var suite = new EvaluationSuite<Request, Outcome>(
    "documents.review", datasetVersion,
    cases,     // ID, fresh input factory, typed expected outcome, optional tags
    checks);   // ID/version, declared metric names, typed callback returning EvaluationResult

var report = await EvaluationRunner.RunAsync(suite,
    [rulesTarget, agentWorkflowTarget],
    new EvaluationRunOptions
    {
        CaseIds = ["refine-description"],
        BaselineTargetId = "rules",
        Repetitions = 3,
        MaxConcurrency = 2,
        MaxModelCalls = 12,
        AttemptTimeout = TimeSpan.FromMinutes(2),
    }, token);
await report.WriteJsonAsync(callerOwnedStream, cancellationToken: token);
```

Each `EvaluationTarget<TInput,TOutput>` has an ID, implementation version, evidence kind,
optional metadata, and an async typed operation. The consumer explicitly selects the suite
for its workflow and supplies a target for each implementation being compared. No registry,
DI discovery, CLI, provider factory or universal workflow abstraction is imposed.

`EvaluationCase<TInput,TOutput>` calls its input factory once for each attempt; use it to
clone mutable input. Expected outcomes and captured callback dependencies must be immutable
or safe to share. Targets create/dispose fresh application contexts, native workflow graphs,
agent sessions and other operation resources inside their callback. The runner creates fresh
`EvaluationExecution` state per attempt; it creates no application DI scopes or transactions.
User callbacks/checks must support the selected concurrency. It awaits their disposal before
reporting completion; callbacks must not start untracked work or retain execution state.

## Matrix and native checks

The selected case × target × repetition matrix is validated before any input/operation/check
callback runs. IDs and declared metrics are nonblank, unique and ordinal. Unknown case/target
IDs fail; selected tags are conjunctive. An empty selection or a matrix exceeding `MaxOperations`
fails. Defaults: one repetition, concurrency one (configurable 1–64), maximum 1000 attempts,
five-minute cooperative attempt deadline and zero wrapped model calls.

Each selected check receives typed input, actual and expected output, the attempt execution
and its cancellation token. It returns native `EvaluationResult`; checks retain native metric
names, diagnostics, interpretations and serialization. The suite declares unique metric names
across all its checks. Undeclared/mismatched identities are execution errors. Missing required
metrics, missing/unknown/inconclusive interpretations, error diagnostics, false/null boolean
values and null/nonfinite numeric values cannot pass. Native interpretations define numeric
acceptance; the runner imposes no quality scale, business threshold or improvement direction.
The callback can use native `IEvaluator` with explicitly supplied chat evidence/context.

Deterministic checks run before judge checks. Judges are omitted unless `IncludeJudges` is
explicitly true, and selected checks run even when an earlier completed metric failed.
Judge clients/rubrics/calibration and expected negative controls remain consumer-owned.
Judges cannot turn failed deterministic checks into a passing attempt. Exceptions stop that
attempt's remaining checks, retain partial evidence, and do not stop unrelated attempts.

## Actual chat-call budget

Wrap each physical generation/judge `IChatClient` with `execution.WrapChatClient(client)`.
The wrapper shares one atomic run cap and counts each admitted nonstreaming request or
stream enumeration, including failed requests. Put it immediately above the physical
transport, below tool loops or retry layers, so every physical attempt is counted. A transport
with hidden retries is outside that guarantee. The wrapper links the attempt token even
when caller code omits a provider token. Disposing it disposes the wrapped client.

Zero `MaxModelCalls` rejects a wrapped call before transport. Exhaustion marks the attempt
`CallLimitReached` even when callback code catches the limit exception. Concurrent attempts
cannot exceed the shared cap; judges and generation spend the same budget. Scripted chat
clients consume this cap too: counts describe admitted chat calls, not billed requests.
This is a developer execution bound, not billing, token quotas or authorization. Arbitrary
callbacks, unwrapped clients, hidden transport retries and nonchat tools are not intercepted.
No credentials are loaded and no live client is constructed by the library.

## Lifecycle, reports and comparison

An attempt returns `Passed`, `Failed`, `Error`, `Cancelled`, `TimedOut` or `CallLimitReached`.
`OutputAvailable` distinguishes a produced default/null value from no output. Setup errors
throw before execution; callback exceptions become ledger entries with exception type only,
without raw exception text/stack. The runner never retries. A callback's independent
`OperationCanceledException` is an error unless the caller/attempt token was cancelled.

Cancellation signals active operations, stops admitting callback work and returns the complete
planned ledger, including cancelled unstarted attempts. It does not throw caller cancellation
after admission. Deadlines/cancellation are cooperative: the runner awaits callbacks and their
cleanup, so code ignoring cancellation can delay completion indefinitely. It never abandons
running application scopes to meet a wall-clock limit. Caller cancellation takes precedence
over timeout, then call-limit exhaustion, then callback error. No retained artifact is a claim
that external effects rolled back. Callers own isolation and cleanup of databases/tools.

`EvaluationReport<TOutput>` records execution/dataset identity, selected options, target
implementation/metadata versions, selected checks/versions, typed outcomes, native metrics,
duration, chat-call counts and explicit caller-recorded JSON artifacts. Suite/check IDs and
versions, dataset hashes, prompt/model/options versions and recorded-output provenance must
be supplied honestly by the consumer. `Deterministic`, `Fixture`, `Recorded` and `Live` kinds
identify evidence; fixtures do not establish live model quality or prevent unwrapped calls.
Caller-selected output/artifacts may contain sensitive content; callers own report disclosure,
retention and serializer options. Artifacts are cloned and names must be unique per attempt.

An explicitly selected baseline is paired by case/repetition with each other selected target.
Comparisons retain both statuses and finite shared numeric candidate-minus-baseline deltas
for completed scored attempts. They supply no statistical significance, acceptance policy or
automatic winner. Ledger order is deterministic regardless of completion order; concurrency
does not promise execution order or a fixed distribution of shared budget among attempts.

`WriteJsonAsync` writes format version 1 to a caller-owned stream. It preserves native metric
serialization and typed consumer payloads, without a compatibility promise for arbitrary custom
types, serializers or future native metric schemas. This initial pre-v1 ledger has no historical
migration/old-reader guarantee. Custom output/metric serializers require their own proof.
Nonfinite native values need suitable caller JSON number handling if retained in JSON.

Native Microsoft HTML/JSON chat reporting remains optional consumer composition. The
[sample projection](../samples/WorkflowEvaluationDemo/DocumentEvaluations.cs) uses actual
recorded agent conversations and leaves ordinary typed outcomes in the general ledger; it
does not invent chat messages for nonchat execution. No automatic report storage, provider,
background hosting, distributed scheduler, process recovery or experiment database is included.
