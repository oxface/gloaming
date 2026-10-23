# Library and application design

Gloaming is the primary product: independently selectable .NET libraries for native APIs
and workers. Consumers retain application composition and business authority. This guide
records cross-family ownership; each family's README and capability records define its
current public contracts, setup, limitations and deferred work.

See [library design conventions](conventions/library-design.md) for new capabilities,
[architecture decisions](adr/README.md) for accepted trade-offs, and the
[test map](../tests/README.md) for executable proof owners.
[Modular monolith patterns](../modular-monolith-patterns/README.md) describe how a consuming
application applies these boundaries.

## Library boundaries

A library should remove meaningful technical complexity after accounting for registration,
configuration, failure handling and support cost. An explicitly selected operation may own
bounded execution, including a transaction and consumer callback, when its guarantees and
consumer obligations are clear. There is no required root runtime, automatic module discovery,
mediator, generic repository or workflow language.

Consumers own domain rules, module Contracts, tenant admission, resource permissions,
payload meaning, schemas/migrations, transport routes, deployment and operational policy.
A fixed technical table or provider-specific protocol can be a useful library mechanism;
it must state its supported model rather than promise arbitrary storage/provider authority.

| Concern | Current contract and boundary |
| --- | --- |
| Actor and tenant context | [ActorIdentity](../src/Gloaming.ActorIdentity/README.md) and [Tenancy](../src/Gloaming.Tenancy/README.md) are independent. Context attributes work; it grants no authority. Authentication, identity mapping, admission and permissions stay consumer-owned. |
| HTTP establishment | The actor adapter wraps native policy evaluation after scheme-specific authentication. Tenancy resolves after native authorization and actor completion, before endpoint work. Native authorization handlers cannot assume established tenancy. See [actor setup](../src/Gloaming.ActorIdentity/Gloaming.ActorIdentity.AspNetCore/README.md) and [tenancy setup](../src/Gloaming.Tenancy/Gloaming.Tenancy.AspNetCore/README.md). |
| Persistence | [Ownership filtering and protected saves](../src/Gloaming.Persistence/docs/capabilities.md) cover the documented ordinary single-table model. Same-tenant relationships and business concurrency remain native consumer mappings. Optional PostgreSQL RLS also requires deployed policies, restricted credentials and a caller-owned transaction for protected reads and writes. |
| Save preparation | [EF capability composition](../src/Gloaming.EntityFrameworkCore/README.md) orders selected preparation/change detection/validation through native saving. Consumers bind each context and cover sync/async saves; no middleware silently commits. Raw SQL/bulk paths need their own enforcement. |
| Logical command receipts | [CommandReceipts](../src/Gloaming.CommandReceipts/README.md) serializes cooperating attempts and stages immutable results in caller-owned EF/PostgreSQL transactions. The caller owns admission, logical identity, comparison/reader semantics and schema placement. Retain for module lifetime; explicit removal ends protection. No serializer, handler runtime or automatic expiry is selected. |
| Local facts and reactions | [EventReactions](../src/Gloaming.EventReactions/docs/local-dispatch.md) provides explicit sources, ordered bounded waves and typed reactions. The caller owns the transaction and save/commit; complex dependency ordering belongs in explicit application orchestration. Replay emits no historical external effects. |
| Event payloads and history | [Events](../src/Gloaming.Events/docs/capabilities.md) supplies independent JSON identity/upcasting and range validation. Consumers own payload semantics, queries, temporal selection and reader-before-writer rollout. Range integrity alone does not certify excluded rows or a database snapshot. |
| Event-sourced writes and repair | [EventSourcing](../src/Gloaming.EventSourcing/docs/capabilities.md) stages append plus one required inline aggregate. Ordinary loads never repair. Full replay is separate explicit maintenance, with optimistic native stamps invalidating stale decisions. Secondary/async/multi-stream projections remain deferred. |
| Accepted-change audit | [Auditing](../src/Gloaming.Auditing/docs/transactional-audit.md) stages explicitly selected attribution and JSON in the owning transaction. Business selection, action vocabulary, visibility and retention remain consumer policy. Native PostgreSQL JSONB configuration needs no separate adapter package. |
| Durable delivery | [Messaging](../src/Gloaming.Messaging/docs/capabilities.md) separates retained intake, bounded local processing and external publication. Consumer transport adapters acknowledge after intake commit. Outbox lease fencing protects database completion; it cannot ensure external exactly-once effects. |
| Durable continuations | The optional [combined EF package](../src/Gloaming.Messaging/docs/workflows.md) retains input/progress, invokes the saved continuation and stages one next request atomically. Consumer methods own branching and compensation; admission, save/commit and delivery remain explicit. No human-assignment model, scheduler or general replay engine is supplied. |

