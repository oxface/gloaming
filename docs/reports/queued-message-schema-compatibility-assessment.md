# Queued-message schema compatibility assessment

Date: 2026-10-21. Fetched origin/main `86331de` contains squash-merged PR #41, including the
owner's CI-fix checkpoint `6cde7b7`. Its complete tree matches the previous topic tip. The
checkout and index were clean before creating feat/queued-message-schema-compatibility from
that fetched base without tracking origin/main. Earlier outbox/CI tests remain historical
evidence for this assessment; no new runtime proof was executed in this preparation.

## Findings from current callers

- ExportRequestCommands writes an explicit exports.render alias/schema 1 and PascalCase
  ExportRequestId/Pages JSON. The receiver owns a separate CLR DTO and RenderJob identity.
- HttpCommandPublisher and RabbitMqExportPublisher both reject schema values other than 1.
  WorkerPublisher delegates to the latter. Upgrading only the processor would leave dispatch
  rejecting new versions; publishers belong in the reader-before-writer rollout.
- Both HTTP intake and RabbitMqRenderReceiver call RenderExportHandler.Validate before
  retaining the original message. HandleAsync calls Decode again. These are distinct admission
  and retained-processing obligations; operator Retry grants neither compatibility nor authority.
- Inbox duplicate comparison includes exact message name/schema and JSONB payload, plus business
  metadata. Replacing a v1 incoming envelope with a transformed v2 envelope before ReceiveAsync
  would change identity-reuse behavior. Transform only the in-memory decoding input/result.
- JsonEventCodec already implements explicit name/version registrations and pure forward JSON
  transformations without EF, event-store, transport or DI dependencies. Those semantics match
  receiver-local historical payload decoding. Its EventDecodingException needs deliberate
  translation at the sample's current InvalidDataException admission/classification boundary.
- The existing outbox negative proof OperatorRetryCannotBypassCurrentPublisherContractValidation
  uses schema 2 as unsupported. If 2 becomes supported, preserve that negative outcome using a
  declared unsupported future schema rather than weakening validation.

## Concrete recommendation and review scope

The [proposal](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/queued-message-schema-compatibility.md) selects a small consumer wire
change: schema 2 renames Pages to PageCount while preserving business meaning. Keep default
v1 writes, opt into v2 explicitly, and retain v1 support through the optional existing codec.
Proposed constructor/DTO usage, finite executable adoption, file/dependency scope and native
PostgreSQL/HTTP/RabbitMQ/process proof obligations are recorded there for owner review.

This is a candidate consumer composition and rollout proof. The codec is a reuse candidate;
no new reusable mechanism was proven. Messaging interfaces/storage, schema migrations,
native grants, worker configuration and template output need no proposed change. Classification,
admission, writer selection, deployment coordination and operator authority remain consumer-owned.

Library finding: existing envelope, decoder and conditional recovery seams appear sufficient.
Sample finding: explicit v1 gates exist in both publication and receiving paths. Template finding:
this assessment supplies no new setup pattern or template file. Implementation must establish
durable compatibility and failure behavior before updating supported-capability claims.

Preparation checks cover base/tree identity, the originally empty review index, changed Markdown
whitespace and local links. New runtime checks, actual rollout/rollback behavior and dependency
adoption remain proposed. Inbox retention/deduplication expiry, payload editing, completed-message
replay, downcasting, generic schema negotiation and a new migration/recovery framework remain
outside this outcome.
