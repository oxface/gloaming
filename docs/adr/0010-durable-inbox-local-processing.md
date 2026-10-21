# ADR 0010: Durable intake and separate local inbox processing

M2's [ADR 0018](0018-serialized-inbox-failure-recovery.md) supersedes the separate retry-write
path below with savepoint rollback, serialized failure accounting and authorized conditional
retry. The original I1 decision/evidence remains historical; retained intake/identity and local
success atomicity are preserved.

Status: interface and scope owner-approved, 2026-10-08. Implementation remains unstaged for
line-by-line review. [Reviewed scope](../plans/inbox1-durable-intake-processing.md).

Each receiving module owns one typed DbContext, schema/table and explicitly keyed handler
registration. Inbox intake, processing and hosting are independently selected; an inbox-only
consumer needs neither an outbox/publisher nor event sourcing, tenancy or actor packages.
No new package or universal Gloaming transaction/metadata runtime is introduced.

Retain the complete admitted incoming envelope in a caller-owned native ReadCommitted
transaction. Only its committed intake permits transport acknowledgement or successful HTTP
acceptance. Deduplication uses subscription/producer/message ID and compares retained named
schema, JSONB payload and tenant/correlation/causation metadata, including completed rows.
Incompatible identity reuse throws explicitly. Producer and tenant metadata do not authorize
access; trusted transport mapping, admission and decoding remain consumer policy.

Callable processing uses a fresh context and owns one native ReadCommitted transaction.
FOR UPDATE SKIP LOCKED holds a selected delivery through bounded local handler work, save,
completion and commit. Other processors skip that row. Local business effects and optional
outgoing work share the transaction; external effects belong in the outbox. Native row locks
release on connection loss, so no inbox lease, renewal or stale-token API is needed. Outbox
publication retains ADR 0009's lease because transport acceptance occurs outside its transaction.

Failed local work rolls back, including earlier saves. A separate conditional database-time
retry update defers only pending work and cannot undo successor completion. Cancellation does
not force this update; commit response loss can be ambiguous. Completed rows remain retained.
Business constraints/version predicates still protect different deliveries touching the same
aggregate. The handler contract is for trusted local code, not a sandbox against manual commits.

Optional sequential hosting uses fresh scopes and cooperative cancellation. Transport lifecycle,
acknowledgements, setup/migrations, poison policy, retention, redrive, scaling and cross-module
coordination remain explicit or deferred. Native RabbitMQ and HTTP samples demonstrate acceptance
boundaries without moving transport code into Gloaming. T1 and the frozen archive are preserved.

[Library-local contract](../../src/Gloaming.Messaging/docs/inbox.md) and
[new execution evidence](../reports/inbox1-durable-intake-processing.md) distinguish supported
mechanisms, consumer policy and remaining proof gaps from archived receipt-only evidence.
