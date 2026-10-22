# Ordinary EF durable incoming render jobs

An independent inbox-only receiver using Gloaming.Messaging's PostgreSQL runtime, native EF
and Kestrel. No outbox table/publisher, EventSourcing, Tenancy, ActorIdentity, Persistence or peer
implementation dependency. Rendering opts into Gloaming.Events.Serialization for receiver-local
historical JSON decoding. The wire contract is exports.render v1/v2, matching the
[OutboxDemo sender](../OutboxDemo/README.md) through independently defined DTOs.

POST /commands requires X-Message-Id, X-Message-Name and X-Message-Schema plus JSON
ExportRequestId/Pages for schema 1 or ExportRequestId/PageCount for schema 2.
Optional X-Correlation-Id/X-Causation-Id are retained. This finite
demo endpoint binds the producer namespace to exports and rejects tenant metadata; it does
not authenticate arbitrary callers. Production replaces its admission/endpoint permissions.

HTTP 202 follows committed retained intake. Equivalent retries also receive 202; incompatible
identity reuse receives 409; invalid wire input/tenant metadata receives 400. Database faults
are not accepted. Intake does not imply the job was processed or a PDF rendered.

RenderExportHandler separately creates a local RenderJob using ExportRequestId as a semantic
business key. Another delivery ID with identical requirements does not create another job.
Incompatible pages fail processing and are dead-lettered for consumer intervention. External
rendering, file writes, scheduling and completion are outside this locked local handler.

```bash
export INBOX_DEMO_CONNECTION_STRING='Host=localhost;Database=inbox_demo;Username=demo;Password=demo'
dotnet run --project src/Gloaming.Messaging/samples/InboxDemo/InboxDemo.csproj -- --setup
export INBOX_DEMO_URL='http://localhost:5087'
dotnet run --project src/Gloaming.Messaging/samples/InboxDemo/InboxDemo.csproj -- --worker
# Point OutboxDemo's OUTBOX_DEMO_RECEIVER to http://localhost:5087/commands.
dotnet test --project src/Gloaming.Messaging/samples/InboxDemo.Tests/InboxDemo.Tests.csproj
```

--setup is explicit finite native migration setup. Regular hosting/worker does not migrate.
Omit --worker for intake-only operation and call IInboxProcessor<RenderDbContext> explicitly
in fresh scopes. The optional worker only processes the local queue; it never receives/acks HTTP
or a broker. Native save guard is wired in both overrides.

Tests run real PostgreSQL/Kestrel and native RabbitMQ through the composition sample. They
prove commit-before-response, conflict handling, SQL-failure recovery, metadata preservation,
semantic job deduplication, worker opt-in and the separate broker acknowledgement windows.
[Family contract](../../docs/inbox.md) documents ownership and limitations.

## Retained schemas and deployment

The handler registers the current receiver-local command at schema 2 and an explicit v1-to-v2
JSON transformation in the existing optional codec. Admission and processing both decode, but
intake retains the original schema/payload. The sender's CLR DTOs are not referenced.

Original v1 redelivery therefore still deduplicates after upgrading while its receipt remains. Reusing that delivery ID
for v2 conflicts even when the decoded export/pages are equal; a new delivery ID can use the
existing semantic job key. Operator retry reexecutes the current decoder without editing
payloads. Installing compatible code can resolve an old reader's dead letter; unsupported
future schemas remain unsupported after retry.

