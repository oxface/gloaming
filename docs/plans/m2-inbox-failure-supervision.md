# M2 follow-up: supervising failure-accounting faults

Status: public interface and behavior approved by the owner, 2026-10-20. The reviewed
proposal below is implemented for line-by-line review. The original
[M2 proposal](m2-inbox-failure-recovery.md) remains the record of the recovery protocol;
the [supervision report](../reports/m2-inbox-failure-supervision.md) records fresh evidence
separately from the original M2 test results.

## Problem and bounded outcome

Before this follow-up, the processor combined the handler error and a rollback/policy/accounting
error in an ordinary AggregateException. The optional worker caught every exception and polled
again. If accounting consistently failed, the same oldest eligible delivery could be selected
indefinitely without consuming its durable retry budget. Earlier accounting-fault tests proved
rollback and error preservation; they did not prove worker supervision or progress under a
persistently broken recovery path.

Distinguish that fault from an ordinary recorded handler failure and let it fault the
optional worker immediately. No automatic accounting-fault retry is added inside this
worker. Retrying the handler repeatedly is not an independent retry of metadata recording.
Native host supervision then applies the consumer's configured response. There is no
new lease, timer, schema, registration method, health framework or recovery transaction.

## Proposed public surface

Add one type to `Gloaming.Messaging.EntityFrameworkCore`:

```csharp
public sealed class InboxFailureAccountingException : AggregateException
{
    public InboxFailureAccountingException(
        string subscriptionKey,
        string producerKey,
        Guid messageId,
        Exception processingFailure,
        Exception accountingFailure);

    public string SubscriptionKey { get; }
    public string ProducerKey { get; }
    public Guid MessageId { get; }
    public Exception ProcessingFailure { get; }
    public Exception AccountingFailure { get; }
}
```

The inherited InnerExceptions retain the original processing error followed by the
accounting error; named properties avoid requiring callers to depend on their positions.
Identity properties identify the retained delivery without including its payload or tenant.
Existing callers catching AggregateException still catch this derived type. Callers or
tests depending on the exact runtime type must change.

The PostgreSQL processor throws this type from its existing accounting catch: failure
rolling back to the savepoint, choosing/validating the policy decision, writing metadata
or committing that metadata. Ordinary successfully recorded handler failures still throw
the original handler exception. Operator retry failures retain their existing contract.

An accounting exception means accounting was not confirmed to the caller. A lost commit
response can leave the metadata committed. The exception never asserts that the delivery
is pending, that rollback succeeded, or that the transaction certainly did not commit.
The processor performs no compensating write or second accounting transaction.

## Worker and executable consumer usage

The optional worker catches InboxFailureAccountingException before its general exception
handler, logs a distinct critical diagnostic with subscription and delivery identity, and
rethrows. Its execution task faults and makes no further processing calls. Requested
shutdown during accounting ends the worker without another poll; the processor still
preserves both errors for callers. Ordinary recorded handler failures continue to use
FailureDelay and a fresh scope; normal cancellation remains cooperative.

The existing InboxDemo process role already adopts this optional worker. Its shared registration
makes the host supervision choice explicit using native .NET configuration (both HTTP and
process hosts adopt it):

```csharp
services.Configure<HostOptions>(options =>
    options.BackgroundServiceExceptionBehavior =
        BackgroundServiceExceptionBehavior.StopHost);
```

With StopHost, the host begins shutdown after the worker faults. This is also the native
default, but the sample states its operational choice. Other services in that host stop
too. With Ignore, the affected worker stays faulted while the host can continue; Ignore
does not restart it. Applications calling IInboxProcessor directly can catch the typed
fault and choose their own supervision, alerting and restart policy.

After repairing the accounting dependency, restart the worker/host and inspect fresh
storage. An unrecorded failure leaves the delivery at its prior committed state; a lost
response may instead have committed retry or quarantine. Only an actually quarantined
delivery needs the existing authorized, revision-checked operator retry. No new operator
command clears this worker fault, and no particular process exit code is promised.

Stopping the worker contains the repeated-selection loop; it does not establish progress
for unrelated messages on that same worker while accounting is broken. Other processors
retain the existing row-lock exclusion rules. Automatic containment of repeated process
death would require a separately reviewed durable claim protocol with fencing and an
explicit consumer policy for abandoned attempts. Lease expiry alone would describe an
unknown outcome, not establish a permanently failed message.

## File changes and required proofs

The approved implementation includes:

- The new exception file, the processor's existing accounting catch, and the worker's
  distinct fault branch. InboxWorkerOptions keeps its current constructor and meaning
  for ordinary processing failures.
- Explicit native host supervision in InboxDemo, inherited by its process worker role.
- Existing PostgreSQL accounting-fault proofs updated to assert the type, identity and
  both original errors for rollback, policy, SQL, commit and connection-termination faults.
  Lost accounting-commit responses must retain their current ambiguous-outcome proof.
- Real PostgreSQL worker/host tests for a throwing failure policy and failing metadata SQL:
  one handler entry, no repeated poll after fault, rolled-back business/outgoing work,
  unchanged failure metadata where commit did not occur, and native host shutdown.
- A process-level adoption proof for the InboxDemo worker: inject an accounting SQL fault,
  observe the distinct diagnostic and host stop, repair the database fault, restart, and
  prove fresh storage permits processing without changing identity or payload.
- Preservation of the existing ordinary-failure retry/fresh-scope proof and a shutdown
  case during accounting. Run the relevant family, sample and restricted Sales tests.
- Family recovery/worker documentation, the M2 report and the existing ADR updated to
  distinguish this supervision guarantee from consumer operational policy and the
  remaining crash/accounting limitations.

This follow-up excludes DLQ naming or inspection-store changes, raw SQL materialization
refactoring, inbox leases, outbox recovery, retention, payload editing and schema evolution.
Those discussions do not expand this implementation scope.
