# S1 CI selection and statement-spacing repair

Date: 2026-10-22. Branch: `feat/v1-integration`. Reviewed baseline: `10d938f`.
Scope: repair S1 review findings in the existing main CI dependency map and two statement
separators. This report records local configuration verification, not a GitHub Actions run.

## Outcome and review-worthy files

| File | Result |
| --- | --- |
| `.github/workflows/ci.yml` | Tenancy now executes `ContextDemo.Tests` followed by the ContextDemo console; Persistence executes `PersistenceDemo.Tests`. Wholesale executes only its canonical `Wholesale.IntegrationTests` project. The complete command inventory and arguments are preserved. |
| `.github/ci-paths.yml` | Tenancy additionally selects ActorIdentity core changes consumed by ContextDemo. Wholesale selects either shared infrastructure fixture, including broker-only fixture changes after test consolidation. |
| `samples/Wholesale/Api/Endpoints/DraftOrderEndpoints.cs` | Separate the completed lifecycle-registration block from the next endpoint registration. |
| `src/Gloaming.Messaging/samples/ServiceDefaults/Extensions.cs` | Separate the optional exporter guard from health-check registration. |
| This report | Preserve the actual graph, linked fixture inputs, selection evidence and limits. |

The repair retains the existing SHA-pinned `dorny/paths-filter`, its `some-with-excludes`
semantics, shared build inputs, conservative unknown-root fallback and package filter.
`changes.yml`, `packages.yml` and the Messaging WF2 deployment-proof command are unchanged.
No archived implementation reference, action dependency or durable custom selector is added.

The graph was inspected rather than inferred from package names. In this tree,
`Gloaming.Tenancy.EntityFrameworkCore.Postgres` references Tenancy core and the NuGet EF
Relational/Npgsql packages; it does **not** reference either Gloaming Persistence adapter or
Gloaming.EntityFrameworkCore. ContextDemo references ActorIdentity core and Tenancy core.
PersistenceDemo references Persistence.EntityFrameworkCore and Tenancy core; it does **not**
reference ActorIdentity. Those nonexistent edges were rejected as selection requirements.
The existing Persistence filter already includes Tenancy core and shared EF support.

## New verification

A temporary Python XML inventory followed every declared transitive `ProjectReference`
from every native `dotnet test`/`dotnet run` command in main CI. A temporary Node 24.21.0
probe parsed YAML with the already installed repository `js-yaml` and matched the existing
glob vocabulary with native `path.matchesGlob`. For each project closure it checked every
tracked non-Markdown file beneath the project directories plus resolved external
`Compile`, `None`/`None Update`, `Content` and `EmbeddedResource` inputs, expanding fixture
globs against current files. The temporary tools live under `/tmp`, not in the repository.
Source reads were also inspected for JSON, SQL and project-declaration fixture consumers.
The exact resulting graph and declared linked inputs appear below.

All **1,921 job/input pairs** across the ten main native CI jobs select their owning job;
there are zero uncovered current non-Markdown inputs. These are repeated job/input pairs,
not 1,921 distinct files. Shared project/props/targets/solution patterns participate in selection.

**97 focused probes** additionally passed for dependent roots, relocated examples, both
infrastructure fixtures, unrelated family sources, unknown roots, shared build inputs and
Markdown exclusions. All **330 tracked Markdown files** skip functional jobs. Package README
changes still select packaging. In particular:

| Changed path | Expected selected job/result |
| --- | --- |
| `src/Gloaming.Tenancy/samples/ContextDemo.Tests/CompositionTests.cs` | Tenancy true; Wholesale false. |
| `src/Gloaming.Persistence/samples/PersistenceDemo.Tests/OwnershipTests.cs` | Persistence true; Wholesale false. |
| `src/Gloaming.ActorIdentity/Gloaming.ActorIdentity/ActorContext.cs` | Tenancy true; Persistence false. |
| `src/Gloaming.ActorIdentity/Gloaming.ActorIdentity.AspNetCore/ActorMiddleware.cs` | Tenancy false. |
| `src/Gloaming.Persistence/Gloaming.Persistence.EntityFrameworkCore/Any.cs` | Tenancy false. |
| `tests/Support/RabbitMqFixture.cs` | Wholesale true. |
| `tests/Support/PostgreSqlFixture.cs` | Wholesale true. |
| `src/Gloaming.Messaging/samples/MessagingWorkerDemo.Tests/ChildHost.cs` | Wholesale true through its explicit linked-input rule. |
| `src/Gloaming.Events/samples/EventCodecDemo/Fixtures/Inventory/opened.v1.json` | Wholesale true through its explicit fixture rule. |
| `src/Gloaming.Messaging/samples/MessagingWorkerDemo.Tests/WorkerTests.cs` | Wholesale false. |
| `src/Gloaming.Tenancy/README.md` | All functional jobs false; packages true. |

