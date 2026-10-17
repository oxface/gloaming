# A2.4: ownership-declared PostgreSQL RLS capability

Implemented for owner review on 2026-10-16, following the
[reference research](a2-4-rls-capability-reference-research.md). The owner authorized the
fluent capability, retaining HasQueryFilter and requiring write protection. This increment
adds an optional library mechanism and independent executable consumer. Earlier A2.2/A2.3
work remains in the same checkout; all changes are unstaged and the existing index is preserved.

Subsequently superseded by the owner-selected [A2.5 transaction refinement](a2-5-transaction-rls-simplification.md):
the session binding and generic native-interceptor registry described below have been replaced.
This report retains the earlier implementation and dated execution evidence.

## Outcome and review surface

The optional [Persistence/PostgreSQL package](../../src/Gloaming.Persistence/Gloaming.Persistence.EntityFrameworkCore.Postgres/README.md)
implements:

```csharp
notes.HasTenantOwnership(row => row.Partition, () => RequiredPartition, "PartitionScope")
    .WithRls(settingName: "consumer.partition_key");

services.AddTenantOwnershipValidation<NotesContext, string>(db => db.RequiredPartition);
services.AddPostgresTenantRowSecurity<NotesContext>();
// Existing scoped native AddDbContext options:
options.UseNpgsql(runtimeConnection).ConfigureGloamingCapabilities<NotesContext>(provider);
```

`RequiredPartition` reads the same operation's immutable Tenancy accessor. No setup call is
needed around ordinary queries or saves. Model metadata stays static and the native builder
return type stays unchanged. The core ownership package gains one read-only metadata query,
`FindTenantOwnershipProperty`, so the optional adapter does not copy private annotation keys.
Initial support is an ordinary unshared table with an unconverted PostgreSQL text owner.

The shared `NativeCapabilityConfiguration` installation seam (removed in A2.5)
adds typed/keyed interceptor registration and a command-time missing-binding guard alongside
the existing save coordinator. The shared package now references EF Relational for those native
contracts, without a provider/feature dependency. Connection and command handlers remain in
the optional feature package; no generic command/transaction/retry executor is introduced.

The [runtime binding](../../src/Gloaming.Persistence/Gloaming.Persistence.EntityFrameworkCore.Postgres/PostgresTenantRegistration.cs)
requires an admitted tenant before opening, verifies a non-authorizing session baseline,
executes a separate parameterized session-level setup command, and binds again on every EF
logical open/reopen. Sync and async native paths are supplied. It starts no transaction and
does not alter EF SQL/result batches. Setup failure/cancellation closes the lease; command
guards reject unbound external connections and tenant changes. Normal reset-enabled Npgsql
pooling is supported; DbContext pooling and transaction-pooling proxies are not this protocol.

The [model/migration helpers](../../src/Gloaming.Persistence/Gloaming.Persistence.EntityFrameworkCore.Postgres/TenantRowSecurityExtensions.cs)
retain ownership filtering/concurrency/write validation and supply explicit migration operations
for ENABLE/FORCE RLS plus a `FOR ALL` policy with both USING and WITH CHECK. Identifiers are
quoted; the qualified lowercase setting name is validated and shared with runtime selection.
The Down operation drops the managed policy and disables RLS, requiring review of any other
policies. **The fluent marker does not scaffold or deploy policies.** Consumers add/review
the helper calls in versioned migrations and maintain the complete table inventory. No
request-time DDL, role creation or automatic model-diff generator is supplied.

## Protection and limitation matrix

These outcomes require deployed matching policies and an unprivileged runtime role.

| Path or condition | Proven protection | Boundary/limitation |
| --- | --- | --- |
| Ordinary EF queries | First query binds the admitted tenant automatically; the existing context-bound filter remains. | No membership admission or resource permissions are inferred. |
| Filters disabled | PostgreSQL returns only the bound tenant's rows. | The declaration alone cannot protect a table whose policy is missing. |
| Tracked INSERT/UPDATE/DELETE | Existing validation remains; native sync/async saves work and generated RETURNING values are preserved. | Ownership is explicitly populated; no automatic stamping. |
| Foreign INSERT | WITH CHECK rejects even when a deliberately faulty consumer disables its EF ownership validator. | Actual PostgreSQL insufficient-privilege outcome, separate from application validation. |
| UPDATE/DELETE targeting a foreign row | USING limits eligible stored rows; bulk/raw/native foreign targets affect zero rows. | Zero affected rows do not identify why a row was unavailable. |
| UPDATE changing the discriminator | WITH CHECK rejects moving a visible row to another tenant. | Authorized tenant transfer is not supplied. |
| Bulk and raw EF SQL | Automatic binding precedes SQL; policies apply when query filters and tracked validation are bypassed. | Arbitrary SQL can mutate a custom GUC; this does not contain a runtime credential holder. |
| Native ADO.NET on an EF-bound lease | Database policies reject foreign write results independently of EF interception. | Direct driver calls are not intercepted; use the owning connection/transaction and do not change its setting/lifetime. |
| Missing or tenantless selected context | Fails before opening/EF SQL. An unbound native connection sees no rows. | Tenantless/global lanes require a separate context/protocol. All EF commands on a selected context require tenancy. |
| Missing selected feature registration | Shared command guard rejects reads/raw writes before business SQL. | Omitting capability configuration bypasses interception and is unsupported integration. |
| Pooled physical reuse | Alpha → Beta → Alpha share a backend, with automatic prepared statements and no tenant carryover; closed leases reset. | Reset-disabled/multiplexed connections are rejected. External poolers are not covered. |
| Commit, full rollback and savepoints | Session binding established before caller transactions survives rollback; callers retain save/commit control. | Do not mix the A2.3 local protocol into this session-bound connection. |
| Setup cancellation/failure | Cancellation after actual open and authorizing role defaults fail closed and close the connection; a fresh scope binds again. | Fresh-scope retry is proven; provider failover/execution-strategy recovery is not claimed. |
| Unselected context | Native commands work without resolving a Tenancy service. | Selecting one table makes the context require a tenant for all its EF commands. |
| Policy removal/recreation | Explicit Down/Up helper SQL changes actual database visibility as expected, including quoted mapped names. | No automatic scaffolding, drift certification, schema-upgrade pipeline or EnsureCreated policy generation. |
| Roles and policy bypass | Actual test runtime is non-owner, without superuser/BYPASSRLS/privileged memberships/schema CREATE, with one populated policy and both table flags. | Consumers provision/audit production roles and extra policies; binding does not certify them on each open. |
| Exact opaque-key comparison | Policy equality explicitly uses PostgreSQL `C` collation; distinct casing remains isolated even on an actual nondeterministic ICU column. | No trimming, case folding or domain-key canonicalization is supplied. |
| Same-tenant permissions and relationships | Existing admission, authorization and ownership checks stay independent. | RLS does not enforce Read/Edit grants, tenant-inclusive foreign keys, unique/RI side-channel restrictions or global/transport policy. |

