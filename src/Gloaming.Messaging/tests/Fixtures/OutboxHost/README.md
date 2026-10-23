# Ordinary EF state and transactional outgoing commands

Test-only executable fixture. Start with the [public delivery example](../../../samples/Delivery/README.md);
this host retains the operational and compatibility cases exercised by the delivery suite.

Worker registration and polling options are supplied by the explicitly selected
[hosting package](../../../Gloaming.Messaging.EntityFrameworkCore.Hosting/README.md), using the
`Gloaming.Messaging.EntityFrameworkCore.Hosting` namespace. Callable persistence/delivery
operations remain independently usable without this package.

An independent, tenantless consumer of Gloaming.Messaging's EF/PostgreSQL outbox. No Events,
EventSourcing, ActorIdentity, Tenancy, Persistence or module-layout dependency.

A draft export with pages can be submitted once. Native expected-version checks and EF's
version predicate reject stale competitors. Default submission stages a direct-JSON RenderExportV1
command in the same native transaction. ExportRequestCommands is the application handler;
it uses typed OutgoingMessage.FromPayload with explicit JSON options. Save/commit remain
explicit. Invalid/empty/already
submitted drafts emit no outgoing work. This is consumer policy, not a generic workflow.

The consumer-owned HTTP publisher maps one logical destination to a configured endpoint,
sends stable message identity/name/schema headers and waits for a successful response with
a ten-second sample timeout. Acceptance does not mean rendering completed. The receiver
must handle repeated delivery. [InboxDemo](../InboxHost/README.md) supplies a separately owned
HTTP inbox receiver; [MessagingDemo](../BrokerJourney/README.md) composes the contexts over RabbitMQ.
This sender itself registers no inbox. Optional correlation/causation/tenant metadata is retained
in the outbox and carried explicitly to the receiver.

```bash
export OUTBOX_DEMO_CONNECTION_STRING='Host=localhost;Database=outbox_demo;Username=demo;Password=demo'
export OUTBOX_DEMO_RECEIVER='http://localhost:8080/commands'
dotnet run --project src/Gloaming.Messaging/tests/Fixtures/OutboxHost/Messaging.OutboxHost.csproj
dotnet test --project src/Gloaming.Messaging/tests/DeliveryTests/DeliveryTests.csproj
```

Use a disposable PostgreSQL database and an HTTP endpoint accepting the command JSON. The
finite journey explicitly applies authored native migrations, creates/submits a new draft,
then dispatches at most one eligible row. It can dispatch an older retained row if a backlog
exists; it does not promise global ordering. Tests run actual Kestrel HTTP and PostgreSQL,
including acceptance-before-completion failure followed by identity-preserving redelivery.
They also prove eligibility, competing submissions, rollback and fresh-context recovery.

## Explicit writer schema

Existing callers write exports.render schema 1 with ExportRequestId/Pages. After upgrading
the dispatch publisher and every receiver/processor, select schema 2 explicitly:

```csharp
var commands = new ExportRequestCommands(database,
    new EfOutbox<ExportDbContext>(database), renderSchemaVersion: 2);
```

