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

See [the family contract](../../src/Gloaming.Messaging/docs/capabilities.md) for setup/errors
and independent inbox composition, workers and deferred ordering policy. PostgreSQL is currently the supported provider.

Optional message diagnostic context and native telemetry setup are documented in the
[Messaging observability contract](../../src/Gloaming.Messaging/docs/observability.md).
