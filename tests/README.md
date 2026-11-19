# Proof ownership

Library tests live beside their owning family in `src/Gloaming.*/tests`. Root `tests/` covers
cross-family composition and repository architecture. Wholesale owns its application and runtime
suites. The release tooling owns a native loopback test project for the Kafka fault proxy.
The solution includes every native test project. Earlier/Current binaries and other
fixture hosts are executable consumers for compatibility and process tests.

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
| Evaluations | [Typed runner and workflow adopter](../src/Gloaming.Evaluations/tests/EvaluationTests/EvaluationTests.csproj) | Native workflows with/without agents, retained outputs, strict metrics, typed/native reports, selection/versions, fresh inputs, concurrency/budgets and cancellation cleanup. |
| Agents and Conversations | [Native persistence](../src/Gloaming.Agents/tests/AgentPersistenceTests/AgentPersistenceTests.csproj) | Exact checkpoint/history restoration, branches/isolation, native tool/reducer support, atomic rollback, append-only grants/guards, serialized writes, typed pending requests, failure and cancellation. |
| ActorIdentity | [Core](../src/Gloaming.ActorIdentity/tests/ActorIdentityTests/ActorIdentityTests.csproj), [HTTP](../src/Gloaming.ActorIdentity/tests/ActorIdentityAspNetCoreTests/ActorIdentityAspNetCoreTests.csproj) | Opaque identity/attribution and immutable establishment; effective native policy principal, schemes, challenges and anonymous paths. |
| Tenancy | [Core](../src/Gloaming.Tenancy/tests/TenantTests/TenantTests.csproj), [HTTP](../src/Gloaming.Tenancy/tests/TenancyAspNetCoreTests/TenancyAspNetCoreTests.csproj), [PostgreSQL](../src/Gloaming.Tenancy/tests/TenancyPostgresTests/TenancyPostgresTests.csproj) | Tenantless/missing distinction, independent admission/order, native transaction-local context and cleanup. |
| Persistence | [Model/write guards](../src/Gloaming.Persistence/tests/EntityFrameworkCoreTests/EntityFrameworkCoreTests.csproj), [GUID/complex-value adopters](../src/Gloaming.Persistence/tests/PersistenceTests/PersistenceTests.csproj), [RLS](../src/Gloaming.Persistence/tests/PostgresTests/PostgresTests.csproj), [module adopter](../src/Gloaming.Persistence/tests/OwnershipAdoptionTests/PersistenceDemo.Tests.csproj) | Supported/rejected mappings, inline/nested/optional and JSON values, stored-owner predicates, detached writes, native policy/grants/lifecycle, independent migrations and transactions. |
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

In the [CI workflow](../.github/workflows/ci.yml), Repository integrity uses Nx to select
ordinary suites, including provider/adoption tests and Wholesale integration. The Wholesale
runtime check owns browser and topology proofs. One scope job applies the
[environment path map](../.github/ci-paths.yml). Manual dispatch runs all checks.
The [development guide](../docs/development.md) documents native local/container prerequisites.

## Test boundaries

| Level | Failure it should catch | Examples |
| --- | --- | --- |
| Unit and library contract | A rule, public return value or retained payload changes. | [Actor establishment](../src/Gloaming.ActorIdentity/tests/ActorIdentityTests/AccessorTests.cs), [event history](../src/Gloaming.Events/tests/EventHistoryTests/HistoryTests.cs). |
| Application | A consuming module makes the wrong business decision or enforces the wrong authority. | [Wholesale scenarios](../samples/Wholesale/IntegrationTests/README.md). |
| Provider integration | Native mappings, transaction boundaries, credentials or delivery behavior break. | [Inbox processing](../src/Gloaming.Messaging/tests/InboxPostgresTests/ProcessingTests.cs), [reaction, ownership and outbox composition](../src/Gloaming.EventReactions/tests/AdoptionTests/CapabilityCompositionTests.cs). |
| System/runtime | The host cannot establish the intended graph, provision it, expose health or recover across a process boundary. | [Wholesale runtime](../samples/Wholesale/RuntimeComposition.Tests/RuntimeTests.cs), [Sales runtime roles and policies](../samples/Wholesale/RuntimeComposition.Tests/RowSecurityRuntimeTests.cs). |
| Browser end to end | The real provider, origin, cookies, admission or protected action fail together. | [Wholesale Chromium journeys](../samples/Wholesale/RuntimeComposition.Tests/BrowserTests.cs). |
| Package adoption | Packaged contents, dependency closure, source mapping or external consumer behavior break. | [Package verification](../eng/release/README.md#full-package-adoption-proof), [Kafka package consumer](../eng/release/consumers/Kafka/README.md). |

Assert each behavior at the cheapest level that can detect its regression. Add a provider,
process, browser or package case when that boundary creates a distinct failure. Use literal
retained payloads and migration inputs with independently stated expected outcomes.
Test the configured Gloaming or application behavior when it depends on a framework.
Architecture checks enforce the module and package dependency boundaries. Use executable
adoption tests to verify a fixture's behavior.

The Kafka profile packs and restores the exact package closure, then runs Kafka and PostgreSQL
through disposable Testcontainers fixtures. Run `npm --prefix eng/release run verify:kafka`
with a Docker-compatible socket. The [native proxy tests](../eng/release/tests/KafkaProxy.Tests.csproj)
exercise the same proxy source over loopback sockets in the ordinary suite. The [recipe](../modular-monolith-patterns/kafka.md) documents
the transport behavior and limits.

## Repository tooling

[Reference-checker tests](../eng/repository/references.test.ts) exercise valid source links,
missing/deleted/untracked targets, ignored build output, literal linked fixtures and CLI status.
They run without .NET or containers. They do not replace native restore/build, package README
rendering, provider/runtime proofs or independent code/architecture review.
