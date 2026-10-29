# 0032: Typed evaluation execution with native metrics

Date: 2026-10-29. Status: implemented for owner interface/implementation review.

Evaluation suites should compare the same workflow cases across ordinary code, native
workflows and agent implementations, and score retained output independently of generation.
Requiring an agent/provider in the common runner would couple those independent choices.
Provide one optional typed evaluation package over native Microsoft evaluation metrics;
MAF and native chat reporting remain explicitly selected consumer dependencies.

The runner validates a finite selected matrix, creates fresh attempt state/input, executes
typed callbacks/checks with bounded concurrency, and records complete outcomes. Strict metric
completeness prevents missing/unknown/errored scores from passing. A shared atomic wrapper cap
counts physical generation/judge chat attempts without configuring clients or estimating tool
loops as one call. Callbacks own fresh application scopes and must cooperate with cancellation;
cleanup is awaited rather than abandoning running state to satisfy a wall-clock deadline.

Versioned typed ledgers retain dataset/implementation/check provenance, evidence kind and
paired numeric deltas without inventing chat conversations, statistical significance or a
business acceptance policy. Consumer adapters may project real chat evidence into native
Microsoft reports. Domain cases, rubrics, provider setup, thresholds, data disclosure and
external-effect isolation stay consumer-owned. This adds no production workflow host,
scheduler, automatic retry, experiment database or distributed process recovery.
See the [local contract and proof owners](../../src/Gloaming.Evaluations/README.md).
