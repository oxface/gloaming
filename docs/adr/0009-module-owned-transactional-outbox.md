# ADR 0009: Module-owned transactional outbox and independent dispatch

Status: accepted and implemented, 2026-10-08.

Each adopting module owns its typed native DbContext, schema/table, business contracts and
transaction. Explicit enqueue joins that context's existing transaction; business changes,
events/required aggregate state where applicable, and outgoing work share the caller's
save/commit. No universal Gloaming unit of work or mandatory context/event dependency is added.
Different modules receive/commit independently, preserving a future service boundary without
assuming a distributed or shared cross-module transaction.

The Messaging family has a provider-free envelope/publisher contract, used EF record/mapping/
enqueue/save/worker code, and PostgreSQL-specific model/claim/completion SQL. PostgreSQL uses
JSONB and its own clock/lease semantics. The EF layer is not a promise of supported dispatch
on another DBMS; no public SQL dialect or lease repository is introduced. Transport implementations,
routes, confirmations and receiving acknowledgements remain consumer source.

Callable dispatch owns short native ReadCommitted transactions. Commit a token/expiry claim
before publication, publish without a database transaction, and fence completion/retry with
the same unexpired token. External acceptance cannot be atomically committed with the completion
write. Delivery may repeat with stable identity; receiver idempotency remains necessary.
The optional sequential hosted worker invokes this same operation in fresh scopes and does
not apply migrations, activate producer publication or discover modules.

Durable intake is independently selected. Retain a complete envelope, commit intake,
acknowledge transport, then later commit local effects, completion and outgoing work together.
[ADR 0010](0010-durable-inbox-local-processing.md) records that protocol. MessageId identifies
delivery; correlation groups related work and causation identifies immediate cause.
Delivery deduplication and logical business-action identity have different lifetimes.

See the [library-local contract](../../src/Gloaming.Messaging/docs/capabilities.md) for independent enqueue, dispatch and intake composition.