## Executable consumer and new evidence

[ConsumerDatabase](../../src/Gloaming.Persistence/tests/PostgresTests/ConsumerDatabase.cs)
uses scoped DI, the immutable accessor, ownership validation and `.WithRls` without Wholesale,
Access, HTTP, actor or transport dependencies. Its
[provisioning lane](../../src/Gloaming.Persistence/tests/PostgresTests/ProofDatabase.cs)
creates a disposable database, applies explicit migration operations and grants a separate
restricted login only the necessary schema/table privileges. It deliberately uses alternate
table/column names with quotes and an opaque tenant containing Unicode, a quote and newline.
The native pool has one physical slot with automatic preparation enabled.

New PostgreSQL/declaration proofs: **28 passed, zero skipped**. The
[native lifecycle/write tests](../../src/Gloaming.Persistence/tests/PostgresTests/RowSecurityTests.cs)
and [declaration checks](../../src/Gloaming.Persistence/tests/PostgresTests/DeclarationTests.cs)
cover the matrix above. External reference tests were not run. A2.2/A2.3's earlier results
remain historical evidence and are not counted as these new proofs.

Fresh affected checks (all zero skipped):

| Suite | Passed |
| --- | ---: |
| New ownership/RLS PostgreSQL and declaration proofs | 28 |
| Existing ownership model/validation | 23 |
| Existing Persistence PostgreSQL | 6 |
| EventReactions core | 21 |
| Independent EventReactions consumer | 104 |
| Wholesale CQRS, including prior RLS composition | 62 |
| Existing explicit Tenancy/PostgreSQL utility | 11 |
| Architecture/dependency boundaries | 96 |
| **Total native tests** | **351** |

Active solution compilation passed with zero warnings/errors using a per-invocation
`-p:NuGetAudit=false`; the final adapter/proof build also passed. The normal audited full
solution build is blocked by an existing transitive `MessagePack 2.5.302` NU1902 finding in
`RuntimeComposition.Tests` ([reported advisory](https://github.com/advisories/GHSA-qhrr-8q5h-9q3h)).
No dependency version or repository audit configuration was changed to conceal that result.

Full active style/analyzer verification passed. CSharpier checked 789 files including generated
source; archive verification preserved all 800 original files. Documentation validation resolved
644 local links/anchors across 24 changed Markdown files with no errors. Whitespace checks passed;
the original index and staged diff remain unchanged.

The existing T1 creation proof also passed: two external generated consumers, each with
**5 adoption tests**, plus deterministic creation, namespace `Task`, conflicting parent SDK,
source provenance, omission/failure and publication-race checks. Both generated solutions built
and passed style/analyzers against a dedicated disposable PostgreSQL server. This verifies
compatibility of the shared EF source/dependency snapshot; it does not adopt RLS in the template.

## Reuse, consumer policy and remaining work

A new reusable mechanism is proven for the bounded direct-Npgsql/scoped-EF composition:
ownership-declared RLS selection, feature-owned automatic session binding, missing-binding
checks and explicit read/write policy SQL operations. The independent consumer makes the
composition executable; it is not a general authorization framework or a complete migration
extension. Public interfaces and implementation remain subject to owner line-by-line review.

Consumers retain authentication/admission, fixed operation establishment, ownership population,
permission predicates, roles/privileges, policy deployment and complete table/relationship
inventory. A forged custom setting remains outside the protection boundary. Provider-specific
integration is optional: core Tenancy/ownership/shared EF acquire no Access/FGA dependency and
no reverse dependency on this package. The A2.3 explicit utility remains independent.

No template preset or default sample rollout is added. Sales' existing opt-in executable keeps
its transaction-local setup; ordinary Wholesale HTTP/AppHost/workers remain unchanged. A production
rollout should first prove its exact restricted credentials and complete policy inventory against
one real request lane. Automatic model/snapshot/migration diffing is a separate follow-up, as are
transaction-pooling support, provider failover and unsupported mapping/key shapes.