Some negative probes intentionally use hypothetical new source names to test the pattern
boundary. The actual tracked-file inventory separately checks current inputs. The two explicit
Wholesale linked-input patterns are necessary: `src/Gloaming.*/Gloaming.*/**` does not match
family `samples` directories.

YAML parsing passed, including shared step aliases. The before/after main-workflow command
multiset is identical, and Messaging's command block and the packages filter compare exactly.
CSharpier passed the two changed C# files (`Checked 2 files`); `git diff --check` passed.
The initially empty index remains empty; these edits remain unstaged. No commit or push was
performed by this repair worker.

No new native runtime/provider test execution is claimed here: the executable commands and
C# behavior are unchanged. Concurrent root integration runs own their fresh whole-solution
and native test evidence. Earlier S1 reports remain historical relocation evidence, not tests
rerun for this configuration repair.

## Scope limits and gap disposition

This closes the reviewed main-CI selection omissions and the two statement-spacing findings.
Hosted PR detection, scheduling and runner execution still require GitHub confirmation after
an authorized push. Native-glob probing validates this configuration locally; it is not execution
of the pinned action's hosted discovery code. Dependency mapping remains explicit and must be
updated when references, linked inputs or source snapshots change. Future Markdown used as
executable fixture input needs a deliberate exception to the current documentation exclusion.

The separate `sample-runtime.yml` policy intentionally remains narrower: Wholesale changes,
shared inputs and manual dispatch request its browser/deployment proofs. Library-only changes
use family and product contract tests. RuntimeComposition.Tests' transitive library graph is
therefore **not** claimed to be exhaustively selected by the runtime filter. The exhaustive
coverage statement above applies only to the ten main CI native jobs. Generated-template and
package-adoption workflows retain their existing source-snapshot/package selection and commands.

No library interface, transaction, migration, fixture byte or sample business rule changed.
This is repository-owned CI wiring and sample formatting; no extraction candidate, template
setup mechanism or new reusable library mechanism was proven. No important main-CI dependency
gap remains in the audited current graph; final integrated runtime/review/release obligations
remain with their existing owners.

## Exact current main-CI project graph

The inventory below records all 81 reachable project files and their direct project edges.
IDs are local to this report. Repeated declared references are retained. A job's transitive
closure is obtained by following the listed edges from its command roots; duplicate console
and test dependency roots do not create extra graph projects. Project paths are repository-relative.

| Job | Command roots | Closure projects | Checked non-Markdown inputs | Uncovered |
| --- | --- | ---: | ---: | ---: |
| `auditing` | P21 | 5 | 29 | 0 |
| `actoridentity` | P18, P17 | 4 | 25 | 0 |
| `event_reactions` | P28, P25 | 11 | 116 | 0 |
| `tenancy` | P80, P78, P79, P76, P77 | 9 | 57 | 0 |
| `persistence` | P70, P71, P72, P68 | 9 | 62 | 0 |
| `events` | P40, P39, P37, P38 | 6 | 44 | 0 |
| `eventsourcing` | P34, P33, P31 | 8 | 75 | 0 |
| `messaging` | P61, P63, P65, P64, P62, P60, P54, P48, P52, P57 | 27 | 249 | 0 |
| `wholesale` | P02 | 37 | 603 | 0 |
| `repository` | P81 | 49 | 661 | 0 |

