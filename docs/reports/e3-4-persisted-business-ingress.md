# E3.4 persisted business ingress and module projects

The owner approved [the plan](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/e3-4-persisted-business-ingress.md) for implementation.
Implementation, executable consumer and relevant proofs are complete for owner review;
all changes remain unstaged and no commit is authorized. E3.3 is checkpointed as `20a02be`.

## Outcome and ownership

The HTTP host now reads actual Inventory-owned PostgreSQL availability rows through an
asynchronous module Contract. The same SKU exists in Alpha/Beta with independently expected
quantities 42/7. A selected Organization becomes a canonical tenant only after Access
admission; Inventory's registered E2 ownership filter then isolates ordinary reads.
No caller-selected tenant key crosses the stock Contract, and no query bypasses that filter.

Access and Inventory each have populated implementation and Contracts projects. Access owns
its global registry/admission reads; Inventory owns its availability rows/query. Rows and
query implementations remain internal. The host owns HTTP mapping, admission metadata,
selection helpers, endpoint policy and response presentation. Business endpoints call
Contracts; native DbContexts/configuration remain public for explicit composition, migration
and setup. [ADR 0004](../adr/0004-module-contracts-and-native-composition.md) records this
owner-approved business/composition boundary, whose implementation is under review.

The existing public catalog preserves its response shape and supports an optional exact SKU
query. Anonymous callers and mapped non-members can read only the selected Organization's
public catalog. A new protected `/organizations/{organization}/stock/{sku}` read requires
native authentication and fresh active membership. Missing SKU is a business 404 after
admission; unknown/inaccessible Organization still gets the generic admission response.
Database faults and cancellation propagate without fixture data or an unscoped fallback.

**No new reusable mechanism was extracted or proven.** This proves that the existing actor,
tenancy and EF ownership mechanisms compose through a real admitted HTTP business read.
Module Contracts, project layout, native registrations/migrations, public-access policy and
finite setup remain editable consumer-owned sample/template code. Technical libraries and
package versions are unchanged; no library dependency or runtime framework layer was added.

## Fresh execution evidence

| Suite | Passed | Evidence |
| --- | ---: | --- |
| HttpIdentityDemo.Tests | 61 | Real PostgreSQL 18.6, native TestServer/cookies, actual module migrations, admitted and public reads, isolation, changed data, revocation, faults/cancellation, retained-schema compatibility and finite CLI setup. |
| PersistenceDemo.Tests | 36 | Existing E2 Inventory/Sales schema, ownership, relationship, version and explicit-transaction consumer proofs. |
| PersistenceTests | 6 | Existing independently adopted GUID ownership consumer on PostgreSQL. |
| ArchitectureTests | 43 | Existing 35 cases plus eight focused real-assembly module/Contracts/host boundary cases. |
| ActorIdentityTests | 19 | Existing independent actor core. |
| TenantTests | 17 | Existing independent tenancy core. |
| ContextDemo.Tests | 15 | Existing independent/combined context composition. |
| EntityFrameworkCoreTests | 23 | Existing EF model and write-validation cases. |
| ActorIdentityAspNetCoreTests | 15 | Existing independent native actor HTTP adapter. |
| TenancyAspNetCoreTests | 39 | Existing independent tenancy HTTP adapter. |
| Total freshly run | **274** | 103 PostgreSQL cases and 171 container-free cases; no failures/skips in these runs. |

The HTTP suite preserves/adapts the previous 49 cases, adds ten catalog/integration cases,
and expands two existing setup/context cases with one additional scenario each. It uses one
persisted host, not a parallel fixture adapter retained for fast hooks.

Route and subdomain protected reads retain the same actor while changing canonical tenant
and quantity; a raw setup update to Alpha is reflected in the next native HTTP read. A SKU
present only in Beta returns business 404 from Alpha's public and protected reads, and is
available in Beta. Twelve overlapping requests exercise the shared EF model with separate
request-scoped contexts and independently expected keys/quantities.

Suspension/removal tests first succeed on protected stock, then commit revocation, reuse the
same valid cookie and make Inventory unavailable. Admission still returns its generic 404,
showing business query work was not entered; an anonymous request retains native 401.
A mapped non-member reads Alpha's public rows while protected Alpha stock is denied.

An actual Inventory table rename returns 500; repair permits a fresh read. Cancellation is
triggered only after PostgreSQL reports the catalog statement waiting behind a table lock,
using a separate autocommit observer. It produces no HTTP result/fallback, leaves the already
established tenant intact, and a fresh read succeeds after lock release. Non-HTTP Contract
calls reject both uninitialized and deliberately tenantless contexts; successful explicit
admission/initialization still reads Alpha's database row.

## Compatibility and explicit setup

