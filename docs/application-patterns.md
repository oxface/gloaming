# Consumer-owned application patterns

Gloaming supplies selected library mechanisms. The application owns its project graph,
identity admission, module boundaries, transactions, transport routing and deployment.
These patterns describe existing executable usage; there is no repository generator or
mandatory application layout. Task skills can follow repeated real consumer work later.

## Compose the application

Start with the native .NET host the application needs. A server can compose several modules
in one process. Add independent workers when their lifecycle or credentials differ; keep
migration/provisioning as an explicitly invoked finite operation. Aspire is optional local
orchestration, not a prerequisite for using the libraries. The existing
[Wholesale host](../samples/Wholesale/Api/README.md) and
[delivery example](../src/Gloaming.Messaging/samples/Delivery/README.md) show concrete compositions.

Keep each module's domain rules and persistence in its implementation. Use consumer-owned
Contracts for deliberate inter-module operations; keep implementation contexts, migrations
and business DTOs out of Gloaming. A module owns its schema and migration history even when
modules share one PostgreSQL database. Avoid a common application DbContext or a transaction
that silently spans module operations. Controllers versus minimal APIs is an application choice.

## Establish each operation

Native authentication establishes identity; the consumer maps it into an actor and admits
the selected tenant. OIDC providers, account linking, membership and business permissions
remain consumer policy. Context values attribute an operation; they do not authorize it.

Use one native DI scope for one operation. Establish every required segment before invoking
capabilities, and dispose the scope on completion, failure or cancellation. Independent worker
turns receive fresh scopes. Follow the exercised [actor/tenant composition recipe](../src/Gloaming.Tenancy/docs/composition.md)
and the selected [HTTP adapters](../src/Gloaming.Tenancy/README.md) for ingress ordering.

## Select persistence per module

For ordinary relational state, use a native DbContext. Select ownership filtering and protected
writes explicitly; query filters alone do not protect detached updates or raw SQL. The
[ownership consumer](../src/Gloaming.Persistence/samples/PersistenceDemo/README.md) demonstrates
cached models, stored-owner checks, same-tenant relationships and caller-owned commits.
Optional [PostgreSQL RLS](../src/Gloaming.Persistence/Gloaming.Persistence.EntityFrameworkCore.Postgres/README.md)
also requires the consumer's policies, restricted credentials and transaction discipline.

Choose [event sourcing](../src/Gloaming.EventSourcing/README.md) for the module whose durable
history needs it. Explicit serialization/history utilities and local reactions remain separately
selectable. Keep native transaction, save and commit ownership visible; select a library-owned
bounded execution operation only under its documented guarantees. There is no repository-wide
persistence mode or required CQRS dispatcher.

## Add durable handoff when needed

Select [outbox/inbox roles](../src/Gloaming.Messaging/README.md) where a module needs durable
delivery. The sender saves its business change and outgoing envelope together. The receiver
retains intake before transport acknowledgement, then processes local effects transactionally.
The consumer owns wire identities, admission, routing, retry classification and external side
effects; a delivery can repeat. Select the optional polling host separately from callable storage.

The [bounded continuation package](../src/Gloaming.Messaging/Gloaming.Messaging.Workflows.EntityFrameworkCore/README.md)
can persist local request/reply progress. It is not a human workflow engine. Longer ERP business
processes, deadlines, revocation policy and compensation still need a concrete consumer design.

## Consume and verify

Use the [local NuGet feed workflow](development.md#local-package-development) during rapid
library changes. It packs unique versions for ordinary package consumers, without copying
library sources or requiring remote publication. Select only the packages the application uses.

Test module policy in that application's tests. Keep reusable mechanism proofs with their
owning family; use independently authored payloads for retained-data compatibility. Native
process tests and package-only consumers prove different obligations. The old Catalog generator
and in-memory SKU preview are retired; their dated evidence remains in Git through the
[current audit](reports/gloaming-library-audit.md).