Schema 2 writes RenderExportV2 with ExportRequestId/PageCount, preserving the page requirement.
Only 1 and 2 are accepted at construction. SubmitAsync and the caller's save/commit are unchanged;
rollback also rolls back the v2 enqueue. Ordinary JSON remains sufficient for the producer.
Publishers preserve the selected schema/payload, including redelivery after unconfirmed completion.
Existing queued v1 rows are never rewritten. The [compatibility journey](../BrokerJourney/README.md#schema-compatibility-journey)
and [deployment contract](../../../docs/schema-compatibility.md) cover rollout,
reader rollback limits and identity-preserving operator retry. This adds no migration or worker.

## Dead letters and operator retry

The finite journey and optional dispatch host adopt ExportOutboxFailurePolicy. Unsupported
route/contract versions stop with `export-contract-unsupported`. HTTP 408/429/5xx and
connection/time-out observations retry after one second until the third recorded failure
in a cycle, then stop with `export-retry-limit`. Other exceptions stop with
`export-review-required`. These codes, threshold and classification belong to this sample.
An exception can follow remote acceptance; accounting cannot retract that acceptance.

Apply migrations explicitly before starting a host. Use the existing producer journey above
to create submitted work, then run the optional native HTTP/worker composition:

```bash
dotnet run --project src/Gloaming.Messaging/tests/Fixtures/OutboxHost -- --setup
export OUTBOX_DEMO_OPERATOR_TOKEN='replace-with-a-private-token-of-at-least-32-characters'
export OUTBOX_DEMO_URL='http://localhost:5088'
dotnet run --project src/Gloaming.Messaging/tests/Fixtures/OutboxHost -- --serve --worker
```

The host starts no migrations. `--serve` without `--worker` needs no receiver URI/publisher
and can host operator operations independently. Without OUTBOX_DEMO_OPERATOR_TOKEN the
operator routes are absent; an empty/short configured token rejects startup. Authentication
uses the sample bearer credential and native `exports.outbox.retry` permission. Production
identity integration, secret lifecycle, operator approval/audit and deployment remain consumer
work. OUTBOX_DEMO_LEASE_SECONDS optionally sets a positive lease duration (default 30);
it is not a transport timeout or renewal policy.

```bash
curl --fail -H "Authorization: Bearer $OUTBOX_DEMO_OPERATOR_TOKEN" \
  'http://localhost:5088/operations/outbox/dead-letters?pageSize=25'
curl --fail -H "Authorization: Bearer $OUTBOX_DEMO_OPERATOR_TOKEN" \
  'http://localhost:5088/operations/outbox/dead-letters/MESSAGE_ID'
curl --fail -X POST -H "Authorization: Bearer $OUTBOX_DEMO_OPERATOR_TOKEN" \
  -H 'Content-Type: application/json' -d '{"expectedRevision":6}' \
  'http://localhost:5088/operations/outbox/dead-letters/MESSAGE_ID/retry'
```

The endpoints fix `exports.render`; query inputs cannot choose another route. List supports
pageSize, exact failureCode and after=NextCursor. Metadata excludes payload, tenant, diagnostic
context and exception text. Detail returns 404 for inapplicable work; retry returns 204 or
409 for a stale/inapplicable revision. Authorization precedes storage work. Retry logs operator
attribution and changes eligibility only. The next dispatcher repeats current validation;
an incompatible schema can dead-letter again under the same identity/payload.

The host explicitly uses native StopHost supervision. OutboxFailureAccountingException
or OutboxCompletionException stops its worker without another poll. A completion fault means
the publisher returned normally but completion was not confirmed; the original storage error
is InnerException. Repair the dependency and inspect fresh storage with an admitted credential:

```sql
SELECT message_id, dispatched_at, lease_until, attempts, failure_count, dead_lettered_at
FROM exports.outgoing_work
WHERE message_id = 'MESSAGE_ID';
```

Restart with the same `--serve --worker` command after repair. A retained lease waits for
expiry before redelivery of the same envelope. A completed row is already excluded; a committed
dead letter still requires authorized retry. The dead-letter endpoint cannot retry pending
or completed work. Missing commit responses can leave completion or accounting committed.
This follow-up requires no new migrations, grants or configuration.
Attempts counts committed claims, while FailureCount counts committed publisher-exception
observations. Crash/cancellation, expired claims and acceptance followed by completion failure
need not consume that budget. Receiver deduplication remains necessary.

Tests add real HTTP budget, authorization, identity-preserving retry, response-loss and
legacy-schema upgrade evidence. The separate
[process proofs](../../DeliveryTests/Processes/OutboxRecoveryProcessTests.cs) launch this built
host, kill it during accounting, restart it and exercise operator retry after restart. They
also prove HTTP acceptance followed by a completion SQL fault stops the host after one send;
repair and restart permit the same message to complete without increasing its failure budget.
See [the current protocol and rollout/grant obligations](../../../docs/outbox-recovery.md).

## Backlog and health

The existing `--serve` composition supplies native process/storage health and a fixed-route
backlog sampler. `--serve --worker` additionally expects its dispatch worker to keep running.
Setup remains the explicit `--setup` command above. The status route shares the configured
operator credential/permission with dead-letter operations:

```bash
curl --fail -H "Authorization: Bearer $OUTBOX_DEMO_OPERATOR_TOKEN" \
  'http://localhost:5088/operations/outbox/status'
curl --fail 'http://localhost:5088/health/live'
curl --fail 'http://localhost:5088/health/ready'
```

Status returns a fresh database-time snapshot for exports.render, with no-store caching.
Pending = Eligible + Delayed + Leased; DeadLetters is separate and completed work is excluded.
OldestEligibleAge measures age since original insertion for currently eligible work, including
time spent in previous attempts. No payload, tenant, message ID, lease token or error text is
returned. Unauthorized callers receive 401; a missing operator credential removes the status
route. Database failures return 503 with no details, rather than a false empty backlog.

Public health responses contain only Healthy/Unhealthy. Liveness checks the process. Readiness
performs a fresh storage read and, in worker mode, checks the dedicated host's one expected
dispatch BackgroundService. A faulted/stopped worker under native Ignore makes the live host
unready. The configured StopHost normally shuts the host down instead. Idle or delayed work
does not itself fail readiness; the task check does not establish transport availability or
business progress. Health checks perform no recovery and do not restart workers.

Native meter `ModulithFoundry.OutboxDemo.Outbox` supplies cached observable gauges for pending,
eligible, delayed, leased and dead-letter counts, oldest eligible age and observation age.
The sampler uses a five-second interval after each read and requests a three-second query
deadline. Positive OUTBOX_DEMO_OBSERVATION_SECONDS customizes the interval within Task.Delay
bounds. Query faults retain the last values while observation age increases; before any
success gauges are absent. Callbacks perform no database I/O. Exporter/resource setup and
alert thresholds are consumer choices. Multiple replicas can observe the same backlog, so
their counts must not be summed as independent work.

The [visibility contract](../../../docs/outbox-observability.md) lists instruments,
metadata-only grants, staleness and native OTel subscription. Tests exercise PostgreSQL/Kestrel,
MeterListener callbacks, storage faults/repair and worker readiness.

## Completed-outbox cleanup

Apply the forward completed-row index migration explicitly with the existing `--setup`
command. An owner provisions a separate maintenance login; it receives metadata SELECT and
DELETE, with no UPDATE, payload SELECT, ownership, role membership or business-table access:

```sql
CREATE ROLE exports_outbox_maintenance LOGIN PASSWORD 'replace-with-a-private-password'
  NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION NOINHERIT;
GRANT USAGE ON SCHEMA exports TO exports_outbox_maintenance;
GRANT SELECT (message_id, destination, dispatched_at, dead_lettered_at, lease_token, lease_until)
  ON exports.outgoing_work TO exports_outbox_maintenance;
GRANT DELETE ON exports.outgoing_work TO exports_outbox_maintenance;
```

Review existing PUBLIC/inherited privileges and row-security policies with the owner. A DELETE
grant is table-wide; native RLS is a separate way to constrain arbitrary SQL to admitted completed
rows. Enabling policies must account for all existing producer/dispatcher roles. This sample
performs no role provisioning or policy reconciliation itself.

Choose the evidence cutoff explicitly and run one finite batch using only the maintenance
connection. The example timestamp is a consumer choice, not a default retention window:

```bash
export OUTBOX_DEMO_MAINTENANCE_CONNECTION_STRING='Host=localhost;Database=outbox_demo;Username=exports_outbox_maintenance;Password=replace-with-a-private-password'
export OUTBOX_DEMO_COMPLETED_BEFORE='2026-10-01T00:00:00Z'
export OUTBOX_DEMO_CLEANUP_BATCH_SIZE='100'
dotnet run --project src/Gloaming.Messaging/tests/Fixtures/OutboxHost -- --cleanup
```

Cleanup requires a separate `--cleanup` invocation, a UTC timestamp ending in Z and a batch
size from 1 through 1000. It verifies the actual login/effective grants, fixes exports.render,
requests a thirty-second cancellation deadline and prints RouteKey/CompletedBefore/BatchSize/
Deleted only after observed commit. It needs no ordinary producer connection, receiver URI,
operator token or worker. Startup applies no migrations and starts no HTTP host or sampler.

Only completed rows strictly before the cutoff qualify. Pending, delayed, leased and dead-letter
work is preserved. Competing cleaners may wait and return fewer or zero; zero does not prove
exhaustion. After commit-response loss, deletion may already have happened. Repeating the
command may remove the next batch; scheduling, reconciliation and totals remain owner policy.
Dispatch credentials and HTTP operator authority gain no DELETE permission.

Deletion removes sender identity/envelope/failure history and permits accepting that
MessageId again. Choose an evidence window and keep identities unique. Obsolete publishers
may still accept externally after completion/deletion; inbox retention is independent.
See the [retention contract](../../../docs/outbox-retention.md).

See [the family contract](../../../docs/capabilities.md) for setup/errors
and independent inbox composition, workers and deferred ordering policy. PostgreSQL is currently the supported provider.

Optional message diagnostic context and native telemetry setup are documented in the
[Messaging observability contract](../../../docs/observability.md).