The [retained SQL fixture](../../samples/Wholesale/IntegrationTests/Api/Fixtures/README.md)
was generated with native EF 10.0.12 from the checkpointed `20a02be` binary before relocation.
Its migration/history are applied to a fresh database before adding independent existing
user, external identity, Organization and membership witnesses. The relocated Access module
recognizes `20261005204257_InitialAccess`, has no pending migration/model changes and preserves
those witnesses after native migration. Inventory adds its initial schema/history separately;
Access's applied history and successful identity/admission remain unchanged.
This tests a checkpoint schema rather than one generated only from the current model.

`--initialize-demo` explicitly migrates both modules, then stages/saves/commits native seed
transactions. Each Inventory seed has its own established tenant context; no ownership bypass
is used. `--initialize-access` retains its Access-only behavior. Both modes run as real finite
child processes without OIDC settings and exit without HTTP. The full mode's rows support
actual HTTP catalog reads; Access-only mode creates no Inventory history or fixture fallback.
Normal HTTP startup against an empty database creates neither module history and leaves the
explicit anonymous liveness endpoint usable.

Native Inventory migration scaffolding needed the same repository convention adjustments as
Access: file-scoped namespace and a static readonly index-column array. Intermediate builds
also exposed the one remaining synchronous test call, a setup-local naming collision and a
native reflection/ArchUnitNET type alias; these were corrected before successful verification.
No data behavior failure occurred in the complete 61-case PostgreSQL run.

The 22-project active solution builds with zero warnings/errors. Style and analyzer checks,
CSharpier verification (165 files), whitespace and 423 local links in 45 active Markdown
files pass. Archive verification
preserves all 800 original files. CI's existing PostgreSQL lane now names both persisted
Access admission and isolated Inventory reads; hooks still keep container suites separate.
The standalone context console remains independent and runnable.

No archived behavior, broker, remote OIDC, Kestrel or Aspire topology suite was rerun. This
slice does not establish real provider validation/session/proxy behavior or another DBMS's
compatibility; PostgreSQL is the actual relational consumer exercised here.

## Library, template and sample findings

**Library:** existing asynchronous context/admission seams and the explicitly registered EF
ownership filter/write validation suffice. No actor/tenant persistence bridge, additional
middleware, generic repository or unit of work is justified. Independent consumers passed.

**Template:** populated module/Contracts projects, host-owned HTTP bridges, native DI/provider
registration, module-owned migrations/histories and finite explicit setup are exercised
recipes. The architecture rules are editable repository policies, not a product dependency
or an exact project/transitive-package whitelist. Materialized template/bootstrap CLI remains E10.

**Sample:** availability is a small state-stored read view. Access admission and Inventory
isolation are distinct; no cross-module database FK or implementation reference is needed.
Seed helpers stage only; the host owns save/transaction/commit. Public native persistence
permits privileged bypasses, which remain consumer responsibilities rather than a claim of
universal runtime enforcement.

## Review-worthy files and remaining gaps

- [Module ownership](../../samples/Wholesale/modules/README.md),
  [Access registration](../../samples/Wholesale/modules/Access/Access/AccessRegistration.cs),
  [Access Contract](../../samples/Wholesale/modules/Access/Access.Contracts/IApplicationAccess.cs)
  and relocated registry/model/migration files. Review relocation separately from the small
  registration/configuration/public seed changes; the schema/migration ID is preserved.
- [Stock Contract](../../samples/Wholesale/modules/Inventory/Inventory.Contracts/IStockCatalog.cs),
  [query](../../samples/Wholesale/modules/Inventory/Inventory/StockCatalog.cs),
  [DbContext](../../samples/Wholesale/modules/Inventory/Inventory/InventoryDbContext.cs),
  [initial migration](../../samples/Wholesale/modules/Inventory/Inventory/Migrations/20261005214919_InitialInventory.cs)
  and staging/configuration helpers.
- [Host composition](../../samples/Wholesale/Api/DemoComposition.cs),
  [business endpoints](../../samples/Wholesale/Api/Endpoints/OrganizationEndpoints.cs),
  [HTTP admission bridge](../../samples/Wholesale/Api/HttpIntegration/OrganizationTenantResolver.cs)
  and [explicit setup](../../samples/Wholesale/Api/DemoSetup.cs).
- [Catalog/integration proofs](../../samples/Wholesale/IntegrationTests/Api/CatalogTests.cs),
  [setup proofs](../../samples/Wholesale/IntegrationTests/Api/PersistenceTests.cs),
  retained SQL baseline and [module rules](../../tests/ArchitectureTests/SampleModuleBoundaryTests.cs).

Setup supports fresh disposable demonstration databases, not repeat reconciliation or atomic
cross-module setup. This read capability establishes no reservation/quantity mutation rules
or authoritative event-sourced stock model. Revocation remains admission-time, not protection
through a later business transaction. Mutations/native antiforgery/concurrency, actual
OIDC/session/proxy/Aspire topology, membership administration, permissions and shared module
transactions remain separately reviewed capabilities. E3 remains open.
