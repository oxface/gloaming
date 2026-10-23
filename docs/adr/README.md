# Current architecture decisions

These records explain accepted architecture choices and their consequences. Family-local
contracts define current public interfaces, setup and supported limits.

| Decision | Owning boundary |
| --- | --- |
| [0001 Opaque operation identities](0001-opaque-operation-identities.md) | Consumer-owned keys; independent attribution and tenancy. |
| [0002 Native HTTP actor establishment](0002-explicit-http-actor-establishment.md) | Effective native policy principal and explicit mapping. |
| [0003 Access admission](0003-access-registry-and-operation-admission.md) | Sample registry and operation-time membership policy. |
| [0004 Module Contracts](0004-module-contracts-and-native-composition.md) | Sample business-call isolation and native host composition. |
| [0005 Aggregate write contract](0005-aggregate-write-contract-and-native-append.md) | Package-free bookkeeping; original prepared append surface superseded by 0008. |
| [0006 Required inline aggregate](0006-transactional-main-inline-state.md) | Same-transaction event/header/main-state participation. |
| [0008 Native optimistic rebuilding](0008-native-optimistic-aggregate-rebuilding.md) | Direct staging, header stamps and separate full replay; supersedes 0007. |
| [0009 Transactional outbox](0009-module-owned-transactional-outbox.md) | Caller-owned intent and separately fenced publication. |
| [0010 Durable inbox](0010-durable-inbox-local-processing.md) | Retained intake and bounded local processing; failure path refined by 0018. |
| [0011 Explicit audit](0011-explicit-transactional-audit.md) | Selected accepted-change participation; native provider configuration. |
| [0012 Diagnostic context](0012-durable-diagnostic-context-and-native-telemetry.md) | Retained W3C context and native host telemetry subscriptions. |
| [0014 Durable continuations](0014-bounded-durable-continuations.md) | Saved methods plus atomic request progress, in one optional EF package. |
| [0016 Local reactions](0016-local-domain-event-dispatch.md) | Composable capture, explicit source selection and bounded waves. |
| [0017 Explicit regeneration](0017-explicit-aggregate-regeneration-execution.md) | Deliberately delegated attempt scopes/transactions and narrow retry outcomes. |
| [0018 Inbox failure accounting](0018-serialized-inbox-failure-recovery.md) | Surviving row lock/savepoint, conditional retry and fault supervision. |
| [0019 Outbox dead letters](0019-lease-fenced-outbox-dead-letters.md) | Lease-fenced accounting/completion and operator retry. |
| [0020 Receipt generations](0020-inbox-expiry-and-receipt-generations.md) | Explicit expiry ends evidence; old operator authority cannot match a new receipt. |
| [0021 Native RabbitMQ session](0021-optional-rabbitmq-session-lifecycle.md) | Optional resource/setup/cleanup ownership; consumer transport/restart policy. |
| [0023 Logical command receipts](0023-logical-command-receipts.md) | Explicit full-key serialization/retained results; native save/commit and module-lifetime evidence. |

ADR numbers are stable and are not reused. Gaps preserve earlier decision identities.
Current records explain accepted choices; package-local contracts define supported setup and limits.
