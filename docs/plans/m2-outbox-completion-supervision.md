# Outbox completion-failure supervision

Status: owner selected this follow-up on 2026-10-21 after approving M2 outbox dead
letters. Implementation authorization includes the existing callable-dispatch,
PostgreSQL storage and native worker/host proof boundaries. The preceding work is
checkpointed as `6bcdf9c`; this slice remains a separate reviewable change.
The [slice report](../reports/m2-outbox-completion-supervision.md) records the implementation,
fresh proofs and remaining limits.

## Problem and outcome

Publication returns normally, but completion SQL or its commit fails. The original
worker logs that error and polls again. After lease expiry it can publish repeatedly
without consuming the publisher-exception budget. A missing commit response can
instead leave the row completed. Neither outcome justifies dead-lettering or
incrementing publisher-failure counts.

Make unconfirmed completion distinguishable to callable consumers and fault the
optional worker on that signal. Native host supervision owns containment; repair,
inspection and restart remain consumer operations. This contains one worker's loop,
not other dispatchers or repeated external process restarts.

## Public surface and consumer usage

One type in Gloaming.Messaging.EntityFrameworkCore:

```csharp
public sealed class OutboxCompletionException : Exception
{
    public OutboxCompletionException(Guid messageId, Exception completionFailure);
    public Guid MessageId { get; }
    // InnerException is the original completion error.
}
```

The signal means the publisher returned normally and completion was not confirmed.
It never asserts rollback, incomplete storage or downstream business success. It
retains identity and the original error, without retaining the envelope. Callers
catching exact native SQL/IO exception types for completion must inspect InnerException.

```csharp
try
{
    await dispatcher.DispatchNextAsync(cancellationToken);
}
catch (OutboxCompletionException failure)
{
    // Record failure.MessageId using consumer diagnostics, suspend this dispatch
    // lane and inspect fresh storage after repairing the dependency.
    throw;
}
```

No recovery interface or automatic recovery write is added. AddOutboxWorker logs a
distinct critical diagnostic and rethrows. HostOptions.BackgroundServiceExceptionBehavior
remains native and explicit consumer configuration: StopHost begins host shutdown;
Ignore leaves the worker faulted without restarting it. OutboxDemo already explicitly
selects StopHost, so its existing executable is the adopter.

Completion-stage cancellation also carries this signal to direct callers, with the
original OperationCanceledException as InnerException. Requested cancellation keeps
the attempt's cancelled telemetry result. During requested shutdown the optional worker
ends normally without another poll. Cancellation during publication keeps its existing
OperationCanceledException contract. ClaimLost remains a result when completion observes
an expired/changed lease; it does not fault the worker. Ordinary publisher failures with
confirmed accounting and claim-stage faults keep their current delay/fresh-scope behavior.

## File and schema scope

- EF exception, IOutboxDispatcher XML contract, worker fault branch and options docs.
- PostgreSQL dispatcher wraps only errors from the existing completion operation,
  including transaction start, SQL, commit and disposal. Claim and publication errors
  retain their existing paths; completion still never calls failure policy/accounting.
- Existing real PostgreSQL and HTTP adoption assertions follow the typed error.
  Native host tests cover SQL rejection and a response lost after actual commit under
  both StopHost and Ignore. Blocked completion tests cover direct cancellation and
  worker shutdown. A built OutboxDemo process proves acceptance, stop, repair and
  identity-preserving recovery after restart. Existing fencing and ordinary-retry proofs
  remain relevant regressions.
- Family/package docs, OutboxDemo adoption instructions, design/roadmap and slice report.

No migrations, grants, dependencies, registration, worker options, transport API or
template preset change. Payload editing, completed-message replay, retention, lease
renewal, global execution caps, readiness/alerts and dispatch parallelism remain deferred.
