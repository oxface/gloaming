# Proof ownership

The 30 native test projects protect Gloaming contracts, independent adoption or the retained
Wholesale reference application. Family tests live beside their libraries; root tests cover
cross-family composition and repository architecture. Process hosts and Earlier/Current binaries
under test fixtures are intentional consumers, not additional public sample applications.

## Maintained suites

| Owner | Test projects | Obligation |
| --- | --- | --- |
| ActorIdentity | [Core](../src/Gloaming.ActorIdentity/tests/ActorIdentityTests/ActorIdentityTests.csproj), [HTTP](../src/Gloaming.ActorIdentity/tests/ActorIdentityAspNetCoreTests/ActorIdentityAspNetCoreTests.csproj) | Opaque identity/attribution and immutable establishment; effective native policy principal, schemes, challenges and anonymous paths. |
| Tenancy | [Core](../src/Gloaming.Tenancy/tests/TenantTests/TenantTests.csproj), [HTTP](../src/Gloaming.Tenancy/tests/TenancyAspNetCoreTests/TenancyAspNetCoreTests.csproj), [PostgreSQL](../src/Gloaming.Tenancy/tests/TenancyPostgresTests/TenancyPostgresTests.csproj) | Tenantless/missing distinction, independent admission/order, native transaction-local context and cleanup. |
| Persistence | [Model/write guards](../src/Gloaming.Persistence/tests/EntityFrameworkCoreTests/EntityFrameworkCoreTests.csproj), [GUID adopter](../src/Gloaming.Persistence/tests/PersistenceTests/PersistenceTests.csproj), [RLS](../src/Gloaming.Persistence/tests/PostgresTests/PostgresTests.csproj), [module adopter](../src/Gloaming.Persistence/tests/OwnershipAdoptionTests/PersistenceDemo.Tests.csproj) | Supported/rejected mappings, stored-owner predicates, detached writes, native policy/grants/lifecycle, independent migrations and transactions. |
| Shared EF and EventReactions | [Reactions](../src/Gloaming.EventReactions/tests/EventReactionsTests/EventReactionsTests.csproj), [native EF adopter](../src/Gloaming.EventReactions/tests/AdoptionTests/Consumer.Tests.csproj) | Ordered bounded capture, explicit source selection, immutable/package-free models, context-specific capability wiring and failure/save ownership. |
| Events | [Codec](../src/Gloaming.Events/tests/EventSerializationTests/EventSerializationTests.csproj), [history](../src/Gloaming.Events/tests/EventHistoryTests/EventHistoryTests.csproj), [adopter/compatibility](../src/Gloaming.Events/tests/CodecAdoptionTests/EventCodecDemo.Tests.csproj) | Durable names/schema validation, forward decoding, range integrity, literal historical JSON and actual executable usage. |
| EventSourcing | [Aggregate](../src/Gloaming.EventSourcing/tests/EventSourcingTests/EventSourcingTests.csproj), [PostgreSQL](../src/Gloaming.EventSourcing/tests/EventSourcingPostgresTests/EventSourcingPostgresTests.csproj), [storage adopter](../src/Gloaming.EventSourcing/tests/StorageAdoptionTests/EventStorageDemo.Tests.csproj) | Candidate bookkeeping, append/state atomicity, native concurrency, complete-key history/rebuild/regeneration and alternative consumer mappings. |
| Auditing | [Envelope/guard/transaction](../src/Gloaming.Auditing/tests/AuditPostgresTests/AuditPostgresTests.csproj) | Explicit trusted attribution, owned JSON, context/transaction association and native rollback. Business audit selection remains consumer policy. |
| Messaging delivery | [Core](../src/Gloaming.Messaging/tests/MessagingTests/MessagingTests.csproj), [outbox](../src/Gloaming.Messaging/tests/OutboxPostgresTests/OutboxPostgresTests.csproj), [inbox](../src/Gloaming.Messaging/tests/InboxPostgresTests/InboxPostgresTests.csproj), [RabbitMQ](../src/Gloaming.Messaging/tests/RabbitMqTests/RabbitMqTests.csproj), [delivery adoption/process](../src/Gloaming.Messaging/tests/DeliveryTests/DeliveryTests.csproj) | Retained identities, claim/fencing or row-lock/savepoint protocols, accounting, retention, authority, native session recovery and real process-death/shutdown/telemetry behavior. |
| Continuations | [Definitions](../src/Gloaming.Messaging/tests/WorkflowTests/WorkflowTests.csproj), [PostgreSQL turns](../src/Gloaming.Messaging/tests/WorkflowPostgresTests/WorkflowPostgresTests.csproj), [compiled deployments](../src/Gloaming.Messaging/tests/WorkflowDeploymentTests/WorkflowDeploymentTests.csproj) | Saved-continuation selection, retained input/state, duplicate outcomes, competing turns, atomic next requests and old/new deployment compatibility. |
| Cross-family composition | [Native DI](ContextCompositionTests/ContextCompositionTests.csproj), [architecture](ArchitectureTests/ArchitectureTests.csproj) | Independent context lifetimes/establishment; deliberate library dependency boundaries, module Contracts, consumer composition and sample schema/migration policy. |
| Wholesale | [Integration](../samples/Wholesale/IntegrationTests/Wholesale.IntegrationTests.csproj), [runtime/browser](../samples/Wholesale/RuntimeComposition.Tests/RuntimeComposition.Tests.csproj) | Actual Access/Sales/Inventory authorization, domain decisions, transactions, migrations, historical events and durable handoffs; Aspire/OIDC/HTTPS/readiness with native processes and browser. |

The [CI workflow](../.github/workflows/ci.yml) runs family/provider/adoption suites and Wholesale
integration; [runtime CI](../.github/workflows/sample-runtime.yml) owns browser/topology proofs.
The [explicit path map](../.github/ci-paths.yml) selects dependent lanes. Command presence is
not proof of scheduling or passing results; manual full selection remains the integration gate.
The [development guide](../docs/development.md) documents native local/container prerequisites.

## Keep proofs for distinct boundaries

Unit, provider, package-only and process tests can exercise the same API while protecting
different failures. A fake publisher cannot prove broker acceptance uncertainty; an in-process
rollback cannot prove abrupt process recovery; ProjectReference builds cannot prove the
[six package-only profiles](../tools/release/README.md#full-package-adoption-proof).
Historic literals and populated migration fixtures need independent expected outcomes and
owned provenance, not values manufactured by the implementation under test.

Architecture uses real compiled types plus small native XML declarations. Library declarations
protect independently selectable dependencies even when a new reference is unused by code.
Provider test projects do not need exact xUnit/Testcontainers/package-list snapshots; their
executable adoption remains. Removing those snapshots gives up policing unused references in
those test projects, not library dependency or runtime isolation guarantees. Declaration checks
do not evaluate every MSBuild import/condition; ordinary package adoption is a separate proof.

Keep a test when it catches a supported library/integration mistake or retained sample behavior.
Prune framework-only demonstrations, private implementation mirrors and duplicate fixture
assertions after tracing their obligation to an active proof. Sample business rules do not
become Gloaming guarantees. No test-count target or framework feature-parity list decides scope.

## Repository tooling

[Reference-checker tests](../tools/repository/references.test.ts) exercise valid source links,
missing/deleted/untracked targets, ignored build output, literal linked fixtures and CLI status.
They run without .NET or containers. They do not replace native restore/build, package README
rendering, provider/runtime proofs or independent code/architecture review.
