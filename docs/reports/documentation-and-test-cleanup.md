# Documentation and test cleanup

Date: 2026-10-23. Base checkpoint: `3c4f9a9`. Status: local checks passed; uncommitted review batch.
The previous integrated-verification report and two status
updates were already unstaged when this pass began. They remain in this review batch;
the existing index is preserved. No commit, push or publication is performed.

## Outcome and proof ownership

The extra pass inventories the active documentation and native test graph, checks duplication
and retired-roadmap wording, and distinguishes public-contract proofs, independent adopter
wiring and Wholesale business assertions. It is a targeted source/contract assessment, not
an exhaustive assertion-by-assertion or API/branch coverage certification.

The independent families already have substantial direct coverage. Wholesale is an additional
application adopter; its size and folder placement do not make it Gloaming's only proof owner.
The inspected boundaries include:

| Family | Independent contract/adoption evidence and disposition |
| --- | --- |
| ActorIdentity | [Accessor lifecycle/concurrency](../../src/Gloaming.ActorIdentity/tests/ActorIdentityTests/AccessorTests.cs) and [native HTTP policy identity](../../src/Gloaming.ActorIdentity/tests/ActorIdentityAspNetCoreTests/HttpActorTests.cs). Keep both; native policies and operation lifetime protect different failures. |
| Tenancy | [Standalone context lifecycle](../../src/Gloaming.Tenancy/tests/TenantTests/AccessorTests.cs) and [PostgreSQL transaction/pool reuse](../../src/Gloaming.Tenancy/tests/TenancyPostgresTests/ContextTests.cs). Admission/Organization rules remain consumer-owned. |
| EntityFrameworkCore | [Standalone coordinator suite](../../src/Gloaming.EntityFrameworkCore/tests/CapabilityTests/CompositionTests.cs). Transfer the coordinator-only tests out of EventReactions; strengthen the existing sync/async contract without another feature-family dependency. |
| Persistence | [Detached GUID-owner writes](../../src/Gloaming.Persistence/tests/PersistenceTests/GuidOwnershipTests.cs) and [restricted-role RLS](../../src/Gloaming.Persistence/tests/PostgresTests/RowSecurityTests.cs). Keep model validation, actual SQL enforcement and module-migration adoption as distinct obligations. |
| Events | [Explicit upcasting/JSON ownership](../../src/Gloaming.Events/tests/EventSerializationTests/UpcastingTests.cs) and [ordered-history validation](../../src/Gloaming.Events/tests/EventHistoryTests/HistoryTests.cs), plus retained literal adopter fixtures. Business payload meaning stays with the consumer. |
| EventReactions | [Bounded dispatcher](../../src/Gloaming.EventReactions/tests/EventReactionsTests/DispatcherTests.cs) and [real reaction/ownership/outbox saving](../../src/Gloaming.EventReactions/tests/AdoptionTests/CapabilityCompositionTests.cs). Keep actual provider composition after moving only coordinator probes. |
| EventSourcing | [Aggregate candidate acceptance](../../src/Gloaming.EventSourcing/tests/EventSourcingTests/AggregateTests.cs), [direct-JSON rebuild/failure recovery](../../src/Gloaming.EventSourcing/tests/EventSourcingPostgresTests/RebuildTests.cs) and independent storage adoption. Inventory's stock eligibility and populated application history remain Wholesale policy/proofs. |
| Auditing | [Envelope/save guards](../../src/Gloaming.Auditing/tests/AuditPostgresTests/GuardTests.cs) and [native transaction ownership/rollback](../../src/Gloaming.Auditing/tests/AuditPostgresTests/TransactionTests.cs). Audit selection, vocabulary and disclosure stay with the consumer. |
| Messaging | [Inbox locking/atomic effects](../../src/Gloaming.Messaging/tests/InboxPostgresTests/ProcessingTests.cs), outbox fencing/recovery, native RabbitMQ lifetime, [continuation turns](../../src/Gloaming.Messaging/tests/WorkflowPostgresTests/TransactionTests.cs) and separately compiled/process adopters. Keep storage, transport uncertainty and process-death assertions; they are not interchangeable duplicates. |

No broad suite or historical fixture is removed merely because another test reaches the same
API. Repeated actor/tenant lifecycle assertions protect independently selectable contracts.
Provider/process/package boundaries retain independent proofs. Wholesale's Sales permissions,
draft allowances, stock decisions, migrations and real handoffs remain sample assertions.
No new supported-library runtime defect was identified by this pass; the concrete gap repaired
is independent ownership and failure-contract coverage for the shared save coordinator.

## Coordinator tests and selection

The old EventReactions adopter contained three coordinator-only test methods/four cases.
They now live under `src/Gloaming.EntityFrameworkCore/tests/CapabilityTests`, with both save
paths covered for missing bindings and cached-model/scoped execution. The suite has 18 cases,
including prepare/detect/validate ordering with automatic detection disabled, earlier native
suppression, swallowed nested-save failure, callback-fault disposal, context-qualified bindings
and asynchronous cancellation. Assertions use the public API and native EF behavior; no
private coordinator state is exposed. Native model-only probes never open a database.

