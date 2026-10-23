# Inbox expiry and receipt generations

Status: accepted and implemented. Owner-approved checkpoint `6510f9e`, merged as `2e1ffac`
on 2026-10-22, includes the five documented supplied-feature deferrals.

Processed inbox cleanup removes duplicate and incompatible-content evidence, so a later
delivery with the same key can execute again. Use a new immutable ReceiptId for each intake
generation and require it alongside revision in the existing operator retry interface:
revision alone can repeat after deletion and authorize an old request against new work.
Keep wire MessageId and the delivery primary key unchanged. UUID defaults avoid a permanent
tombstone store or a sequence shared by every processing transition. This adds a forward
schema migration and a pre-v1 caller change; no revision-only retry overload remains.

Unprocessed and dead-lettered work is preserved. Retention duration, scheduling and business
idempotency remain consumer-owned. See the [reviewed scope](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/m2-inbox-retention.md)
and [current family contract](../../src/Gloaming.Messaging/docs/inbox-retention.md).
