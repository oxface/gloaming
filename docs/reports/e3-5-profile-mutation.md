# E3.5 Sales profile mutation

Status: implemented for owner review; new changes remain unstaged. The owner-approved
[plan](../plans/e3-5-profile-mutation.md) supplies scope/interfaces, not commit authorization.
The owner's existing 73-file staged E3.4 set was preserved unchanged throughout this slice.

## Outcome and ownership

A mapped human with active Organization membership can read and edit a customer profile
through Sales.Contracts. The HTTP host performs native authorization, actor establishment,
fresh tenant admission and explicit native antiforgery validation before business work.
Sales requires established tenancy, validates its inputs and owns begin/two saves/commit/
rollback in ordinary C#. Success returns the committed profile/version; missing or foreign
customer/address pairs return 404; stale/competing versions return 409.

Sales and Sales.Contracts are populated consumer projects alongside Access and Inventory.
Sales's initial native migration creates only its own schema/history. Both customer/address
rows use the existing ownership utility; a tenant-bearing FK prevents a foreign-Organization
parent and code uniqueness is per Organization. This demonstration permits one address per
customer. Version is a native concurrency token starting at 1 and is incremented explicitly.
The caller's expected version is the original UPDATE value even after a newer server reload.
Rows and business implementation remain internal. Business endpoints depend on Contracts;
public DbContexts remain available to explicit composition, migrations and finite setup.

The native antiforgery endpoint issues a non-cacheable cookie/request-token pair without
selecting an Organization. Its host-only cookie is HttpOnly, Secure and SameSite.Strict.
The explicitly attached PUT filter validates native protection; no transaction/retry/save
is hidden in a filter. Native additional data binds tokens to the established human ActorId,
resolved from request services rather than captured by a singleton provider. Tokens are
portable between Organizations for the same actor only after independent fresh admission.

## Fresh executable evidence

All results below are fresh runs against the active implementation. Archive/E2 historical
reports and pinned ASP.NET source findings were used as references, not substituted for
these results. The new HTTP suite uses native protected cookies, real HTTP token issuance,
TestServer HTTPS semantics and PostgreSQL 18.6 Testcontainers with resource reaping enabled.

| Guarantee | Evidence |
| --- | --- |
| Committed edit | Version 1 read, protected PUT, version 2 response and fresh name/address read; Beta remains unchanged before its independently admitted edit. |
| Tenant and pair isolation | Foreign customer, foreign address and same-tenant other-customer address return 404 without changing Alpha/Beta witnesses. Direct read/change reject uninitialized or tenantless contexts. |
| Native protection before Sales | Missing/invalid token or cookie returns 400 even with Sales unavailable; fresh business read remains unchanged. Invalid GUID/version/text/length inputs return 400; Contract misuse throws argument exceptions. |
| Actor rather than claim-only binding | An Alpha token fails for Beta. Trusted setup reassigns the exact provider link while retaining the exact authentication cookie/principal: the newly established Beta actor is admitted to Beta but the old token fails. Fresh Beta token succeeds. |
| Admission remains independent | Anonymous, unmapped, non-member and revoked callers are rejected before unavailable Sales. Subdomain mutation uses the same Sales path; public catalog access does not grant profile authority. |
| Caller version and actual race | A later stale request conflicts after reload. A SHARE table lock lets two writers read version 1 and blocks both UPDATE statements; release yields one 200, one 409, one version increment and intact winner values. |
| Failure after first save | A test-only address CHECK rejects the second save. HTTP 500 leaves both rows/version unchanged; removing the fault permits a fresh operation. |
| Controlled cancellation before commit | SHARE address lock permits SELECT and blocks the address UPDATE after the customer save. Autocommit pg_stat_activity observes the wait; cancellation plus request completion proves rollback. Fresh read while the lock remains held sees both original rows; fresh edit succeeds after release. |
| Native schema and setup | Real migration rejects cross-tenant parents, zero versions and duplicate same-tenant codes. Applying Sales preserves existing Access/Inventory identity/catalog witnesses and exact histories. Full setup runs without OIDC and serves seeded profiles; Access-only setup and ordinary startup create no Sales history. |
| Module boundary | ArchUnitNET inspects actual Sales/Contracts assemblies alongside the other modules: implementations cannot depend on peers/host, Contracts cannot expose EF/HTTP/contexts, technical libraries cannot depend on sample types, business endpoints call Contracts. |

The first expanded HTTP run passed 78 cases and failed 14 during setup with PostgreSQL
`53300` (too many clients): per-database idle connection pools accumulated across disposable
hosts. The HTTP test host now disables pooling for its disposable databases; production
registration remains unchanged. The full rerun passes all 92. This repair makes no claim
about production pool behavior and adds no native-pooling tests.

