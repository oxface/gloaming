# ADR 0010: Durable intake and separate local inbox processing

Status: accepted and implemented. The original intake/local-success decision dates to
2026-10-08. [ADR 0018](0018-serialized-inbox-failure-recovery.md) supplies the current
savepoint/failure-accounting protocol; [ADR 0020](0020-inbox-expiry-and-receipt-generations.md)
adds explicit receipt expiry and generation-conditional retry.

Each receiving module owns one typed DbContext, schema/table and explicitly keyed handler
registration. Inbox intake, processing and hosting are independently selected; an inbox-only
consumer needs neither an outbox/publisher nor event sourcing, tenancy or actor packages.
No universal Gloaming transaction/metadata runtime is introduced.

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

Failed local work, including earlier saves, rolls back to the savepoint while the delivery
lock survives. Confirmed failure accounting and the selected retry/dead-letter decision commit
in that transaction. Cancellation/process loss may leave no recorded failure; commit response
loss can be ambiguous. Completed receipts remain until explicit supported retention removes them.
Business constraints/version predicates still protect different deliveries touching the same
aggregate. The handler contract is for trusted local code, not a sandbox against manual commits.

Optional sequential hosting uses fresh scopes and cooperative cancellation. Transport lifecycle,
acknowledgements, setup/migrations, poison policy, retention, redrive, scaling and cross-module
coordination remain explicit or deferred. Native RabbitMQ and HTTP consumers demonstrate acceptance
boundaries; optional native session ownership is separate from transport routing/acknowledgement.
The generator and frozen source archive are retired through Git provenance.

[Library-local contract](../../src/Gloaming.Messaging/docs/inbox.md) and
[dated execution evidence](https://github.com/oxface/modulith-foundry/blob/bd2adb7dea58de304aa2eb3df3c587c6fe348320/docs/reports/inbox1-durable-intake-processing.md) distinguish supported
mechanisms, consumer policy and remaining proof gaps from archived receipt-only evidence.