| ID | Project file | Direct ProjectReference edges |
| --- | --- | --- |
| P01 | `samples/Wholesale/Api/Wholesale.Api.csproj` | P15, P73, P06, P10, P14, P12, P03 |
| P02 | `samples/Wholesale/IntegrationTests/Wholesale.IntegrationTests.csproj` | P74, P01, P04 |
| P03 | `samples/Wholesale/ServiceDefaults/Wholesale.ServiceDefaults.csproj` | None |
| P04 | `samples/Wholesale/Worker/Wholesale.Worker.csproj` | P14, P08, P12, P10, P03, P44, P43 |
| P05 | `samples/Wholesale/modules/Access/Access.Contracts/Access.Contracts.csproj` | None |
| P06 | `samples/Wholesale/modules/Access/Access/Access.csproj` | P05, P16, P75, P19 |
| P07 | `samples/Wholesale/modules/Dispatch/Dispatch.Contracts/Dispatch.Contracts.csproj` | P13 |
| P08 | `samples/Wholesale/modules/Dispatch/Dispatch/Dispatch.csproj` | P07, P41, P75 |
| P09 | `samples/Wholesale/modules/Inventory/Inventory.Contracts/Inventory.Contracts.csproj` | None |
| P10 | `samples/Wholesale/modules/Inventory/Inventory/Inventory.csproj` | P19, P16, P41, P30, P09, P75, P67, P36, P35, P29 |
| P11 | `samples/Wholesale/modules/Purchasing/Purchasing.Contracts/Purchasing.Contracts.csproj` | None |
| P12 | `samples/Wholesale/modules/Purchasing/Purchasing/Purchasing.csproj` | P30, P11, P75, P67, P36, P35, P29 |
| P13 | `samples/Wholesale/modules/Sales/Sales.Contracts/Sales.Contracts.csproj` | P09 |
| P14 | `samples/Wholesale/modules/Sales/Sales/Sales.csproj` | P66, P23, P45, P09, P41, P36, P19, P16, P13, P75, P67 |
| P15 | `src/Gloaming.ActorIdentity/Gloaming.ActorIdentity.AspNetCore/Gloaming.ActorIdentity.AspNetCore.csproj` | P16 |
| P16 | `src/Gloaming.ActorIdentity/Gloaming.ActorIdentity/Gloaming.ActorIdentity.csproj` | None |
| P17 | `src/Gloaming.ActorIdentity/tests/ActorIdentityAspNetCoreTests/ActorIdentityAspNetCoreTests.csproj` | P15 |
| P18 | `src/Gloaming.ActorIdentity/tests/ActorIdentityTests/ActorIdentityTests.csproj` | P16 |
| P19 | `src/Gloaming.Auditing/Gloaming.Auditing.EntityFrameworkCore.Postgres/Gloaming.Auditing.EntityFrameworkCore.Postgres.csproj` | P20 |
| P20 | `src/Gloaming.Auditing/Gloaming.Auditing.EntityFrameworkCore/Gloaming.Auditing.EntityFrameworkCore.csproj` | P22, P16 |
| P21 | `src/Gloaming.Auditing/tests/AuditPostgresTests/AuditPostgresTests.csproj` | P19 |
| P22 | `src/Gloaming.EntityFrameworkCore/Gloaming.EntityFrameworkCore/Gloaming.EntityFrameworkCore.csproj` | None |
| P23 | `src/Gloaming.EventReactions/Gloaming.EventReactions.EntityFrameworkCore/Gloaming.EventReactions.EntityFrameworkCore.csproj` | P22, P24 |
| P24 | `src/Gloaming.EventReactions/Gloaming.EventReactions/Gloaming.EventReactions.csproj` | None |
| P25 | `src/Gloaming.EventReactions/samples/EventReactionsDemo/Consumer.Tests/Consumer.Tests.csproj` | P67, P41, P26 |
| P26 | `src/Gloaming.EventReactions/samples/EventReactionsDemo/Consumer/EventReactionsDemo.csproj` | P27, P23 |
| P27 | `src/Gloaming.EventReactions/samples/EventReactionsDemo/Model/EventReactionsDemo.Model.csproj` | None |
| P28 | `src/Gloaming.EventReactions/tests/EventReactionsTests/EventReactionsTests.csproj` | P24 |
| P29 | `src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/Gloaming.EventSourcing.EntityFrameworkCore.csproj` | P22, P30, P35 |
| P30 | `src/Gloaming.EventSourcing/Gloaming.EventSourcing/Gloaming.EventSourcing.csproj` | None |
| P31 | `src/Gloaming.EventSourcing/samples/EventStorageDemo.Tests/EventStorageDemo.Tests.csproj` | P32 |
| P32 | `src/Gloaming.EventSourcing/samples/EventStorageDemo/EventStorageDemo.csproj` | P30, P29 |
| P33 | `src/Gloaming.EventSourcing/tests/EventSourcingPostgresTests/EventSourcingPostgresTests.csproj` | P29 |
| P34 | `src/Gloaming.EventSourcing/tests/EventSourcingTests/EventSourcingTests.csproj` | P30 |
| P35 | `src/Gloaming.Events/Gloaming.Events.History/Gloaming.Events.History.csproj` | None |
| P36 | `src/Gloaming.Events/Gloaming.Events.Serialization/Gloaming.Events.Serialization.csproj` | None |
| P37 | `src/Gloaming.Events/samples/EventCodecDemo.Tests/EventCodecDemo.Tests.csproj` | P38 |
| P38 | `src/Gloaming.Events/samples/EventCodecDemo/EventCodecDemo.csproj` | P35, P36 |
| P39 | `src/Gloaming.Events/tests/EventHistoryTests/EventHistoryTests.csproj` | P35 |
| P40 | `src/Gloaming.Events/tests/EventSerializationTests/EventSerializationTests.csproj` | P36 |
| P41 | `src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/Gloaming.Messaging.EntityFrameworkCore.Postgres.csproj` | P42 |
| P42 | `src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore/Gloaming.Messaging.EntityFrameworkCore.csproj` | P22, P47 |
| P43 | `src/Gloaming.Messaging/Gloaming.Messaging.OpenTelemetry/Gloaming.Messaging.OpenTelemetry.csproj` | None |
| P44 | `src/Gloaming.Messaging/Gloaming.Messaging.RabbitMQ/Gloaming.Messaging.RabbitMQ.csproj` | None |
| P45 | `src/Gloaming.Messaging/Gloaming.Messaging.Workflows.EntityFrameworkCore/Gloaming.Messaging.Workflows.EntityFrameworkCore.csproj` | P22, P46, P42 |
| P46 | `src/Gloaming.Messaging/Gloaming.Messaging.Workflows/Gloaming.Messaging.Workflows.csproj` | P47 |
| P47 | `src/Gloaming.Messaging/Gloaming.Messaging/Gloaming.Messaging.csproj` | None |
| P48 | `src/Gloaming.Messaging/samples/InboxDemo.Tests/InboxDemo.Tests.csproj` | P49, P50 |
| P49 | `src/Gloaming.Messaging/samples/InboxDemo/InboxDemo.csproj` | P36, P41 |
| P50 | `src/Gloaming.Messaging/samples/MessagingDemo/MessagingDemo.csproj` | P55, P49, P44 |
| P51 | `src/Gloaming.Messaging/samples/MessagingProducerDemo/MessagingProducerDemo.csproj` | P55, P43, P56 |
| P52 | `src/Gloaming.Messaging/samples/MessagingWorkerDemo.Tests/MessagingWorkerDemo.Tests.csproj` | P53, P51 |
| P53 | `src/Gloaming.Messaging/samples/MessagingWorkerDemo/MessagingWorkerDemo.csproj` | P44, P50, P43, P56 |
| P54 | `src/Gloaming.Messaging/samples/OutboxDemo.Tests/OutboxDemo.Tests.csproj` | P55 |
| P55 | `src/Gloaming.Messaging/samples/OutboxDemo/OutboxDemo.csproj` | P41 |
| P56 | `src/Gloaming.Messaging/samples/ServiceDefaults/Messaging.ServiceDefaults.csproj` | None |
| P57 | `src/Gloaming.Messaging/samples/WorkflowDeploymentDemo.Tests/WorkflowDeploymentDemo.Tests.csproj` | P59, P58 |
| P58 | `src/Gloaming.Messaging/samples/WorkflowDeploymentDemo/Current/WorkflowDeploymentDemo.Current.csproj` | P45, P41 |
| P59 | `src/Gloaming.Messaging/samples/WorkflowDeploymentDemo/Earlier/WorkflowDeploymentDemo.Earlier.csproj` | P45, P41 |
| P60 | `src/Gloaming.Messaging/tests/InboxPostgresTests/InboxPostgresTests.csproj` | P41 |
| P61 | `src/Gloaming.Messaging/tests/MessagingTests/MessagingTests.csproj` | P47 |
| P62 | `src/Gloaming.Messaging/tests/OutboxPostgresTests/OutboxPostgresTests.csproj` | P41 |
| P63 | `src/Gloaming.Messaging/tests/RabbitMqTests/RabbitMqTests.csproj` | P44 |
| P64 | `src/Gloaming.Messaging/tests/WorkflowPostgresTests/WorkflowPostgresTests.csproj` | P45, P41 |
| P65 | `src/Gloaming.Messaging/tests/WorkflowTests/WorkflowTests.csproj` | P46 |
| P66 | `src/Gloaming.Persistence/Gloaming.Persistence.EntityFrameworkCore.Postgres/Gloaming.Persistence.EntityFrameworkCore.Postgres.csproj` | P67, P75 |
| P67 | `src/Gloaming.Persistence/Gloaming.Persistence.EntityFrameworkCore/Gloaming.Persistence.EntityFrameworkCore.csproj` | P22 |
| P68 | `src/Gloaming.Persistence/samples/PersistenceDemo.Tests/PersistenceDemo.Tests.csproj` | P69 |
| P69 | `src/Gloaming.Persistence/samples/PersistenceDemo/PersistenceDemo.csproj` | P67, P75 |
| P70 | `src/Gloaming.Persistence/tests/EntityFrameworkCoreTests/EntityFrameworkCoreTests.csproj` | P67 |
| P71 | `src/Gloaming.Persistence/tests/PersistenceTests/PersistenceTests.csproj` | P67 |
| P72 | `src/Gloaming.Persistence/tests/PostgresTests/PostgresTests.csproj` | P66 |
| P73 | `src/Gloaming.Tenancy/Gloaming.Tenancy.AspNetCore/Gloaming.Tenancy.AspNetCore.csproj` | P75 |
| P74 | `src/Gloaming.Tenancy/Gloaming.Tenancy.EntityFrameworkCore.Postgres/Gloaming.Tenancy.EntityFrameworkCore.Postgres.csproj` | P75 |
| P75 | `src/Gloaming.Tenancy/Gloaming.Tenancy/Gloaming.Tenancy.csproj` | None |
| P76 | `src/Gloaming.Tenancy/samples/ContextDemo.Tests/ContextDemo.Tests.csproj` | P77 |
| P77 | `src/Gloaming.Tenancy/samples/ContextDemo/ContextDemo.csproj` | P16, P75 |
| P78 | `src/Gloaming.Tenancy/tests/TenancyAspNetCoreTests/TenancyAspNetCoreTests.csproj` | P73 |
| P79 | `src/Gloaming.Tenancy/tests/TenancyPostgresTests/TenancyPostgresTests.csproj` | P74 |
| P80 | `src/Gloaming.Tenancy/tests/TenantTests/TenantTests.csproj` | P75 |
| P81 | `tests/ArchitectureTests/ArchitectureTests.csproj` | P22, P23, P26, P45, P46, P04, P04, P19, P20, P47, P42, P41, P55, P49, P50, P51, P53, P30, P35, P36, P38, P16, P15, P75, P73, P74, P67, P66, P77, P01, P69, P12, P04, P29, P32, P44 |

