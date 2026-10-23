# Inbox expiry and receipt generations

Status: accepted and implemented, 2026-10-22.

Processed inbox cleanup removes duplicate and incompatible-content evidence, so a later
delivery with the same key can execute again. Use a new immutable ReceiptId for each intake
generation and require it alongside revision in the existing operator retry interface:
revision alone can repeat after deletion and authorize an old request against new work.
Keep wire MessageId and the delivery primary key unchanged. UUID defaults avoid a permanent
tombstone store or a sequence shared by every processing transition. This adds a forward
schema migration and a pre-v1 caller change; no revision-only retry overload remains.

Unprocessed and dead-lettered work is preserved. Retention duration, scheduling and business
idempotency remain consumer-owned. See the [current contract](../../src/Gloaming.Messaging/docs/inbox-retention.md).
