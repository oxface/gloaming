# Native EF messaging composition

References Messaging, Gloaming.EntityFrameworkCore and native EF Core Relational, DI, Hosting and Logging abstractions.
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

Optionally register `AddOutboxWorker<TDbContext>(new OutboxWorkerOptions(idleDelay,failureDelay))`
after a concrete dispatcher and native logging registration. It starts no migrations and
discovers no modules. Each sequential invocation gets a fresh async scope, disposed before
waiting. Dependency/model/provider failures during resolution fail startup/execution;
publication/storage failures are logged and retried with failureDelay. IdleDelay applies on
NoWork; both delays must be positive and within native Task.Delay bounds. Cooperative host
cancellation stops new work and reaches the publisher. A non-cooperative publisher can still
hold shutdown; leases do not forcibly terminate code. Callable dispatch requires no worker.

Inbox composition is independent: IInbox<TDbContext> retains delivery in a caller-owned
native transaction; IInboxProcessor<TDbContext> owns the separate processing save/commit.
Optional subscription-keyed IInboxFailurePolicy selects delayed retry or quarantine using
durable confirmed-failure counts. Independently registered IInboxRecovery supplies one
revision-conditional retry; caller authorization remains consumer wiring.
AddInboxHandler<TDbContext,THandler>(subscriptionKey) binds a consumer-owned scoped handler
through native keyed DI. The handler stages bounded local effects on that context, including
optional outbox work. It does not commit or perform external effects. No public lease or
prepared-message handle is introduced. AddInboxWorker<TDbContext>(subscriptionKey,options)
is separately selected and uses fresh scopes, native logging and cooperative cancellation.
An InboxFailureAccountingException retains delivery identity and both original errors when
failure accounting is unconfirmed. It faults the optional inbox worker without another poll;
native host supervision decides whether the host stops. The library installs no host-wide
setting. Ordinary recorded handler failures still use the worker's failure delay.

ConfigureInbox supplies relational mapping, specialized by ConfigurePostgresInbox for the
supported runtime. Call ValidateInboxChanges in both save overrides: tracked inserts/edits/
deletes of provided records reject. Native intake/completion owns lifecycle SQL; no inbox weak
registry or transaction field is needed. DbSet properties are optional.

Keep domain eligibility, whole-operation retry, tenant admission and deployment policy with
the consumer. Producer/intake final save/commit remain explicit; the processor owns its bounded
local processing transaction. See [inbox setup](../docs/inbox.md),
[failure/recovery obligations](../docs/inbox-recovery.md) and
[complete obligations](../docs/capabilities.md).

See [durable message observability](../docs/observability.md) for optional W3C context, schema upgrades, native
activity/meter names, failure logs and host-owned exporter configuration.
