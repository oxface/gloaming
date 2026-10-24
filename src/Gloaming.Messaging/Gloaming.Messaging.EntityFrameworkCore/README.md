# Native EF messaging composition

References Messaging, Gloaming.EntityFrameworkCore and native EF Core Relational and DI abstractions.
Optional workers live in [Gloaming.Messaging.EntityFrameworkCore.Hosting](../Gloaming.Messaging.EntityFrameworkCore.Hosting/README.md).
No Npgsql, broker, Events, EventSourcing, Persistence or Tenancy dependency.

For [shared save composition](../../Gloaming.EntityFrameworkCore/README.md), ConfigureOutbox/ConfigureInbox
declare their per-context model requirements. Register AddOutboxValidation<TDbContext>() and/or
AddInboxValidation<TDbContext>(), then install ConfigureGloamingCapabilities<TDbContext>(provider)
in scoped native options. Existing validation runs after preparation. Producer/processor/worker
registrations remain separate; this does not enable delivery. Explicit save overrides below remain
a supported alternative. Choose one integration for each validator.

`IOutbox<TDbContext>`/`EfOutbox<TDbContext>` enqueue a provided `OutboxMessageRecord` in the
caller's explicit native transaction. They do not save, commit, dispatch or open a producer
transaction. DbSet declarations are optional; use native Set<OutboxMessageRecord>().

`ConfigureOutbox(model,schema,table)` supplies the relational shape; provider configuration
must specialize payload/time storage. Use [ConfigurePostgresOutbox](../Gloaming.Messaging.EntityFrameworkCore.Postgres/README.md)
for the currently supported runtime. No second provider is certified by this base mapping.

Call `ValidateOutboxChanges()` before base in both SaveChanges(bool) and
SaveChangesAsync(bool,CancellationToken) overrides. It checks that added rows came from enqueue
in the same native transaction and rejects envelope/lifecycle mutation or deletion through
tracked EF writes. Technical initial timestamps are database-generated. No base context or
implicit interceptor installs these obligations without the explicit shared configuration. Omitted guards, bulk/raw SQL and external
writers are outside this contract.

`IOutboxDispatcher<TDbContext>` supplies one separately callable dispatch operation.
NoWork means no eligible row was claimed at that moment; Published means acceptance plus
guarded completion; ClaimLost can follow successful acceptance and permits later delivery.

Callable dispatch requires no worker or Hosting dependency. Hosts can supply their own loop
or select the [optional hosting package](../Gloaming.Messaging.EntityFrameworkCore.Hosting/README.md)
for polling, fresh scopes, cancellation and supervision.

Optional IOutboxFailurePolicy selects delayed retry or dead-letter state using durable
publisher-exception observations. Attempts remains committed claims; failures do not prove
transport rejection. Independently registered IOutboxDeadLetters concentrates bounded metadata
List/Get and revision-conditional retry of the unchanged envelope. OutboxFailureAccountingException
preserves both errors. OutboxCompletionException identifies unconfirmed completion after publication
returns normally, retaining MessageId and the original InnerException, including completion-stage
cancellation. Both faults stop the optional worker without another poll; native host supervision
governs shutdown. Requested shutdown ends the worker normally. Completion faults never trigger
publication-failure accounting and may follow an actual commit. See
[outbox recovery, authority and rollout](../docs/outbox-recovery.md).

OutboxBacklogSnapshot is detached aggregate metadata: pending/eligible/delayed/leased and
dead-letter counts, database observation time and nullable oldest eligible age. The PostgreSQL
package supplies its callable read independently of dispatch/recovery. Health, sampling,
instrument names and alert thresholds stay consumer wiring; see
[backlog and worker health](../docs/outbox-observability.md).

Inbox composition is independent: IInbox<TDbContext> retains delivery in a caller-owned
native transaction; IInboxProcessor<TDbContext> owns the separate processing save/commit.
Optional subscription-keyed IInboxFailurePolicy selects delayed retry or dead-letter state using
durable confirmed-failure counts. Independently registered IInboxDeadLetters supplies bounded
metadata list/detail and receipt-generation/revision-conditional retry; caller authorization
remains consumer wiring. ReceiptId distinguishes a retained receipt from later intake after
deletion. No revision-only retry overload is retained.
AddInboxHandler<TDbContext,THandler>(subscriptionKey) binds a consumer-owned scoped handler
through native keyed DI. The handler stages bounded local effects on that context, including
optional outbox work. It does not commit or perform external effects. No public lease or
prepared-message handle is introduced. AddInboxWorker<TDbContext>(subscriptionKey,options)
is selected from the optional hosting package and uses fresh scopes, native logging and cooperative cancellation.
An InboxFailureAccountingException retains delivery identity and both original errors when
failure accounting is unconfirmed. It faults the optional inbox worker without another poll;
native host supervision decides whether the host stops. The library installs no host-wide
setting. Ordinary recorded handler failures still use the worker's failure delay.

ConfigureInbox supplies relational mapping, specialized by ConfigurePostgresInbox for the
supported runtime. Call ValidateInboxChanges in both save overrides: tracked inserts/edits/
deletes of provided records reject. Native intake/completion owns lifecycle SQL; no inbox weak
registry or transaction field is needed. DbSet properties are optional.

The PostgreSQL package separately supplies [finite processed-receipt cleanup](../docs/inbox-retention.md).
Only processed work qualifies. Deletion ends envelope/deduplication evidence and permits old
delivery to execute again; consumer semantic idempotency can outlive the receipt.

Keep domain eligibility, whole-operation retry, tenant admission and deployment policy with
the consumer. Producer/intake final save/commit remain explicit; the processor owns its bounded
local processing transaction. See [inbox setup](../docs/inbox.md),
[failure/recovery obligations](../docs/inbox-recovery.md) and
[complete obligations](../docs/capabilities.md).

See [durable message observability](../docs/observability.md) for optional W3C context, schema upgrades, native
activity/meter names, failure logs and host-owned exporter configuration.
