# Library family relocation

Status: owner authorized on 2026-10-07, after approving and requesting a checkpoint of the
complete ES1 change set. That checkpoint is `ab85ec9`. This is the separately reviewable
folder/documentation follow-up; relocation changes remain unstaged.

This plan records the 2026-10-07 relocation under its original library names. The subsequent
owner-authorized [initial naming checkpoint](../reports/library-naming-checkpoint.md) and
[Gloaming rename](../reports/gloaming-library-renaming.md) change current source/project
names; the table below preserves the relocation scope at that checkpoint.

## Concrete scope

Group the nine existing library projects and nine library test projects into five families.
Preserve assembly/project identities, runtime APIs, dependencies, C# source, fixtures and
migrations. Add a family README and local capability/composition guide to each. Keep package
READMEs and detailed EventSourcing capability records beside their own projects.

| Previous directory | Current directory |
| --- | --- |
| `src/ModulithFoundry.ActorIdentity/` | `src/ModulithFoundry.ActorIdentity/ModulithFoundry.ActorIdentity/` |
| `src/ModulithFoundry.ActorIdentity.AspNetCore/` | `src/ModulithFoundry.ActorIdentity/ModulithFoundry.ActorIdentity.AspNetCore/` |
| `tests/ActorIdentityTests/` | `src/ModulithFoundry.ActorIdentity/tests/ActorIdentityTests/` |
| `tests/ActorIdentityAspNetCoreTests/` | `src/ModulithFoundry.ActorIdentity/tests/ActorIdentityAspNetCoreTests/` |
| `src/ModulithFoundry.Tenancy/` | `src/ModulithFoundry.Tenancy/ModulithFoundry.Tenancy/` |
| `src/ModulithFoundry.Tenancy.AspNetCore/` | `src/ModulithFoundry.Tenancy/ModulithFoundry.Tenancy.AspNetCore/` |
| `tests/TenantTests/` | `src/ModulithFoundry.Tenancy/tests/TenantTests/` |
| `tests/TenancyAspNetCoreTests/` | `src/ModulithFoundry.Tenancy/tests/TenancyAspNetCoreTests/` |
| `src/ModulithFoundry.Persistence.EntityFrameworkCore/` | `src/ModulithFoundry.Persistence/ModulithFoundry.Persistence.EntityFrameworkCore/` |
| `tests/EntityFrameworkCoreTests/` | `src/ModulithFoundry.Persistence/tests/EntityFrameworkCoreTests/` |
| `tests/PersistenceTests/` | `src/ModulithFoundry.Persistence/tests/PersistenceTests/` |
| `src/ModulithFoundry.Events.Serialization/` | `src/ModulithFoundry.Events/ModulithFoundry.Events.Serialization/` |
| `src/ModulithFoundry.Events.History/` | `src/ModulithFoundry.Events/ModulithFoundry.Events.History/` |
| `tests/EventSerializationTests/` | `src/ModulithFoundry.Events/tests/EventSerializationTests/` |
| `tests/EventHistoryTests/` | `src/ModulithFoundry.Events/tests/EventHistoryTests/` |
| `src/ModulithFoundry.EventSourcing/` | `src/ModulithFoundry.EventSourcing/ModulithFoundry.EventSourcing/` |
| `src/ModulithFoundry.EventSourcing.EntityFrameworkCore/` | `src/ModulithFoundry.EventSourcing/ModulithFoundry.EventSourcing.EntityFrameworkCore/` |
| `tests/EventSourcingTests/` | `src/ModulithFoundry.EventSourcing/tests/EventSourcingTests/` |

Repository architecture checks stay at `tests/ArchitectureTests/`; shared PostgreSQL support
stays at `tests/Support/`. Sample test projects stay with their consumers. The active solution
still contains all 42 projects and excludes the frozen archive.

## File and behavior map

| Files | Change and supported behavior |
| --- | --- |
| Existing library/test files | Relocate into the owning family. C# contents and all nine library project declarations remain byte-for-byte equal to checkpoint `ab85ec9`. |
| Active test/sample project declarations | Rebase ProjectReference and linked Compile/None source paths; retain the same project graph, copied architecture declarations and shared fixture ownership. |
| ModulithFoundry.slnx | Rebase paths and group projects/tests by family; preserve project membership. |
| .editorconfig | Apply the existing test-name analyzer exception to family-local tests. |
| lefthook.yml and active CI jobs | Use relocated library test paths. Archived jobs retain archive-relative commands. |
| tools/template/create.ts and verify.ts | Explicit source map locates the same three selected upstream packages; generated library directories, project names, source hashes and event-free composition retain their existing contract. |
| Family README/docs and package Markdown | Add local composition/capability context, rebase existing navigation, and replace the former EventSourcing relocation proposal with current layout. |
| Root README/design/development/extraction and active historical documentation | Link current family ownership and repair paths for navigation/commands. Dated test counts/checkpoints remain historical evidence. |
| This plan and the relocation report | Separate structural verification from the earlier ES1 implementation proofs. |

No new library public interface or runtime mechanism is introduced. Deferred provider adapters,
projection capabilities and template event presets remain deferred. The family directory is
organizational; selecting one project never installs its siblings automatically.

## Verification contract

Restore/build the full active solution; run family suites and relevant sample consumers,
including real PostgreSQL ownership, event/inline-state and HTTP integration. Recheck native
migration models, architecture/dependency independence and style/analyzers. Exercise the T1
creator and both external generated consumers against PostgreSQL, including deterministic
sources and omission. Verify the frozen archive checksum and active local documentation links.
Compare relocated source bytes/project membership to the approved checkpoint. Record actual
results and any unexecuted lanes in [the report](../reports/library-family-layout.md).

## Ownership and review

The approved implementation is safely checkpointed before relocation. This follow-up does not
change domain policy, event behavior or consumer save/commit ownership. Nothing is staged or
committed by the relocation workflow; a further commit requires approval of its exact complete
change set under the repository workflow.
