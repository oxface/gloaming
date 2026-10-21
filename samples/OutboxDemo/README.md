# Ordinary EF state and transactional outgoing commands

An independent, tenantless consumer of Gloaming.Messaging's EF/PostgreSQL outbox. No Events,
EventSourcing, ActorIdentity, Tenancy, Persistence or module-layout dependency.

A draft export with pages can be submitted once. Native expected-version checks and EF's
version predicate reject stale competitors. Submission stages a direct-JSON RenderExportV1
command in the same native transaction. ExportRequestCommands is the application handler;
it uses typed OutgoingMessage.FromPayload with explicit JSON options. Save/commit remain
explicit. Invalid/empty/already
submitted drafts emit no outgoing work. This is consumer policy, not a generic workflow.

The consumer-owned HTTP publisher maps one logical destination to a configured endpoint,
sends stable message identity/name/schema headers and waits for a successful response with
a ten-second sample timeout. Acceptance does not mean rendering completed. The receiver
must handle repeated delivery. [InboxDemo](../InboxDemo/README.md) supplies a separately owned
HTTP inbox receiver; [MessagingDemo](../MessagingDemo/README.md) composes the contexts over RabbitMQ.
This sender itself registers no inbox. Optional correlation/causation/tenant metadata is retained
in the outbox and carried explicitly to the receiver.

```bash
export OUTBOX_DEMO_CONNECTION_STRING='Host=localhost;Database=outbox_demo;Username=demo;Password=demo'
export OUTBOX_DEMO_RECEIVER='http://localhost:8080/commands'
dotnet run --project samples/OutboxDemo/OutboxDemo.csproj
dotnet test --project samples/OutboxDemo.Tests/OutboxDemo.Tests.csproj
```

Use a disposable PostgreSQL database and an HTTP endpoint accepting the command JSON. The
finite journey explicitly applies authored native migrations, creates/submits a new draft,
then dispatches at most one eligible row. It can dispatch an older retained row if a backlog
exists; it does not promise global ordering. Tests run actual Kestrel HTTP and PostgreSQL,
including acceptance-before-completion failure followed by identity-preserving redelivery.
They also prove eligibility, competing submissions, rollback and fresh-context recovery.

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
dotnet run --project samples/OutboxDemo -- --setup
export OUTBOX_DEMO_OPERATOR_TOKEN='replace-with-a-private-token-of-at-least-32-characters'
export OUTBOX_DEMO_URL='http://localhost:5088'
dotnet run --project samples/OutboxDemo -- --serve --worker
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
stops its worker after one accounting fault; repair the dependency and restart. A retained
lease then waits for expiry; a committed dead letter still requires authorized retry.
Attempts counts committed claims, while FailureCount counts committed publisher-exception
observations. Crash/cancellation, expired claims and acceptance followed by completion failure
need not consume that budget. Receiver deduplication remains necessary.

Tests add real HTTP budget, authorization, identity-preserving retry, response-loss and
legacy-schema upgrade evidence. The separate
[process proofs](../MessagingWorkerDemo.Tests/OutboxRecoveryProcessTests.cs) launch this built
host, kill it during accounting, restart it and exercise operator retry after restart.
See [the current protocol and rollout/grant obligations](../../src/Gloaming.Messaging/docs/outbox-recovery.md).

See [the family contract](../../src/Gloaming.Messaging/docs/capabilities.md) for setup/errors
and independent inbox composition, workers and deferred ordering policy. PostgreSQL is currently the supported provider.

Optional message diagnostic context and native telemetry setup are documented in the
[Messaging observability contract](../../src/Gloaming.Messaging/docs/observability.md).