## Operation and transaction ownership

Actor, initiator and tenant remain immutable throughout one operation. Anonymous and tenantless
execution are deliberate independent choices, distinct from missing context. Keys are opaque,
compared ordinally and canonicalized by the consumer. Context segments publish independently;
the host finishes all required establishment before invoking a capability and disposes the
scope if establishment fails. A worker establishes a fresh scope per operation.
See [ADR 0001](adr/0001-opaque-operation-identities.md) and the
[native composition recipe](../src/Gloaming.Tenancy/docs/composition.md).

Most operations stage work for caller-owned save/commit. The separately invoked inbox
processor deliberately owns its bounded row-lock/savepoint/handler/completion transaction;
the optional aggregate regenerator owns fresh attempt scopes and native transactions.
These selected protocols do not introduce a universal unit of work. Follow their exact
rollback, cancellation, uncertain-commit and cleanup contracts:
[inbox](../src/Gloaming.Messaging/docs/inbox.md),
[failure recovery](../src/Gloaming.Messaging/docs/inbox-recovery.md) and
[regeneration](../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/README.md#complete-regeneration-operations).

Native transaction ownership includes failure recovery: dispose failed state and retry a
fresh decision only where supported. Independent module commits and compensating messages
provide different guarantees from a shared database transaction. Cross-context shared
transaction composition needs its own proof; in-process calls alone do not survive process death.

## Dependencies and optional execution

Select only the documented package closure. ActorIdentity and Tenancy cores are independent
and package-free. Persistence ownership requires neither context family. Event sourcing needs
no messaging; state-stored modules need no event-sourcing types. Technical libraries reference
no sample Contracts. PostgreSQL and RabbitMQ guarantees have real provider proofs; those
results do not certify other providers or a transport matrix.

Callable messaging persistence/delivery remains usable without Hosting abstractions. The
explicit [hosting package](../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Hosting/README.md)
adds sequential polling, fresh scopes and fault supervision. Consumers may write native
workers around the same operations. The optional
[RabbitMQ session package](../src/Gloaming.Messaging/Gloaming.Messaging.RabbitMQ/README.md)
owns native acquired resources and setup/cleanup phases; transport topology, acceptance,
terminal-session recovery and external process restart remain consumer choices.

Libraries expose native logging, activities and meters. Hosts own SDK/exporter configuration,
sampling, readiness and alert policy; subscribe with native APIs. Aspire/AppHost and
ServiceDefaults belong to consumers and test composition, not the library runtime.
See [messaging observability](../src/Gloaming.Messaging/docs/observability.md).

## Samples, adoption and evidence

[Wholesale](../samples/Wholesale/README.md) is a three-module reference application: Access,
Sales and Inventory. Each module owns its implementation, Contracts and persistence. Its
Organization/user/membership lifecycle, draft permissions, stock rules, RLS roles and
operational commands are sample policy. They do not prescribe every adopter's project graph.
See [ADR 0003](adr/0003-access-registry-and-operation-admission.md) and
[ADR 0004](adr/0004-module-contracts-and-native-composition.md).

Family examples demonstrate focused public-interface adoption. Fault-injection hosts and
separately compiled compatibility consumers are test-owned. Preserve literal payloads and
migrations where a supported compatibility guarantee depends on them.

[Modular monolith patterns](../modular-monolith-patterns/README.md) provide document-driven
application initialization and module tasks. They define a preferred consumer layout without
adding a mandatory library runtime or configurable source generator.

The [local package workflow](../tools/release/README.md) supports ordinary NuGet consumption
during development. Package-only, provider, process and runtime tests verify different
boundaries; see the owning test commands and contracts.
