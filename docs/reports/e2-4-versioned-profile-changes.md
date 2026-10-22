# E2.4 versioned profile changes and caller-owned transactions

2026-10-04. E2.3 was owner-reviewed and checkpointed as `d67c7fc`. The owner authorized
proceeding to this next slice. E2.4 was subsequently owner-reviewed and checkpointed as
`6069c05`, completing the initial supported E2 scope. The results below describe that checkpoint.

## Outcome

Sales now has an explicitly initialized and advanced customer Version, independent of the
stable Organization ownership token. Consumer-owned `CustomerProfileChanges.ApplyAsync`
coordinates a customer name and address change through two native saves in a transaction
already opened by its caller. The caller explicitly commits or rolls back; native EF
reports stale writes without automatic retries or exception translation.

**No new reusable library mechanism was proven.** Native EF concurrency tokens and
transactions supplied the primitives needed here. All runtime changes are in the consumer;
the ActorIdentity, Tenancy and EF ownership library interfaces and implementations are
unchanged. No custom persistence scope, unit of work, base context or runtime interceptor
was justified.

## Decisions and obligations

- New customers explicitly receive Version 1. The consumer maps a required `long` native
  concurrency token with `ValueGenerated.Never`. A database default of 1 backfills existing
  rows in the incremental `CustomerVersions` migration; it does not advance versions.
- The operation supplies the request's expected version as EF's original token, even when
  the server loads a newer row. It explicitly assigns the checked next version, saves the
  name, then saves the selected customer's address. Both lookups use ordinary tenant-filtered
  queries; neither supplies ownership nor authorizes the caller.
- The operation rejects a call without an existing Sales transaction. It does not begin,
  commit or roll back that transaction. The returned version describes staged changes.
- Use a dedicated operation context without unrelated pending changes. On failure, explicitly
  roll back and discard it; database rollback does not reset accepted tracker state. The
  console uses `CancellationToken.None` for cleanup rather than the canceled request token.
- Relevant writers must check and advance the customer version to participate in this
  protocol. Direct child writes do not advance it automatically. Existing fixture writes
  are not a universally enforced aggregate protocol. Admission and input policy stay local.

## New executable evidence

Five new cases call the actual sample operation against disposable PostgreSQL 18.6:

| Guarantee | Observed evidence |
| --- | --- |
| Caller controls commit | A bare operation call is rejected. After both saves, a fresh scope sees the old profile until the caller commits; afterward it sees the new name, address and Version 2. Beta remains unchanged. |
| Request version matters | Two callers observe Version 1. The winner commits; a new server context loads the current row for the stale caller, but the request's original Version 1 still produces `DbUpdateConcurrencyException`. Winner values remain intact. |
| Partial-write rollback | The first save reaches PostgreSQL. The second hits the real required-address constraint (`23502`); a no-tracking query inside the transaction confirms the first name/version write. Explicit rollback restores the original profile as read from a fresh scope. A fresh operation then commits successfully. |
| Cancellation cleanup | A native test-only save interceptor cancels the request immediately after its first successful save. The next save is canceled; an independent query confirms the first SQL write, then explicit rollback restores the original profile. No cancellation hook is present in runtime wiring. |
| Existing data upgrade | A database at `CustomerAddresses` contains a customer/address without a Version column. The new migration preserves both, backfills Version 1 and records the actual module history; the new operation then commits Version 2. |

The existing console proof now exercises explicit transactions on first invocation and checks
the same eight output lines on repeat invocation, including Version 2 for each Organization.
Console fixture setup promotes drafts only when their values differ. Already-matching data
upgraded from an older slice can remain at Version 1; this is not general idempotency.

The existing two container-free model/artifact cases also check the native version token,
explicit generation/default configuration, actual module-local column operations and a
matching model snapshot. They do not add a custom migration parser or synthetic framework
tests. The earlier relationship-upgrade proof now seeds the literal older column shape
through parameterized SQL because the current EF model includes Version. This is active
historical-fixture setup; archived source and fixtures are unchanged. Initial migrations,
the previous relationship migration and all Inventory artifacts are unchanged.

## Verification

All seven active suites passed: **131 cases, no failures or skips**, including **42 real
PostgreSQL cases**. These are new executions of the current implementation; E2.3's 126/37
counts remain historical.

| Suite | Passed |
| --- | ---: |
| ActorIdentity | 19 |
| Tenancy | 17 |
| Context consumer | 15 |
| EF ownership model/interface | 23 |
| Architecture/model/migration policies | 15 |
| Wholesale PostgreSQL consumer | 36 |
| Independent GUID PostgreSQL consumer | 6 |

The complete active solution built with zero warnings/errors. Native EF scaffolding produced
the consumer migration and snapshot without a database. Semantic style and analyzer checks
passed; CSharpier checked 84 files including generated artifacts. All 800 original archived
files passed checksum verification. Whitespace and changed-document local links were checked.
No archived runtime suites were rerun for E2.4.

## Review-worthy files and extraction findings

Start with [the operation](../../src/Gloaming.Persistence/samples/PersistenceDemo/Sales/CustomerProfileChanges.cs),
[its request](../../src/Gloaming.Persistence/samples/PersistenceDemo/Sales/CustomerProfileChange.cs),
[the caller](../../src/Gloaming.Persistence/samples/PersistenceDemo/Program.cs),
[the customer](../../src/Gloaming.Persistence/samples/PersistenceDemo/Sales/CustomerReference.cs) and
[the mapping](../../src/Gloaming.Persistence/samples/PersistenceDemo/Sales/SalesDbContext.cs).
Then review [the incremental migration](../../src/Gloaming.Persistence/samples/PersistenceDemo/Sales/Migrations/20261004170258_CustomerVersions.cs),
its generated designer/snapshot, [the PostgreSQL proofs](../../src/Gloaming.Persistence/samples/PersistenceDemo.Tests/CustomerProfileTests.cs)
and [the model/artifact policy](../../tests/ArchitectureTests/ModulePersistenceTests.cs).
Existing seed helpers and the console expectation also change for the new required version.

Library finding: ownership validation composes with a separate native version token; it
does not advance it or coordinate saves. Template finding: copy/edit the native transaction
recipe in [the sample guide](../../src/Gloaming.Persistence/samples/PersistenceDemo/README.md), including
failure disposal and cleanup. Sample finding: the coordinating operation, version protocol,
fixture defaults and draft promotion are consumer policy. No separate template file or
reusable transaction wrapper is needed by this evidence.

## Remaining gaps and next direction

Shared cross-module transactions need a named workflow and explicit shared-connection/
enlistment/failure proof. Ambiguous commit outcomes, retries, production migration
coordination, downgrade execution and other database providers remain unproven. This is
single-module operation atomicity, not a distributed transaction or complete domain model.

The next main extraction increment is E3 trusted ingress and consumer-owned Access:
native authentication/authorization integration, independent actor/tenancy establishment,
Organization selection and admission/membership policy. Its exact first capability and
public interfaces require a separate plan/review. Persistence refinements can return when a
real workflow requires them; this slice supplies no reason to pre-build a generic transaction
abstraction. See [the E2 inventory](../plans/e2-persistence.md) and
[the overall extraction plan](../plans/library-extraction.md).
