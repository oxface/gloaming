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
Incompatible pages fail processing and remain pending for consumer intervention. External
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

Optional message diagnostic context and native telemetry setup are documented in the
[Messaging observability contract](../../src/Gloaming.Messaging/docs/observability.md).