Real reaction-created rows/outbox validation and PostgreSQL rollback remain with the
EventReactions adopter. The change does not claim model-only probes prove SQL writes or
transaction atomicity. No new production API, package dependency or runtime mechanism is added.

The solution has 31 native test projects: 27 family-local, two cross-family and two Wholesale.
The coordinator has its own CI family lane, explicit path-filter/output wiring and local hook.
The root README, [test guide](../../tests/README.md), development guide and family entry points
make selection and infrastructure requirements visible. Family-local placement is retained;
there is no repository-wide test relocation or compatibility promise for test paths.

## Documentation disposition

The starting tracked inventory contained 125 Markdown documents, plus the pending integrated
report. Retained documents have identifiable owners: root conventions/design/decisions and
research, library/package contracts, executable sample setup, fixtures and proof evidence.
Reference validation checks the whole active graph; manual changes focus on actual stale or
duplicated content rather than rewriting every valid document.

- Remove the duplicated EF event-sourcing capability file. The family catalog already contains
  its supported behavior and limits, including the later bootstrap limitation. Repoint package,
  core and Events links to that single catalog; retain the distinct package-free aggregate contract.
- Correct the aggregate capability table: the EF adapter requires Events.History and DI
  abstractions; the aggregate core remains independent. This is a documentation repair, not
  a dependency change.
- Remove obsolete E10/T1/template follow-up promises from active sample and capability guidance.
  Current application patterns and explicitly selected Messaging replace those references;
  pinned historical plans/reports remain dated evidence.
- Replace copied generic test-prerequisite paragraphs with the shared test guide, avoiding
  PostgreSQL prerequisites beside entirely container-free families.

Current owned migrations, SQL/JSON literals, separately compiled consumers and process fixtures
remain intact. Historical source remains reachable through the existing retirement provenance;
no new compatibility repair or consumer schema change is required.

## Verification and remaining gaps

Fresh checks passed:

- Complete solution restore/build: **zero warnings/errors**.
- Affected native suites: coordinator **18**, real PostgreSQL reaction adopter **82** and
  architecture **84**; **184 cases**, zero failures/skips. The adopter's previous 86 becomes
  82 because four existing coordinator cases move; the new suite adds 14 cases overall.
- CSharpier (**860 files**), .NET style and analyzers; repository-tool types/formatting and
  six checker regression cases. The first reference run found one unrepointed codec README
  link to the removed duplicate; it was repaired and the complete reference check passed.
- Parsed solution/CI inventory: all **31 native projects** have explicit commands; new
  filter/output/job/hook wiring is consistent. All 27 family projects have no direct
  Wholesale ProjectReference. This is a declaration check, not hosted scheduling execution.
- All **21 package archives and six fresh-cache executable NuGet consumers**, with exact
  dependency closures and zero ProjectReferences, including native README rendering and
  PostgreSQL save/rollback/recovery. Version: `1.0.0-local.1792755110896.g59478690`.
  Manifest SHA-256: `ed7bfee0f1e67c3df67b05f741c5ecc2891108184d78b9bbec476325614dc0eb`.

The previous [integrated run](integrated-verification-and-reviews.md) remains dated evidence
for checkpoint `3c4f9a9`, including all 1,671 cases and browser/runtime behavior. This pass
reruns affected suites and package adoption; it does not present that earlier full-suite
result as a new execution after these test/doc/CI edits.

Local evidence is retained outside the owned tree:

- `/tmp/gloaming-extra-pass-build.log`, `/tmp/gloaming-extra-pass-native-results.json`,
  `/tmp/gloaming-extra-pass-native-progress.log` and individual logs under
  `/tmp/gloaming-extra-pass-native/`.
- `/tmp/gloaming-extra-pass-{csharpier,style,analyzers,tooling,references,selection,preservation}.log`.
- `/tmp/gloaming-extra-pass-packages.log` and
  `/home/oxface/.cache/tmp/gloaming-package-proof-lz3Vmm/manifest.json`, alongside its archives,
  isolated consumers and command logs.

Of the prior 868 owned source/project/fixture inputs, **867 are byte-for-byte unchanged**.
The only existing changed input is the reduced EventReactions test file; the coordinator
test source/project are new. All existing library/sample C#, project files, migrations and
SQL/JSON fixtures remain intact. The solution adds one test-project entry. The staged snapshot
is unchanged, and the now-empty deleted document directory was removed with `rmdir`.

Review-worthy changes are the coordinator tests/project, the reduced EventReactions composition
file, solution/CI/hook wiring, test navigation and corrected capability/sample guidance.
All changes remain unstaged. **No new reusable Gloaming mechanism was proven.** No template
generation is reinstated and no sample business rule is promoted into a technical library.

The owner's separate-session fresh-context sweep remains outstanding. This pass does not claim
complete API/branch coverage, hosted CI scheduling, production capacity or Umbrine adoption.
Existing supported limits and explicit owner-approved deferrals remain with their owning
families; no new deferral is introduced. No verification defect remains unresolved.
