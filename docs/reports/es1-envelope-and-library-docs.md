# ES1 default envelope and library-local documentation refinement

Historical design/execution record. The active interface is superseded by
[the owner-approved native EF replacement](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/es2-native-ef-simplification.md).
Renamed-source links lead to current replacements; removed mechanisms link to their replacement scope.

Status: implemented for owner review, 2026-10-07. The owner requested a default StoredEventRecord,
accepted clearer store/projection names, and required self-sufficient capability/limitation/
plan context beside each library. This refines the same reviewable ES1 capability. New changes
are unstaged; the owner's existing index is preserved. No commit or later runtime capability.

## Outcome

The EF library supplies optional sealed StoredEventRecord implementing the existing envelope
interface. It carries the same common identity/position/name/schema/time/JsonElement properties;
it imposes no encoding, JSONB provider registration, tenant or additional-field policy. Multiple
concrete facts retain different payload shapes in one stream. Custom envelope implementations
remain appropriate for owned complete keys and extra fields.

InlineState is renamed InlineProjectionStorage, including source file and native consumers.
The store's internal coordination now uses aggregateState, requiredProjections/ProjectionBinding
and loadedStreams/LoadedStream. The projection storage helper loads any selected TRow; it does
not rebuild/catch up. Save validation is split into named checks for inserted envelopes,
advancing headers, required projection metadata, event ranges and independent projection edits.
The checks preserve the existing native transaction/version/range behavior.

Current capabilities, limits and deferred context now live in library-local READMEs/docs.
The EF package owns the relocated capability catalog, with a pointer at its previous root path.
The aggregate core has its own capability document and dependency/composition explanation.
Other library READMEs retain supported setup/errors locally and add explicit deferred context;
stale future-tense descriptions of implemented tenancy HTTP/EF and native history adoption
are corrected. Repository conventions record this documentation ownership. Root plans, ADRs
and reports retain interface review, cross-cutting decisions and dated proof evidence.

The local record explicitly preserves rebuild/catch-up, async, snapshots, upcasting and
multi-stream projection gaps. Several required synchronous views of one stream are supported;
they are persistence projections, not additional command aggregates or domain children.

Provider-specific locks/RLS and a possible ModulithFoundry.EventSourcing.Postgres adapter are
recorded candidates only. Shared packages do not acquire provider dependencies. SQL Server
and SQLite are unverified candidates, not promised compatibility. A useful provider seam and
its difficulty remain to be demonstrated by a concrete caller and real lock/transaction tests.
No SELECT ... FOR UPDATE abstraction or provider package is implemented.

Grouping related projects/README/docs/tests under a family folder is recorded as a separate
relocation proposal. Current project/test paths remain intact. Its scope would include solution
entries, source/project references, architecture policies, CI/hooks and template source snapshots.
T1 copies selected top-level C#/project files, not these READMEs/docs, and its generated event-free
composition is unaffected. The complete template adoption lane was not rerun here.

## New proof and verification

The new default-envelope integration test uses the provided IEventStore write flow to prepare
and commit two facts with different CLR/JSON shapes. Its alternate native model maps the library
envelope onto the existing journal tables created by retained consumer migrations. A fresh
context using the original consumer mapping reads the rows, verifies durable names, schema,
contiguous versions/timestamps and exact typed payload values. This proves default-envelope
interoperability without replacing fixtures/migrations or the retained custom-row demonstration.

Actual reruns against the refinement:

| Suite/check | Result |
| --- | --- |
| Wholesale EventPersistenceDemo.Tests | 141 passed; disposable PostgreSQL 18.6 |
| Independent EventStorageDemo.Tests | 50 passed; disposable PostgreSQL 18.6 |
| Existing HTTP/PostgreSQL consumer | 97 passed |
| ArchitectureTests | 67 passed |
| EventSourcingTests | 11 passed |
| EventSerializationTests | 16 passed |
| EventHistoryTests | 16 passed |
| EventCodecDemo.Tests | 16 passed |
| Active solution build | 42 projects; zero warnings/errors |
| Semantic style/analyzers | Verification clean |
| CSharpier | 368 files checked |
| Native EF pending-model checks | All three retained consumer models unchanged |
| Documentation/whitespace | 973 local links in 90 Markdown documents; clean |
| Archive verification | 800 original files preserved |

Total: **414 passed**, none skipped/failed. Native EF pending-model checks found no changes for Inventory, Purchasing or the independent
storage model. Documentation links and whitespace passed at handoff. PostgreSQL/runner checks used authorized local IPC and the rootless Podman socket.

The preceding [store report](es1-library-write-store.md) records its separate 413-test execution.
This refinement adds one reusable default row implementation and proves its interoperability;
renaming, validation decomposition and documentation introduce no new concurrency, async,
provider-neutral or projection-engine guarantee. Existing consumer eligibility, competing-writer,
rollback, omitted/altered projection and fresh-context recovery assertions are rerun unchanged.
Archive references and Marten comparisons remain historical/source evidence.

## Review scope and preserved work

Review [StoredEventRecord](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/StoredEventRecord.cs),
[InlineProjectionStorage](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/InlineStateReader.cs),
[EventStore](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/EventStore.cs),
[save validation](../../src/Gloaming.EventSourcing/Gloaming.EventSourcing.EntityFrameworkCore/RequiredInlineStateExtensions.cs)
and [default-envelope adoption](https://github.com/oxface/modulith-foundry/blob/dd6eb77a4a0e3c8afd7af710d7689ca265d43a64/src/Gloaming.EventSourcing/samples/EventStorageDemo.Tests/AppendTests.DefaultEnvelope.cs).
The existing append test class becomes partial to host that proof. Inventory/Purchasing inline
loaders and Inventory's native query use the renamed storage type. No runtime dependency change.

Review both EventSourcing package READMEs/local capability documents, the seven other library
README refinements, repository documentation convention and current root plan/design links.
The previous catalog path remains a pointer rather than losing its historical references.
Exact public types and the complete ES1 file map remain in [the review brief](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/es1-library-write-store.md).

Library mechanism: optional default envelope, existing shared write coordinator/projection
storage and explicit tracked-save checks. Consumer policy: domain fact definitions, encoding/
registrations, reducers, projection choices, ownership/admission and native save/commit. Template
finding: documentation locality does not require adding event presets or changing generated
source. Independent adoption still does not require Events.Serialization or Events.History;
Wholesale intentionally selects both.

The index hash at this turn's start and handoff is
`0e9dd0eda2341ad55d98d650b49ac3c2be04bca850f87c0d26b8d07991008b48`.
The owner had staged earlier work since the preceding report; no index entries are changed here.
Pre-existing untracked PreparedEventAppend remains preserved. No demo, migration, fixture,
archived file or unrelated worktree change is removed. No commit is authorized or made.

The same tracked-save limits remain: explicit guard integration is required; arbitrary/bulk/
external writes, bypassed saves and semantic state-payload tampering are not covered. Native
optimistic concurrency is implemented, explicit locks are deferred, and only PostgreSQL is
verified. No automatic repair, catch-up, retry/rebase, pending clear or ambiguous-commit recovery.
Stop after this single ES1 capability is reviewable.
