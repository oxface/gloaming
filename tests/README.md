# Proof ownership

The **32 native test projects** comprise 28 family-local Gloaming suites, two root cross-family
suites and two Wholesale suites. Family tests live in `src/Gloaming.*/tests`; root `tests` covers
cross-family composition and repository architecture. This guide is their common entry point,
and the solution includes every native test project. Process hosts and Earlier/Current binaries
under test fixtures are intentional consumers, not additional public sample applications.

## Run a selected suite

From the repository root, restore/build the solution, then select a project from the table below:

```sh
dotnet build Gloaming.slnx
dotnet test --project src/Gloaming.EntityFrameworkCore/tests/CapabilityTests/CapabilityTests.csproj --no-build --no-restore
```

The coordinator suite uses native EF/Npgsql model-only probes without opening a database.
Core identity, codec, history and reaction suites also need no running infrastructure. Provider,
adoption and process suites use the real services required by their owning contracts; follow
the [container and runtime prerequisites](../docs/development.md). Directory placement does not
control test selection: the native solution and explicit CI commands do.

## Maintained suites

| Owner | Test projects | Obligation |
| --- | --- | --- |
| ActorIdentity | [Core](../src/Gloaming.ActorIdentity/tests/ActorIdentityTests/ActorIdentityTests.csproj), [HTTP](../src/Gloaming.ActorIdentity/tests/ActorIdentityAspNetCoreTests/ActorIdentityAspNetCoreTests.csproj) | Opaque identity/attribution and immutable establishment; effective native policy principal, schemes, challenges and anonymous paths. |
| Tenancy | [Core](../src/Gloaming.Tenancy/tests/TenantTests/TenantTests.csproj), [HTTP](../src/Gloaming.Tenancy/tests/TenancyAspNetCoreTests/TenancyAspNetCoreTests.csproj), [PostgreSQL](../src/Gloaming.Tenancy/tests/TenancyPostgresTests/TenancyPostgresTests.csproj) | Tenantless/missing distinction, independent admission/order, native transaction-local context and cleanup. |
| Persistence | [Model/write guards](../src/Gloaming.Persistence/tests/EntityFrameworkCoreTests/EntityFrameworkCoreTests.csproj), [GUID adopter](../src/Gloaming.Persistence/tests/PersistenceTests/PersistenceTests.csproj), [RLS](../src/Gloaming.Persistence/tests/PostgresTests/PostgresTests.csproj), [module adopter](../src/Gloaming.Persistence/tests/OwnershipAdoptionTests/PersistenceDemo.Tests.csproj) | Supported/rejected mappings, stored-owner predicates, detached writes, native policy/grants/lifecycle, independent migrations and transactions. |
| EntityFrameworkCore | [Save coordinator](../src/Gloaming.EntityFrameworkCore/tests/CapabilityTests/CapabilityTests.csproj) | Context/model selection, scoped execution, prepare/detect/validate ordering, idempotent installation, suppressed/nested saving, cancellation and failed-operation disposal. No feature-family or sample dependency. |
| CommandReceipts | [Independent provider/deployment adopter](../src/Gloaming.CommandReceipts/tests/CommandReceiptTests/CommandReceiptTests.csproj) | Public receipt API, transaction/immutable-proposal guards, concurrency, rollback/cancellation/uncertain commit, append-only lifetime and explicit removal; separately compiled old/new consumers, real process death and frozen lock-protocol compatibility. No Sales or Messaging dependency. |
| EventReactions | [Reactions](../src/Gloaming.EventReactions/tests/EventReactionsTests/EventReactionsTests.csproj), [native EF adopter](../src/Gloaming.EventReactions/tests/AdoptionTests/Consumer.Tests.csproj) | Ordered bounded capture, explicit source selection, immutable/package-free models, real EF save/rollback and reaction-created ownership/outbox validation. |
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

## What each kind of test establishes

The outbox suite also owns a [native due-time consumer experiment](../src/Gloaming.Messaging/tests/Fixtures/DueTimeIntent/README.md).
Its clock/cancellation/generation/process proofs establish consumer behavior using existing
outbox and command-receipt contracts, not a new supported scheduler capability.

Direct library-contract tests use public APIs with independent expected outcomes, such as
[immutable actor establishment](../src/Gloaming.ActorIdentity/tests/ActorIdentityTests/AccessorTests.cs),
[history integrity](../src/Gloaming.Events/tests/EventHistoryTests/HistoryTests.cs) and
[inbox concurrency/rollback](../src/Gloaming.Messaging/tests/InboxPostgresTests/ProcessingTests.cs).
These already protect Gloaming independently of Wholesale.

Adoption tests add real consumer mappings, registrations, migrations or process boundaries.
For example, [reaction/ownership/outbox composition](../src/Gloaming.EventReactions/tests/AdoptionTests/CapabilityCompositionTests.cs)
proves that the independently selected capabilities work together during native saving.
Its sample rule is test setup, not a new library guarantee.

Wholesale tests additionally protect Sales permissions, stock decisions, application migrations
and end-to-end handoffs. They can detect library regressions through real composition, but their
business assertions cannot stand in for a direct library contract. A family directory alone
does not make an assertion reusable-library coverage. This map identifies proof owners; it is
not a complete API/branch coverage measurement or a production-capacity certification.

## Keep proofs for distinct boundaries

Unit, provider, package-only and process tests can exercise the same API while protecting
different failures. A fake publisher cannot prove broker acceptance uncertainty; an in-process
rollback cannot prove abrupt process recovery; ProjectReference builds cannot prove the
[eleven package-only profiles](../tools/release/README.md#full-package-adoption-proof), including
the [native event-storage consumer](../tools/release/consumers/EventStorage.cs),
[local reaction composition](../tools/release/consumers/EventReactions.cs),
[native HTTP establishment](../tools/release/consumers/HttpEstablishment.cs), and
[restricted PostgreSQL row security](../tools/release/consumers/RowSecurity.cs).
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