Upgrade every receiving/processing instance and the publishers before enabling v2 writes.
Keep v1 support for retained work and late delivery. Producer rollback can resume v1 while the
compatible reader remains deployed; a reader rollback cannot assume all v2 work has disappeared.
See the [finite executable journey](../MessagingDemo/README.md#schema-compatibility-journey)
and [full compatibility contract](../../docs/schema-compatibility.md).

## Failed work and authorized operator retry

Rendering owns failure policy: invalid/conflicting commands and unclassified errors are dead-lettered
immediately. Recognized PostgreSQL serialization/deadlock failures retry after one second,
stopping at three recorded failures since the last applied operator retry. The library supplies
counters, locking and conditional transitions; classification, limits and codes remain editable
consumer policy. A killed/cancelled attempt may leave no recorded failure; see the
[recovery contract](../../docs/inbox-recovery.md).

If failure accounting itself fails, the library throws InboxFailureAccountingException with
delivery identity and both errors. The optional worker logs a critical diagnostic and faults
without invoking another handler. Register explicitly selects native StopHost supervision:
the entire sample host shuts down, including HTTP endpoints. Repair the database/grant/policy
fault before restarting. Inspect fresh state because an accounting commit response can be lost;
only an actually dead-lettered delivery needs authorized operator retry. A worker fault alone
does not dead-letter the row, and no particular exit code or deployment restart is supplied.

Enable the optional operator routes by setting `INBOX_DEMO_OPERATOR_TOKEN` to a private credential
of at least 32 characters. Without it the routes are absent. Native ASP.NET Core authentication
compares bearer-credential SHA-256 digests in constant time and grants the fixed
`rendering.inbox.retry` permission to `rendering-operator`. Replace this finite demo policy with
your production identity/permission policy; use HTTPS for remote credential-bearing requests.

Inspect and resolve the consumer precondition, then submit both ReceiptId and Revision returned by GET:

```sh
curl -H "Authorization: Bearer $INBOX_DEMO_OPERATOR_TOKEN" \
  "http://localhost:5087/operations/inbox/dead-letters?pageSize=25"
curl -H "Authorization: Bearer $INBOX_DEMO_OPERATOR_TOKEN" \
  "http://localhost:5087/operations/inbox/dead-letters/$MESSAGE_ID"
curl -X POST -H "Authorization: Bearer $INBOX_DEMO_OPERATOR_TOKEN" \
  -H 'Content-Type: application/json' \
  -d "{\"expectedReceiptId\":\"$RECEIPT_ID\",\"expectedRevision\":$REVISION}" \
  "http://localhost:5087/operations/inbox/dead-letters/$MESSAGE_ID/retry"
```

The fixed subscription/producer are `rendering.jobs`/`exports`; callers cannot select another
lane. List GET returns Items/NextCursor; use `after=<NextCursor>` for continuation and optionally
filter an exact `failureCode`. Page size is 1–100 (default 50); malformed query/cursor input
returns 400. Detail GET returns current dead-letter metadata or 404, excluding payload, tenant,
diagnostic context and exception text. Pending/redriven/completed work is absent. Pages observe
live state: retry removes a row and a later failure can put it back. Inspection reserves nothing.
POST returns 204 for applied retry and 409 for stale/inapplicable state. Missing/wrong credentials
return 401 before storage work. Missing/empty receipt tokens return 400. Requests/results log
the fixed principal, delivery ID, receipt generation and revision; durable approval/audit
is a separate consumer choice.

Retry preserves identity, payload, metadata and total failures, resets only the cycle count and
makes the retained row eligible. Processing repeats current admission/business rules; permanent
conflicts can be dead-lettered again. Stale requests cannot reopen that later dead-letter state. After response
loss reuse the same ReceiptId/revision pair, not automatically refreshed tokens. Equivalent
intake deduplicates while its receipt remains. An old request cannot apply to a new receipt
generation after cleanup even if revision values repeat.

Apply `--setup` with old processors stopped before using the updated model. The additive recovery
migration preserves historical pending/completed rows, setting unknown past counts to zero. Old
processors ignore dead-letter state: mixed versions/binary rollback need a separately reviewed plan.
Startup still performs no migration. The additional lookup migration only creates the dead-letter
index; DeadLetteredAt retains the physical `quarantined_at` column and existing row data/grants.

Optional message diagnostic context and native telemetry setup are documented in the
[Messaging observability contract](../../docs/observability.md).

## Processed-inbox cleanup

Choose a retention horizon that accepts possible re-execution after expiry. Receipt cleanup
never removes RenderJob business state: the independent ExportRequestId key still keeps one
local job for equivalent business requirements. Library delivery deduplication itself ends
when the processed receipt is deleted. A changed envelope under that expired delivery key
can also be admitted under current decoding/business rules.

Apply `--setup` using migration authority, upgrade every intake/operator binary and caller,
then separately authorize the finite cleanup command. The new receipt-generation migration
backfills `receipt_id` and adds Rendering's processed-row index; native migration locks apply.
An existing column-restricted operator role needs receipt_id SELECT. No intake/processing/
operator role should receive DELETE just to use this command.

Example dedicated role provisioning, run explicitly by the database owner and adapted to
local credential handling:

```sql
CREATE ROLE rendering_maintenance LOGIN PASSWORD 'replace-with-private-secret'
    NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE NOREPLICATION NOINHERIT;
GRANT USAGE ON SCHEMA rendering TO rendering_maintenance;
GRANT SELECT(subscription_key, producer_key, message_id, receipt_id, processed_at, quarantined_at),
    DELETE ON rendering.incoming_work TO rendering_maintenance;
```

```bash
export INBOX_DEMO_MAINTENANCE_CONNECTION_STRING='Host=localhost;Database=inbox_demo;Username=rendering_maintenance;Password=replace-with-private-secret'
export INBOX_DEMO_PROCESSED_BEFORE='2026-09-01T00:00:00Z' # illustrative; choose your evidence horizon
export INBOX_DEMO_CLEANUP_BATCH_SIZE=100
dotnet run --project src/Gloaming.Messaging/samples/InboxDemo/InboxDemo.csproj -- --cleanup
```

The command needs only these three settings. It authorizes the fixed `rendering.jobs`/`exports`
lane, checks actual maintenance privileges, requests a thirty-second cancellation deadline,
deletes one batch and emits JSON with SubscriptionKey, ProducerKey, ProcessedBefore, BatchSize
and Deleted. It rejects owner/superuser/BYPASSRLS and expanded effective privileges. It starts
no API, intake/processing worker, telemetry loop or migrations. Invoke manually or schedule
through existing consumer infrastructure; no separate permanent worker is required.

Only processed, non-dead-lettered receipts strictly before the explicit UTC-Z cutoff qualify;
batch size is 1–1000. Other lanes and all unprocessed/dead-lettered work are preserved. Native
DELETE locking can wait; repeated calls can delete another batch, and zero during contention
is not global exhaustion. A lost commit response can leave deletion committed. Retention
duration, repetition, scheduling, logs and business idempotency stay consumer-owned.

The sample's minimal table DELETE grant cannot prevent arbitrary SQL by its credential holder
from deleting active rows. Trusted invocation is required; install consumer-native row-security
policies when database-enforced tenant/lane/lifecycle restrictions are needed. The library
respects those policies and bypasses no RLS. See [the full retention/expiry/authority contract](../../docs/inbox-retention.md)
and [new PostgreSQL/process evidence](../../../../docs/reports/m2-inbox-retention.md).
