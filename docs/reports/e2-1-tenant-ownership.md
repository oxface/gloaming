# E2.1 explicit EF tenant ownership

Checkpoint update, 2026-10-04: the owner reviewed and committed E2.1 plus the test audit as
`50e7933`. The implementation narrative below records its earlier review handoff, not the
current staging state. [E2.2](e2-2-module-migrations.md) now adds two-module migrations for
review without changing this library's implementation.

2026-10-03. Implemented for owner code review after the approved E1 split checkpoint
`c8cbf64`. The owner approved the first interface and scope, with discretion to revise it
consistently with prior decisions. All E2.1 changes are unstaged and uncommitted.

## Outcome

`ModulithFoundry.Persistence.EntityFrameworkCore` provides two explicit utilities:
`HasTenantOwnership` configures a named ownership filter and native ownership concurrency
token; `ValidateTenantChanges` inspects pending protected writes before a consumer save.
It references EF Core Relational, without ActorIdentity, Tenancy, Npgsql, Access or hosting.

The finite Inventory consumer uses Tenancy and native Npgsql/EF composition. An independent
GUID-key consumer uses neither context library. These consumers prove reuse across different
key types, property names and schemas on PostgreSQL, not provider independence.

The library never supplies ownership values, saves, commits, retries or registers services.
Consumer overrides visibly call validation before native saves. A caller-owned transaction
can roll back a successfully saved change.

## Fresh verification