| Suite | Passed |
| --- | ---: |
| HTTP sample / PostgreSQL | 92 |
| Finite PersistenceDemo / PostgreSQL | 36 |
| Independent EF ownership / PostgreSQL | 6 |
| Architecture | 49 |
| Actor core | 19 |
| Tenant core | 17 |
| Context sample | 15 |
| EF ownership model | 23 |
| Standalone actor HTTP | 15 |
| Standalone tenancy HTTP | 39 |
| **Total** | **311** |

134 PostgreSQL cases and 177 container-free cases passed, with no skipped tests. Relative
to E3.4 this adds 31 HTTP cases and six architecture cases; existing setup/startup proofs
also gained Sales assertions. All 24 active projects build with zero warnings/errors.
The context console, native style/analyzer checks, CSharpier and archive integrity checks
pass. CSharpier checked 184 files; 454 local Markdown links in 47 active documents resolve.
The archive verifier reports all 800 original files unchanged.

Commands use `DOTNET_PROCESSOR_COUNT=4`, native `dotnet test --project ... --no-build
--no-restore`, and the [documented Podman socket prefixes](../development.md) for all three
PostgreSQL suites. Build uses `dotnet build ModulithFoundry.slnx --no-restore
--disable-build-servers -m:1`; style/analyzers use `dotnet format ... --verify-no-changes
--no-restore`; formatting uses `dotnet csharpier check . --include-generated`. Native
`dotnet ef migrations add InitialSales --project samples/Wholesale/modules/Sales/Sales
--output-dir Migrations` generated the new consumer migration; namespace/layout and constant
index arrays were adjusted to repository conventions. No archived migration was imported.

## Library, template and sample findings

**Library:** existing actor/tenant accessors, HTTP publication/admission order and EF
ownership suffice for this mutation. **No new reusable mechanism was proven or extracted.**
Actor-bound native antiforgery is a concrete integration candidate; another useful adoption
must justify promotion. No technical library changed or gained a Sales/Access dependency.

**Template:** populated module/Contracts projects, native provider/history registration,
explicit endpoint filter, actor-aware token provider and visible transactions are exercised
source recipes. Materialization and bootstrap CLI remain E10; this slice adds no parallel
template implementation. Authentication, authority, cookie configuration, result/status
mapping and migration execution remain editable consumer choices.

**Sample:** Sales owns profile/version/address behavior. Access owns fresh membership
admission and Inventory retains its availability read. The active-member edit permission,
one-address restriction, demo IDs and failure outcomes are sample policy, not technical
library invariants. There is no general result abstraction, mediator, save middleware,
ambient/shared transaction promise, role system or automatic audit/event population.

## Review entry points and remaining gaps

Review [Sales Contracts](../../samples/Wholesale/modules/Sales/Sales.Contracts/ICustomerProfiles.cs),
[profile results](../../samples/Wholesale/modules/Sales/Sales.Contracts/CustomerProfile.cs),
[transaction implementation](../../samples/Wholesale/modules/Sales/Sales/CustomerProfiles.cs),
[model](../../samples/Wholesale/modules/Sales/Sales/SalesDbContext.cs),
[actor binding](../../samples/Wholesale/Api/HttpIntegration/ActorAntiforgeryData.cs),
[filter](../../samples/Wholesale/Api/HttpIntegration/ValidateAntiforgeryFilter.cs),
[endpoints](../../samples/Wholesale/Api/Endpoints/CustomerProfileEndpoints.cs),
[composition](../../samples/Wholesale/Api/DemoComposition.cs),
[finite setup](../../samples/Wholesale/Api/DemoSetup.cs),
[mutation proofs](../../samples/Wholesale/IntegrationTests/Api/ProfileTests.cs) and
[persistence proofs](../../samples/Wholesale/IntegrationTests/Api/ProfilePersistenceTests.cs).

Failed contexts are discarded, without retry/rebase. General lost-response cancellation
cannot prove noncommit; no durable receipt/idempotency mechanism exists. Revocation remains
admission-time policy: admitted work may finish after later removal. No membership lock
spans the Sales transaction. Shared cross-module transactions, audit and customer/address
management need separate capabilities. Sales migration compatibility with the independent
E2 demo database is not claimed.

Actual OIDC login/callback, browser cookie behavior, TLS handshake, proxy/subdomain session
topology and Aspire ServiceDefaults remain E3 work. These local proofs do not establish
production provider/deployment compatibility. Membership management/invitations/linking/
permissions and further extraction decisions remain later Access work. No archive, broker,
remote provider or Aspire topology suite was rerun.
