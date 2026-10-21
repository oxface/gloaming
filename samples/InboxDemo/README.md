# Ordinary EF durable incoming render jobs

An independent inbox-only receiver using Gloaming.Messaging's PostgreSQL runtime, native EF
and Kestrel. No outbox table/publisher, Events, EventSourcing, Tenancy, ActorIdentity, Persistence
or peer implementation dependency. The wire contract is exports.render v1, matching the
[OutboxDemo sender](../OutboxDemo/README.md) through an explicit receiver-local DTO.

POST /commands requires X-Message-Id, X-Message-Name and X-Message-Schema plus JSON
ExportRequestId/Pages. Optional X-Correlation-Id/X-Causation-Id are retained. This finite
demo endpoint binds the producer namespace to exports and rejects tenant metadata; it does
not authenticate arbitrary callers. Production replaces its admission/endpoint permissions.

HTTP 202 follows committed retained intake. Equivalent retries also receive 202; incompatible
identity reuse receives 409; invalid wire input/tenant metadata receives 400. Database faults
are not accepted. Intake does not imply the job was processed or a PDF rendered.

RenderExportHandler separately creates a local RenderJob using ExportRequestId as a semantic
business key. Another delivery ID with identical requirements does not create another job.
Incompatible pages fail processing and quarantine for consumer intervention. External
rendering, file writes, scheduling and completion are outside this locked local handler.

```bash
export INBOX_DEMO_CONNECTION_STRING='Host=localhost;Database=inbox_demo;Username=demo;Password=demo'
dotnet run --project samples/InboxDemo/InboxDemo.csproj -- --setup
export INBOX_DEMO_URL='http://localhost:5087'
dotnet run --project samples/InboxDemo/InboxDemo.csproj -- --worker
# Point OutboxDemo's OUTBOX_DEMO_RECEIVER to http://localhost:5087/commands.
dotnet test --project samples/InboxDemo.Tests/InboxDemo.Tests.csproj
```

--setup is explicit finite native migration setup. Regular hosting/worker does not migrate.
Omit --worker for intake-only operation and call IInboxProcessor<RenderDbContext> explicitly
in fresh scopes. The optional worker only processes the local queue; it never receives/acks HTTP
or a broker. Native save guard is wired in both overrides.

Tests run real PostgreSQL/Kestrel and native RabbitMQ through the composition sample. They
prove commit-before-response, conflict handling, SQL-failure recovery, metadata preservation,
semantic job deduplication, worker opt-in and the separate broker acknowledgement windows.
[Family contract](../../src/Gloaming.Messaging/docs/inbox.md) documents ownership and limitations.

## Failed work and authorized operator retry

Rendering owns failure policy: invalid/conflicting commands and unclassified errors quarantine
immediately. Recognized PostgreSQL serialization/deadlock failures retry after one second,
stopping at three recorded failures since the last applied operator retry. The library supplies
counters, locking and conditional transitions; classification, limits and codes remain editable
consumer policy. A killed/cancelled attempt may leave no recorded failure; see the
[recovery contract](../../src/Gloaming.Messaging/docs/inbox-recovery.md).

If failure accounting itself fails, the library throws InboxFailureAccountingException with
delivery identity and both errors. The optional worker logs a critical diagnostic and faults
without invoking another handler. Register explicitly selects native StopHost supervision:
the entire sample host shuts down, including HTTP endpoints. Repair the database/grant/policy
fault before restarting. Inspect fresh state because an accounting commit response can be lost;
only an actually quarantined delivery needs authorized operator retry. A worker fault alone
does not quarantine the row, and no particular exit code or deployment restart is supplied.

Enable the optional operator routes by setting `INBOX_DEMO_OPERATOR_TOKEN` to a private credential
of at least 32 characters. Without it the routes are absent. Native ASP.NET Core authentication
compares bearer-credential SHA-256 digests in constant time and grants the fixed
`rendering.inbox.retry` permission to `rendering-operator`. Replace this finite demo policy with
your production identity/permission policy; use HTTPS for remote credential-bearing requests.

Inspect and resolve the consumer precondition, then submit the exact revision returned by GET:

```sh
curl -H "Authorization: Bearer $INBOX_DEMO_OPERATOR_TOKEN" \
  "http://localhost:5087/operations/inbox/$MESSAGE_ID"
curl -X POST -H "Authorization: Bearer $INBOX_DEMO_OPERATOR_TOKEN" \
  -H 'Content-Type: application/json' -d '{"expectedRevision":1}' \
  "http://localhost:5087/operations/inbox/$MESSAGE_ID/retry"
```

The fixed subscription/producer are `rendering.jobs`/`exports`; callers cannot select another
lane. GET returns lifecycle/counts/revision/safe reason, excluding payload and exception text.
POST returns 204 for applied retry and 409 for stale/inapplicable state. Missing/wrong credentials
return 401 before storage work. Requests/results log the fixed principal, delivery ID and revision;
durable approval/audit is a separate consumer choice.

Retry preserves identity, payload, metadata and total failures, resets only the cycle count and
makes the retained row eligible. Processing repeats current admission/business rules; permanent
conflicts can quarantine again. Stale requests cannot reopen that later quarantine. After response
loss reuse the same revision, not an automatically refreshed one. Equivalent intake deduplicates
throughout.

Apply `--setup` with old processors stopped before using the updated model. The additive recovery
migration preserves historical pending/completed rows, setting unknown past counts to zero. Old
processors ignore quarantine: mixed versions/binary rollback need a separately reviewed plan.
Startup still performs no migration.

Optional message diagnostic context and native telemetry setup are documented in the
[Messaging observability contract](../../src/Gloaming.Messaging/docs/observability.md).