## Exact linked and copied inputs

All paths below resolve relative project declarations into repository-relative inputs.
The Events JSON update is an SDK-owned local item; external links and fixture globs are
expanded explicitly. Local default SDK inputs are included by project-directory inventory.

| Project | Item kind | Resolved input/pattern | Current matching files |
| --- | --- | --- | ---: |
| P02 | Compile | `tests/Support/PostgreSqlFixture.cs` | 1 |
| P02 | Compile | `tests/Support/RabbitMqFixture.cs` | 1 |
| P02 | Compile | `src/Gloaming.Messaging/samples/MessagingWorkerDemo.Tests/ChildHost.cs` | 1 |
| P02 | None | `samples/Wholesale/IntegrationTests/Api/Fixtures/Access-20a02be.sql` | 1 |
| P02 | None | `src/Gloaming.Events/samples/EventCodecDemo/Fixtures/**/*.json` | 9 |
| P21 | Compile | `tests/Support/PostgreSqlFixture.cs` | 1 |
| P25 | Compile | `tests/Support/PostgreSqlFixture.cs` | 1 |
| P31 | Compile | `tests/Support/PostgreSqlFixture.cs` | 1 |
| P33 | Compile | `tests/Support/PostgreSqlFixture.cs` | 1 |
| P38 | None | `src/Gloaming.Events/samples/EventCodecDemo/Fixtures/**/*.json` | 9 |
| P48 | Compile | `tests/Support/PostgreSqlFixture.cs` | 1 |
| P48 | Compile | `tests/Support/RabbitMqFixture.cs` | 1 |
| P52 | Compile | `tests/Support/PostgreSqlFixture.cs` | 1 |
| P52 | Compile | `tests/Support/RabbitMqFixture.cs` | 1 |
| P54 | Compile | `tests/Support/PostgreSqlFixture.cs` | 1 |
| P57 | Compile | `tests/Support/PostgreSqlFixture.cs` | 1 |
| P57 | Compile | `src/Gloaming.Messaging/samples/MessagingWorkerDemo.Tests/ChildHost.cs` | 1 |
| P57 | None | `src/Gloaming.Messaging/samples/WorkflowDeploymentDemo/fixtures/*.json` | 4 |
| P58 | Compile | `src/Gloaming.Messaging/samples/WorkflowDeploymentDemo/Application.cs` | 1 |
| P59 | Compile | `src/Gloaming.Messaging/samples/WorkflowDeploymentDemo/Application.cs` | 1 |
| P60 | Compile | `tests/Support/PostgreSqlFixture.cs` | 1 |
| P60 | Compile | `src/Gloaming.Messaging/tests/MessagingTelemetryProbe.cs` | 1 |
| P62 | Compile | `tests/Support/PostgreSqlFixture.cs` | 1 |
| P62 | Compile | `src/Gloaming.Messaging/tests/MessagingTelemetryProbe.cs` | 1 |
| P63 | Compile | `tests/Support/RabbitMqFixture.cs` | 1 |
| P64 | Compile | `tests/Support/PostgreSqlFixture.cs` | 1 |
| P68 | Compile | `tests/Support/PostgreSqlFixture.cs` | 1 |
| P71 | Compile | `src/Gloaming.Persistence/tests/EntityFrameworkCoreTests/GuidConsumerContext.cs` | 1 |
| P71 | Compile | `tests/Support/PostgreSqlFixture.cs` | 1 |
| P72 | Compile | `tests/Support/PostgreSqlFixture.cs` | 1 |
| P79 | Compile | `tests/Support/PostgreSqlFixture.cs` | 1 |
| P81 | None | `src/Gloaming.EntityFrameworkCore/Gloaming.EntityFrameworkCore/Gloaming.EntityFrameworkCore.csproj` | 1 |
| P81 | None | `src/Gloaming.EventReactions/samples/EventReactionsDemo/Model/EventReactionsDemo.Model.csproj` | 1 |
| P81 | None | `src/Gloaming.Persistence/Gloaming.Persistence.EntityFrameworkCore.Postgres/Gloaming.Persistence.EntityFrameworkCore.Postgres.csproj` | 1 |
| P81 | None | `src/Gloaming.Persistence/tests/PostgresTests/PostgresTests.csproj` | 1 |
| P81 | None | `src/Gloaming.Tenancy/Gloaming.Tenancy.EntityFrameworkCore.Postgres/Gloaming.Tenancy.EntityFrameworkCore.Postgres.csproj` | 1 |
| P81 | None | `src/Gloaming.Tenancy/tests/TenancyPostgresTests/TenancyPostgresTests.csproj` | 1 |
| P81 | None | `src/Gloaming.EventReactions/Gloaming.EventReactions/Gloaming.EventReactions.csproj` | 1 |
| P81 | None | `src/Gloaming.EventReactions/Gloaming.EventReactions.EntityFrameworkCore/Gloaming.EventReactions.EntityFrameworkCore.csproj` | 1 |
| P81 | None | `src/Gloaming.Messaging/Gloaming.Messaging.Workflows/Gloaming.Messaging.Workflows.csproj` | 1 |
| P81 | None | `src/Gloaming.Messaging/Gloaming.Messaging.Workflows.EntityFrameworkCore/Gloaming.Messaging.Workflows.EntityFrameworkCore.csproj` | 1 |
| P81 | None | `src/Gloaming.Messaging/Gloaming.Messaging.OpenTelemetry/Gloaming.Messaging.OpenTelemetry.csproj` | 1 |
| P81 | None | `src/Gloaming.Auditing/Gloaming.Auditing.EntityFrameworkCore.Postgres/Gloaming.Auditing.EntityFrameworkCore.Postgres.csproj` | 1 |
| P81 | None | `src/Gloaming.Auditing/Gloaming.Auditing.EntityFrameworkCore/Gloaming.Auditing.EntityFrameworkCore.csproj` | 1 |
| P81 | None | `src/Gloaming.Auditing/tests/AuditPostgresTests/AuditPostgresTests.csproj` | 1 |
| P81 | None | `src/Gloaming.Messaging/samples/InboxDemo/InboxDemo.csproj` | 1 |
| P81 | None | `src/Gloaming.Messaging/Gloaming.Messaging/Gloaming.Messaging.csproj` | 1 |
| P81 | None | `src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore/Gloaming.Messaging.EntityFrameworkCore.csproj` | 1 |
| P81 | None | `src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/Gloaming.Messaging.EntityFrameworkCore.Postgres.csproj` | 1 |
| P81 | None | `src/Gloaming.Messaging/samples/OutboxDemo/OutboxDemo.csproj` | 1 |
| P81 | None | `src/Gloaming.EventSourcing/tests/EventSourcingPostgresTests/EventSourcingPostgresTests.csproj` | 1 |
| P81 | None | `src/Gloaming.EventSourcing/Gloaming.EventSourcing/Gloaming.EventSourcing.csproj` | 1 |
| P81 | None | `src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/Gloaming.EventSourcing.EntityFrameworkCore.csproj` | 1 |
| P81 | None | `src/Gloaming.EventSourcing/samples/EventStorageDemo/EventStorageDemo.csproj` | 1 |
| P81 | None | `src/Gloaming.Events/Gloaming.Events.History/Gloaming.Events.History.csproj` | 1 |
| P81 | None | `src/Gloaming.Events/Gloaming.Events.Serialization/Gloaming.Events.Serialization.csproj` | 1 |
| P81 | None | `src/Gloaming.Tenancy/Gloaming.Tenancy.AspNetCore/Gloaming.Tenancy.AspNetCore.csproj` | 1 |
| P81 | None | `src/Gloaming.ActorIdentity/Gloaming.ActorIdentity.AspNetCore/Gloaming.ActorIdentity.AspNetCore.csproj` | 1 |
| P81 | None | `src/Gloaming.ActorIdentity/Gloaming.ActorIdentity/Gloaming.ActorIdentity.csproj` | 1 |
| P81 | None | `src/Gloaming.Tenancy/Gloaming.Tenancy/Gloaming.Tenancy.csproj` | 1 |
| P81 | None | `src/Gloaming.Persistence/Gloaming.Persistence.EntityFrameworkCore/Gloaming.Persistence.EntityFrameworkCore.csproj` | 1 |
| P81 | None | `src/Gloaming.Messaging/Gloaming.Messaging.RabbitMQ/Gloaming.Messaging.RabbitMQ.csproj` | 1 |
| P81 | None | `src/Gloaming.Messaging/tests/RabbitMqTests/RabbitMqTests.csproj` | 1 |

The Wholesale event-fixture pattern expands to all nine current JSON files, including the
original Inventory literals and later Inventory/Purchasing versions. The Messaging deployment
fixture pattern expands to its four current JSON files. Architecture project declarations
are copied data as well as source-reference evidence; shared `**/*.csproj` selection covers
their changes. The linked GUID consumer, telemetry probe, child-process helper, shared
application source and both neutral infrastructure fixtures are included independently
of their ProjectReference closures.
