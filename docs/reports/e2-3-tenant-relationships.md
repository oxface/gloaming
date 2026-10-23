# E2.3 same-tenant parent/child relationships

2026-10-04. Implemented after the owner approved proceeding from E2.2 checkpoint `f2dcf2b`.
Changes remain unstaged and uncommitted for review. [The slice plan](../plans/e2-3-tenant-relationships.md)
records the authorized scope and consumer choices.

## Outcome and guarantees

Sales now owns a separately mapped `CustomerAddressReference`: GUID ID, Organization key,
customer ID and address line. Both customer and address explicitly register the existing
ownership utility. The parent keeps its GUID primary key and gains an alternate key
`(OrganizationKey, Id)`; the child foreign key `(OrganizationKey, CustomerId)` references it.
An Alpha-owned address pointing to a Beta customer's ID fails at the database even when
the parent is not loaded. Row ownership validation and relationship integrity are separate
guarantees; neither establishes membership or permission.

The consumer selects native restricted deletion and explicitly removes child rows before
deleting a customer. The incremental `CustomerAddresses` migration adds the alternate key,
address table, index and foreign key. Initial Sales/Inventory migrations are unchanged;
existing E2.2 customers can migrate forward and acquire addresses. Inventory's model and
history configuration are unchanged.

The finite sample explicitly populates, saves and reads one fixture address per customer.
First/repeat runs produce six expected lines, including Alpha's `42 Market Street` and
Beta's `7 Dock Road`. These are persistence fixtures, not complete customer aggregates or
a general address-management application.

## Fresh proofs and findings

Seven new real PostgreSQL cases exercise the actual consumer configuration:

| Proof | Observed outcome |
| --- | --- |
| Detached parent-ID insertion, tenant-filtered children and same-tenant reparenting | Beta cannot read Alpha's child; its own child saves. Alpha can change its address to another Alpha customer, with a fresh read confirming the relationship. |
| Foreign-parent insertion and detached update (two cases) | A child carrying current tenant Alpha plus a Beta customer ID fails through the named composite foreign key. Fresh reads show no extra child or changed relationship/address line. |
| Child ownership registration/save wiring | A foreign-owned child is rejected by the utility before saving; a fresh Beta scope reads no inserted child. |
| Restricted customer deletion | Deleting the customer without loading its child is rejected. A fresh scope confirms the parent remains; explicit child removal followed by parent deletion succeeds. |
| Detached customer compatibility | Updating the display name still works with ownership in the customer's alternate key. |
| E2.2-to-E2.3 migration | A customer inserted under `InitialSales` retains its ID/code/name after the incremental migration, accepts an address and retains the initial plus new history entries. |

The existing two fast module-policy cases now classify the child and inspect the actual
required, tenant-bearing relationship and restricted deletion setting. Migration inspection
admits the actual new unique-key operations and validates the address foreign key's schema,
principal table, column pairing and deletion action. Snapshot/model consistency remains
checked without a database. No generic migration parser or synthetic framework-failure
tests were introduced.

Foreign-parent writes produced PostgreSQL `23503` (`foreign_key_violation`); restricted
parent deletion produced `23001` (`restrict_violation`), both naming our actual constraint.
The first sample run failed because the deletion assertion incorrectly expected `23503`.
It was corrected to the native RESTRICT code, and all 31 sample cases passed on rerun.
These are provider outcomes, not new library error translations. See
[PostgreSQL's error codes](https://www.postgresql.org/docs/18/errcodes-appendix.html).

## Extraction and consumer ownership

**Library:** no interface or implementation changed, and no new reusable mechanism was
proven. E2.1's ordinary single-table ownership utility already works for the independently
mapped child and the parent with an alternate key. A relationship facade, automatic child
enrollment or base DbContext is unnecessary for this proof.

**Template:** copy/edit the native alternate/principal/foreign-key mappings and reviewed
incremental migration from the Sales consumer. Register ownership on each protected mapped
entity explicitly. Consumer model/artifact policies can check the chosen relationships;
they are not a universal runtime or mandatory library policy.

**Sample:** Sales owns customer/address fixture language, required fields, IDs, relationship,
restricted deletion, explicit child removal and fixture population. No Contracts or business
rules were moved into technical libraries. The sample has no cross-module EF relationships.

Alternate-key components are immutable in native EF. Changing the customer's Organization
may fail during native change detection before the library returns its typed ownership
failure. The implementation does not translate that exception or weaken the constraint.
Detached same-owner customer edits were freshly proven; no general tenant-transfer behavior
is supported. See [native alternate keys](https://learn.microsoft.com/en-us/ef/core/modeling/keys)
and [principal/foreign keys](https://learn.microsoft.com/en-us/ef/core/modeling/relationships/foreign-and-principal-keys).

## Verification

All seven active suites passed: **126 cases, no failures or skips**, including **37 real
PostgreSQL cases** on disposable PostgreSQL 18.6 Testcontainers with resource reaping enabled.
The final sample rerun is the passing result; its earlier failed assertion is recorded above.

| Suite | Passed |
| --- | ---: |
| ActorIdentity | 19 |
| Tenancy | 17 |
| ContextDemo | 15 |
| EF model/validation | 23 |
| Architecture/model/migration policies | 15 |
| PersistenceDemo PostgreSQL | 31 |
| Independent GUID PostgreSQL | 6 |

The active 12-project build passed with zero warnings/errors; the corrected sample test
project was also rebuilt successfully. Active style and analyzer checks passed, and CSharpier
checked 79 files including generated migration code. All 800 archived files passed checksum
verification. Archived suites were not rerun; their evidence remains historical. Existing
active CI lanes cover these tests; remote CI has not been observed.

## Review files and remaining gaps

Start with [Sales mapping](../../src/Gloaming.Persistence/samples/PersistenceDemo/Sales/SalesDbContext.cs),
[the child fixture](../../src/Gloaming.Persistence/samples/PersistenceDemo/Sales/CustomerAddressReference.cs)
and [explicit console usage](../../src/Gloaming.Persistence/samples/PersistenceDemo/Program.cs). Then review
[the incremental migration](../../src/Gloaming.Persistence/samples/PersistenceDemo/Sales/Migrations/20261004162310_CustomerAddresses.cs),
its designer and updated Sales snapshot, [relationship proofs](https://github.com/oxface/modulith-foundry/blob/dd6eb77a4a0e3c8afd7af710d7689ca265d43a64/src/Gloaming.Persistence/samples/PersistenceDemo.Tests/CustomerRelationshipTests.cs)
and [sample schema policies](../../tests/ArchitectureTests/ModulePersistenceTests.cs).
[The consumer recipe](../../src/Gloaming.Persistence/samples/PersistenceDemo/README.md) describes mapping
and lifecycle obligations. Existing migration/console proofs now include the address table/output.

This establishes a consumer-owned relationship constraint on PostgreSQL, not authorization,
hostile-code isolation, a cross-module association or multi-provider compatibility. Raw SQL
and privileged ownership changes remain outside the ordinary ownership utility contract.
EF owned types, inheritance, table splitting and complex types remain unsupported.

Restricted deletion is a selected sample policy, not a library mandate. Explicit removal in
the positive test uses separate saves and establishes no new operation-level atomicity.
Downgrade execution, concurrent relationship workflows and production migration coordination
are not claimed. Initial E2.1 `EnsureCreated` fixtures still require recreation.

E2.4 remains separate version conflict detection and failure/rollback through an actual
consumer operation. Shared cross-module transactions require their own workflow and review.
Trusted HTTP ingress, Access membership and later event/messaging capabilities remain planned.
