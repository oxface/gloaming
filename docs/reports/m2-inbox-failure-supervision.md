# M2 follow-up: failure-accounting supervision

The later [dead-letter report](m2-inbox-dead-letters.md) records current naming and metadata
inspection; this report retains its dated supervision evidence.

Date: 2026-10-20. Branch: `feat/inbox-failure-recovery`, continuing the original M2 work.
The owner approved the [concrete exception and behavior](../plans/m2-inbox-failure-supervision.md)
before implementation. The existing staged M2 review snapshot is preserved; this follow-up
is unstaged and uncommitted. Original M2 results remain dated evidence in its
[separate report](m2-inbox-failure-recovery.md).

## Outcome and ownership

Unconfirmed failure accounting now throws InboxFailureAccountingException, derived from
AggregateException. It retains subscription/producer/message identity and both original errors
through named ProcessingFailure/AccountingFailure properties and inherited InnerExceptions.
Successfully recorded handler failures still rethrow the original processing error.

The optional inbox worker logs a distinct critical diagnostic and faults without another
poll on this exception. Its old broad catch could repeatedly re-execute an eligible delivery
whose failures could never be recorded, without consuming its durable failure budget.
Requested shutdown during accounting ends the worker without another poll. Direct callers
still receive both original errors when accounting is interrupted by cancellation.

This is a newly proven reusable fault signal and worker containment mechanism. It does not
change the savepoint/locking/accounting transaction, operator retry, schema, envelope or
deduplication protocol. No new package, runtime dependency or worker option was needed.

| Owner | Responsibility |
| --- | --- |
| Messaging library | Typed unconfirmed-accounting fault, delivery identity/error preservation, distinct critical diagnostic, and termination of the optional worker's polling loop. No host-wide supervision setting is installed. |
| Native .NET host | Applies HostOptions.BackgroundServiceExceptionBehavior. StopHost begins shutdown of the whole host; Ignore leaves the worker faulted without restarting it. |
| Rendering consumer | Shared InboxDemo registration explicitly selects StopHost; both its HTTP host and the separate process role adopt it. Existing classification, retry budget and quarantine codes remain consumer policy. |
| Operations/deployment consumer | Alerting, readiness, process exit handling, repair, restart, and any restart limits. An actually quarantined row still requires the existing authorized, revision-checked retry. |
| Templates | No template or preset changes. No automatic messaging or supervision adoption. |

## Fresh PostgreSQL and process evidence

Eight new cases exercise the approved behavior; the existing M2 accounting-fault assertions
also now require the typed error and preserved identity/errors.

- Two library worker cases fail consumer classification or the native metadata UPDATE after
  the handler has already saved business and outgoing SQL. The execution task faults with
  the typed error, only one handler scope runs, and fresh PostgreSQL reads show no business
  rows, outgoing rows, counters, revision change, completion or quarantine.
- Two library cancellation cases block the savepoint rollback callback and cancel accounting.
  A direct caller receives the original handler error and accounting cancellation together;
  a stopping worker completes without faulting or invoking another handler. PostgreSQL retains
  the unchanged delivery. The callback is a controlled interruption, not a network outage.
- Three native host cases cover throwing policy with StopHost, rejected metadata SQL with
  StopHost, and consumer-selected Ignore. StopHost signals application shutdown and completes
  native shutdown; Ignore permits a running host with its worker task faulted. Fresh storage
  shows no rendering job or partial failure accounting.
- A child-process proof retains the original delivery plus unrelated work, fails job SQL and
  failure-accounting SQL, and observes the distinct diagnostic and native StopHost shutdown.
  A PostgreSQL sequence, which survives transaction rollback, records exactly one handler
  attempt. Neither message completes. Repair both constraints and restart a fresh process:
  both messages finish, with the original delivery identity, payload and receipt time retained,
  no invented failure count, and no operator redrive. The sequence observes exactly the failed
  attempt plus the two successful attempts.
- Existing native SQL, deferred commit, injected rollback, policy and invalid-decision failures
  still preserve both errors and roll back all effects. Actual backend termination still cannot
  overwrite a successor's quarantine. Commit-response loss remains tested after an actual
  PostgreSQL commit: the exception means unconfirmed accounting, and fresh storage may contain
  committed quarantine. No compensating metadata write is introduced.
- Existing competing-processor/operator, repeated redrive, rollback, quarantine/restart,
  unrelated-message, deduplication, admission/authentication, migration and restricted-grant
  proofs remain in the suites run below. Ordinary worker retry still uses fresh scopes.

## Verification

Fresh complete-suite executions: **357 passed, zero failed, zero skipped** across six projects.

| Project | Passed |
| --- | ---: |
| Messaging InboxPostgresTests | 52 |
| InboxDemo.Tests | 20 |
| MessagingWorkerDemo.Tests | 10 |
| Messaging WorkflowPostgresTests | 38 |
| WorkflowDemo.Tests (including all restricted Sales reply cases) | 141 |
| ArchitectureTests | 96 |

The final proof refinements also passed through the focused supervision class (four cases)
and process-recovery class (two cases). These repeat selections above, not six additional
distinct tests. The full solution builds with zero warnings/errors. Active solution
`dotnet format style` and `dotnet format analyzers` both passed with
`--verify-no-changes --no-restore`.

`dotnet csharpier check . --include-generated` passed for 840 files. All 378 local links
in the changed Markdown documents resolve, Markdown whitespace checks passed, and
`git diff --check` passed. The staged binary-diff hash matches the starting snapshot;
no follow-up archive, dependency or schema files changed.

Invocations use .NET SDK 10.0.401, PostgreSQL 18.6 and the existing disposable RabbitMQ/Collector
fixtures through rootless Podman, with the same environment and test command recorded in the
original M2 report. Focused selections use `--filter-class '*FailureSupervisionTests'` and
`--filter-class '*InboxRecoveryProcessTests'` respectively. No new fixture service or durable
repository script is introduced.

## Review-worthy changes and limits

Review the new `InboxFailureAccountingException.cs`, the PostgreSQL processor's existing
accounting catch, the optional `InboxWorker.cs` fault branch, and InboxWorkerOptions documentation.
Review the explicit native host choice in `samples/InboxDemo/InboxDemoHost.cs`; this intentionally
governs the entire consuming sample host. Library/sample FailureSupervisionTests and the new
InboxRecoveryProcessTests case prove adoption. Existing FailureRecoveryTests and restricted
SalesReplyRecoveryTests use the named errors. Family/package/sample documentation, ADR 0018,
design and the extraction plan explain the new guarantee and consumer obligations.

An accounting exception does not prove rollback: its commit response may have been lost.
Repair the cause, inspect fresh state and restart. An unrecorded failure is not automatically
quarantined, and an operator retry is not a worker restart command. Native StopHost does not
promise a nonzero exit code. Ignore does not restart a dead worker. Pre-handler infrastructure
errors retain the existing worker failure-delay behavior; no general infrastructure retry or
supervision framework was added.

The worker's lane pauses on this fault. Progress for unrelated messages while accounting is
broken is not proven or promised; repair/restart restores processing in the process proof.
Durable started-at records, leases/fencing, abandoned-attempt budgets, all-execution counts,
real network partition/failover and automatic containment of repeated process death remain
outside this capability. Existing noncooperative-handler/blocked-statement shutdown limits remain.

DLQ naming/stores/admin convenience and raw SQL column/materialization refactoring remain
separate proposals. Outbox recovery, retention/cleanup, payload editing, schema evolution,
generic management and archived source changes remain excluded. No commit or push was made.