| Suite/check | Result | Evidence |
| --- | --- | --- |
| Container-free EF model and validation | 25 passed | [Model/write tests](../../src/Gloaming.Persistence/tests/EntityFrameworkCoreTests/OwnershipTests.cs), [unsupported mappings](../../src/Gloaming.Persistence/tests/EntityFrameworkCoreTests/UnsupportedMappingTests.cs). |
| Independent GUID consumer, PostgreSQL 18.6 | 6 passed | [GUID proofs](../../src/Gloaming.Persistence/tests/PersistenceTests/GuidOwnershipTests.cs), with no context-library dependency. |
| Inventory composition, PostgreSQL 18.6 | 30 passed | [Sample proofs](https://github.com/oxface/modulith-foundry/blob/dd6eb77a4a0e3c8afd7af710d7689ca265d43a64/src/Gloaming.Persistence/samples/PersistenceDemo.Tests/OwnershipTests.cs), including first/repeat finite console runs. |
| Active solution | Build succeeded, zero warnings/errors | All 11 active projects, audited restore, EF 10.0.12 and Npgsql 10.0.3. |
| Formatting and semantics | Passed | CSharpier, active native style and analyzer checks. |
| Dependency boundaries | Passed | Both restored-graph dependency verifiers. |
| Archive integrity | Passed | All 800 manifest files preserved. |

Total: **61 new passing tests**, with no skips. Container proofs used disposable rootless
Podman PostgreSQL instances with the resource reaper enabled. CI now declares the active
PostgreSQL ownership lane; the remote CI run has not been observed.

The E1 checkpoint hooks separately passed 63 context tests and 21 historical architecture
tests. Those are checkpoint results, not additional E2.1 proofs. Archived persistence tests
were source evidence for planning and were not rerun for this implementation.

## What was proven and rejected

- Cached models use each current context's tenant: the same SKU returns 42/7, foreign IDs
  are absent, and concurrent contexts retain isolation. Disabling only soft deletion keeps
  the ownership filter active.
- Unestablished or tenantless Inventory operations fail on owned reads and saves. Explicit
  global-only saves do not resolve tenancy. The library does not infer global policy.
- Supplied ownership is required and must match the operation. Foreign tracked writes,
  changed ownership and forged originals fail, including with automatic detection disabled.
  All four native public save overloads reach the consumer's validation wiring.
- A detached payload can supply the current tenant while targeting a stored foreign ID.
  In-memory validation alone cannot reject that target. Native ownership concurrency
  predicates reject update/delete with `DbUpdateConcurrencyException`; fresh reads confirm
  that the foreign row remains unchanged. Valid same-tenant detached writes succeed.
- An unchanged owner marked modified by native detached `Update` is accepted. Rejecting
  the modified flag alone would incorrectly reject ordinary native consumer usage.
- Unsupported or altered protected models fail in the exercised cases: wrong key type,
  replaced filter, disabled concurrency token, conversion, ignored insert ownership,
  inheritance, owned/complex mappings, shared tables, entity splitting and views.

A conversion negative test exposed that an explicitly configured provider CLR conversion
is not always visible through `GetValueConverter()`. Validation now inspects the effective
type-mapping converter, and registration also rejects a configured provider CLR type.
The utility explicitly sets and validates before-save behavior `Save`, so native inserts
send the supplied owner. These are supported-model checks, not read interception.

The key expression must read a member of the DbContext instance; captured local keys are
rejected. Model creation does not resolve the operation key. Comparison uses the EF property
value comparer. Native PostgreSQL text/UUID storage is proven; arbitrary collations,
conversions and custom identity semantics require separate proofs.

## Library, template and sample findings

New reusable mechanism proven: explicit ownership-filter construction, supported-model
metadata validation and tracked-write validation combined with native stored-owner
predicates. No repository, unit-of-work abstraction or base DbContext was needed.

Template recipe: copy/edit the ordinary registration, schema/history configuration, model
calls and two native save overrides in the executable consumer. No separate template file
or bootstrap CLI is introduced before that recipe receives review.

Consumer-owned policy: tenant selection and storage-key mapping, model/table classification,
global data, soft deletion, provider, schema, business indexes, membership/permissions,
transaction completion and privileged workflows. Inventory owns its schema and reference
fixtures; they do not claim rich aggregate or reservation behavior. The utility itself
does not require a multitenant context library.

The sample explicitly initializes an empty disposable database with `EnsureCreatedAsync`.
It has no migrations yet. The owner allowed later native development-time migration
scaffolding, with generated migrations remaining reviewed consumer code.

## Review-worthy files

- [Public helpers](../../src/Gloaming.Persistence/Gloaming.Persistence.EntityFrameworkCore/TenantOwnershipExtensions.cs),
  [metadata checks](../../src/Gloaming.Persistence/Gloaming.Persistence.EntityFrameworkCore/TenantOwnershipMetadata.cs),
  [failure enum](../../src/Gloaming.Persistence/Gloaming.Persistence.EntityFrameworkCore/TenantOwnershipFailure.cs),
  [exception](../../src/Gloaming.Persistence/Gloaming.Persistence.EntityFrameworkCore/TenantOwnershipException.cs)
  and [documented contract](../../src/Gloaming.Persistence/Gloaming.Persistence.EntityFrameworkCore/README.md).
- [Inventory DbContext](../../src/Gloaming.Persistence/samples/PersistenceDemo/Inventory/InventoryDbContext.cs),
  [composition](../../src/Gloaming.Persistence/samples/PersistenceDemo/DemoComposition.cs),
  [finite console](../../src/Gloaming.Persistence/samples/PersistenceDemo/Program.cs) and
  [independent GUID context](../../src/Gloaming.Persistence/tests/EntityFrameworkCoreTests/GuidConsumerContext.cs).
- [Current dependency architecture tests](../../tests/ArchitectureTests/AdoptionDependencyTests.cs),
  [CI lanes](../../.github/workflows/ci.yml) and [development commands](../development.md).

The handoff also updates E1 documentation to record its approved checkpoint. Existing E1
library/sample source and archived source/fixtures remain unchanged.

## Limits and remaining gaps

Keep the operation key fixed for a DbContext lifetime, and use the same source for its filter
and validator. The utility cannot prove arbitrary consumer getters agree. Unregistered
entities receive no implied protection; consumer model tests classify all expected tables.

Validation is a save-time check. Deliberately altered/bypassed filters or annotations,
`IgnoreQueryFilters`, raw SQL, bulk operations, foreign entities already tracked and arbitrary
same-process code are outside the supported isolation contract. This is not database RLS.
A stable ownership token does not detect competing edits within the same tenant.

Next E2 work remains two module-owned schemas/contexts, reviewed migrations and history
ownership, separately mapped children and same-tenant relationships, a separate version
token, and richer failure/transaction cases. Shared cross-module transactions require their
own explicit workflow and proof. Membership, actor authorization and trusted HTTP ingress
remain E3. See [the E2 scope inventory](../plans/e2-persistence.md).

## Subsequent tooling update

The original handoff used two Python dependency verifiers with 11 active projects. On
2026-10-04 they were replaced by [a .NET architecture suite](architecture-tests.md), adding
a twelfth active project. Original database proof results above are unchanged; the new
report records architecture verification separately.

## Subsequent test audit

[The active test audit](test-audit.md) reduces E2.1 coverage to 50 current cases: 23 fast,
21 Inventory PostgreSQL and 6 independent GUID PostgreSQL cases. It removes duplicate
save-overload combinations, a private-annotation fault fixture and the dedicated native
rollback demonstration. Original 61-case results remain historical evidence; current
maintained coverage is listed in the audit. Runtime library/sample behavior is unchanged.
