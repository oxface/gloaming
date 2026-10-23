# Library family relocation report

Status: implementation and verification complete for owner review, 2026-10-07. The owner-approved
ES1 implementation is committed as `ab85ec9`. This report covers only the subsequent
owner-authorized family relocation; those changes are unstaged for review.

## Outcome and review scope

Nine existing library projects and nine library test projects now belong to five source
families: ActorIdentity, Tenancy, Persistence, Events and EventSourcing. Each family owns a
README and local capability/composition guide. Detailed package setup and deferred context
remain beside their source. The [scope map](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/library-family-layout.md) lists every
previous/current project directory and affected tooling behavior.

All active C# sources and the nine library project declarations are byte-for-byte unchanged
from the approved checkpoint. Runtime interfaces, package/project identities, dependencies,
domain rules, fixtures and migrations retain their approved contents. Architecture checks
and shared PostgreSQL fixture code stay at the root; sample suites stay with their consumers.
The active solution retains all 42 projects. Related packages remain independently selectable.

Review the family READMEs/docs, active solution/project reference changes, `.editorconfig`,
hook/CI paths and the template creator/proof's explicit upstream source map. Existing active
Markdown navigation and command paths were rebased; historical execution counts/checkpoints
were retained. Frozen archive source/docs were not rewritten.

## Verification and evidence

New executions after relocation (all test cases passed, none skipped):

| Suite | Result |
| --- | --- |
| ActorIdentityAspNetCoreTests | 15 passed |
| ActorIdentityTests | 19 passed |
| EventSourcingTests | 11 passed |
| EventHistoryTests | 16 passed |
| EventSerializationTests | 16 passed |
| EntityFrameworkCoreTests | 23 passed |
| PersistenceTests | 6 passed |
| TenancyAspNetCoreTests | 39 passed |
| TenantTests | 17 passed |
| ArchitectureTests | 67 passed |
| ContextDemo.Tests | 15 passed |
| EventCodecDemo.Tests | 16 passed |
| PersistenceDemo.Tests | 36 passed |
| EventStorageDemo.Tests | 50 passed |
| EventPersistenceDemo.Tests | 141 passed |
| HttpIdentityDemo.Tests | 97 passed |
| Both external T1 consumers | 5 each, 10 total; fresh PostgreSQL 18.6 databases |

The 16 selected active suites total **584 passed**. The T1 proof additionally passed creation
rejections, repeat/partial-failure/publication races, deterministic identical-input output,
namespace `Task`, conflicting parent SDK pin, source hashes, dependency omission and local
references. Both external solutions restored/built without warnings and ran their native
PostgreSQL journeys. The disposable template-proof server was removed after completion.

Other checks:

- All 42 active projects restored and built with zero warnings/errors.
- Native style and analyzer verification passed with the family-local test naming rule.
- CSharpier checked 368 files; TypeScript type/Prettier checks passed.
- Inventory, Purchasing and independent event-storage native migration-model checks report
  no pending changes. Fixture/migration contents are unchanged.
- ContextDemo and EventCodecDemo finite executable journeys completed successfully.
- Source audit compared all 315 active C# files against `ab85ec9`: byte-for-byte unchanged.
  The 81 relocated C# files and all nine library project declarations retain identical bytes.
- All 93 explicit project/reference/linked-file targets retain their original destinations
  after applying the directory map; active solution membership is identical.
- All 1,039 local links in 102 active Markdown documents resolve. New-file whitespace and
  `git diff --check` passed.
- The frozen archive verifier matches all 800 original files. Archived CI job commands are
  byte-for-byte unchanged; no archive file, template skeleton or fixture was changed.
- The approved implementation remains checkpoint `ab85ec9`; the relocation index is empty.

These executions demonstrate that the new paths preserve working composition and existing
supported behavior. They introduce no new event-store guarantee or proof case. The checkpoint's
hooks and earlier ES1 reports remain separate prior evidence. Archived and Aspire browser/runtime
suites were not rerun for this relocation; their paths/inputs are preserved and active host
projects compile. Previous ignored build caches were moved outside the visible family layout
and retained locally; the new project locations have their own restored/built outputs.

## Mechanism, policy and remaining gaps

**No new reusable mechanism was proven.** This change improves source ownership and consumer
documentation portability. Shared libraries still supply their existing technical mechanisms;
consumer authentication/admission, domain eligibility/evolution, schema/provider configuration
and final native save/commit remain consumer policy. Families do not add runtime dependencies,
DI containers, automatic discovery or new template presets.

T1 still snapshots exactly ActorIdentity, Tenancy and Persistence.EntityFrameworkCore into its
existing generated `libraries/{Package}` directories. Events.Serialization/History remain used
by Wholesale but optional for EventSourcing and absent from T1. EventSourcing.Postgres, locks,
rebuild/catch-up, snapshots, async/multi-stream projections and other catalogued capabilities
remain deferred in the owning library documentation.

Provider/runtime support is unchanged. No package publication or independent package release
pipeline was added. Shared architecture/support files remain repository infrastructure; a
future separate-package extraction must choose the supporting proofs it carries with it.
Archived Fast/PostgreSQL/broker/topology and Aspire browser lanes retain their original paths;
this relocation does not claim fresh execution of every historical/runtime lane.
