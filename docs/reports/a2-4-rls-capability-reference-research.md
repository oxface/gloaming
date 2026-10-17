# A2.4: declarative PostgreSQL RLS capability references

Research date: 2026-10-16. This note supports review of the owner's preferred
`HasTenantOwnership(...).WithRls()` direction. That spelling is a proposal for Gloaming,
not an existing API established by the references below. Research changes no runtime,
public interface, migration, template or sample behavior. **No new reusable mechanism
was proven.**

**Recommendation:** the owner's fluent chain fits Gloaming's model-selected capability
design. Bytefish supplies the connection-hook precedent; the closest inspected complete
declaration/schema/runtime implementation is Marten's PostgreSQL RLS support.
Finbuckle supplies a useful EF model-declaration comparison, but its shared-database
isolation mechanism is a query filter and tracked-write enforcement. EF's official custom
migration example supplies the provider extension pattern; it does not implement an RLS
model-to-migration pipeline. These distinctions matter when estimating what Gloaming would
actually have to implement and prove.

Subsequent implementation: the owner authorized the fluent capability with retained EF filters
and explicit write protection. The [implementation report](a2-4-ownership-rls-capability.md)
records the initial code and executable evidence. The later owner-selected
[A2.5 transaction refinement](a2-5-transaction-rls-simplification.md) replaces its session/registry
wiring. Statements below describe the research baseline.

## Evidence and repository baseline

Source inspection used these immutable revisions; the linked official documentation was
also read. Public source files were downloaded read-only under
`/tmp/a2-4-rls-references`. External test source was inspected selectively, not executed.

| Reference | Inspected revision | Relevant scope |
| --- | --- | --- |
| Marten | [`8285a143d8aef5dcb5bbd094cde9802040d9b072`](https://github.com/JasperFx/marten/tree/8285a143d8aef5dcb5bbd094cde9802040d9b072) | .NET/PostgreSQL implementation, fluent RLS selection, schema deltas and connection lifetimes; this is not EF Core. |
| Finbuckle.MultiTenant | [`v10.1.4`, `a1e1391a802396360d05d4dadb6075b1736ed5a1`](https://github.com/Finbuckle/Finbuckle.MultiTenant/tree/a1e1391a802396360d05d4dadb6075b1736ed5a1) | EF Core fluent model selection, named query filter and specialized builder. |
| EF Core | [`v10.0.12`, `001bfac21c1910cdf1110ca256c9ea0a06f374ca`](https://github.com/dotnet/efcore/tree/001bfac21c1910cdf1110ca256c9ea0a06f374ca) | Native connection/transaction interception ordering and SaveChanges transaction creation. |
| Npgsql / EF provider | [`v10.0.3`, `d3768398c17877b3a916c3c4d87e8e11698991fc`](https://github.com/npgsql/npgsql/tree/d3768398c17877b3a916c3c4d87e8e11698991fc) / [`v10.0.3`, `5e912bf071068a44e5abbdab497b313ba99d3ee1`](https://github.com/npgsql/efcore.pg/tree/5e912bf071068a44e5abbdab497b313ba99d3ee1) | Physical pool reset, native batching and EF result consumption. |

The repository versions are EF Core 10.0.12, Npgsql/provider 10.0.3 and PostgreSQL 18.6.
The existing [shared EF integration](../../src/Gloaming.EntityFrameworkCore/README.md)
selects scoped Prepare/Validate work at SaveChanges. It starts no transaction and currently
supplies no connection or transaction capability. The separate
[A2.3 PostgreSQL utility](../../src/Gloaming.Tenancy/Gloaming.Tenancy.EntityFrameworkCore.Postgres/README.md)
establishes parameterized transaction-local context explicitly after a caller begins a
native transaction. Its documented guarantees and earlier proof results remain existing
evidence, not new test results from this research.

## Concrete comparisons

### Marten: fluent selection plus schema and runtime mechanisms

**Source-backed facts.** Marten exposes store-wide `UseRowLevelSecurity()` and mapping-level
`Schema.For<T>().MultiTenanted().UseRowLevelSecurity(...)` /
`DisableRowLevelSecurity()`. The fluent mapping records stable selection and setting-name
overrides. Mapping resolution requires conjoined tenancy and combines the explicit override
with store configuration. This is an actual implemented chain, rather than an API inferred
from a search result. ([Pinned fluent implementation](https://github.com/JasperFx/marten/blob/8285a143d8aef5dcb5bbd094cde9802040d9b072/src/Marten/MartenRegistry.cs#L859),
[mapping resolution](https://github.com/JasperFx/marten/blob/8285a143d8aef5dcb5bbd094cde9802040d9b072/src/Marten/Schema/DocumentMapping.cs#L246).)

Its `RlsPolicySchemaObject` generates ENABLE/FORCE RLS and one named policy with both
`USING` and `WITH CHECK` against the tenant column. It queries PostgreSQL catalogs to
compare flags and the policy's setting reference, and generates a replacement when they
do not match. Disabling its managed RLS drops that policy and disables both table flags.
This goes beyond writing initial CREATE POLICY SQL: removal and configuration changes
have schema-delta behavior. ([Pinned schema object](https://github.com/JasperFx/marten/blob/8285a143d8aef5dcb5bbd094cde9802040d9b072/src/Marten/Storage/RlsPolicySchemaObject.cs).)

Runtime setup is separate. The applier executes parameterized
`SELECT set_config(@name, @value, false)` on the session connection. Transactional lifetimes
apply on opening and reset before closing; the external-transaction adapter applies before
either commands or native batches and resets on disposal. Those are session settings,
including when a transaction is present. They do not demonstrate Gloaming's current
transaction-local protocol. ([Applier](https://github.com/JasperFx/marten/blob/8285a143d8aef5dcb5bbd094cde9802040d9b072/src/Marten/Internal/Sessions/Rls/RlsSessionVariableApplier.cs),
[transaction lifetime](https://github.com/JasperFx/marten/blob/8285a143d8aef5dcb5bbd094cde9802040d9b072/src/Marten/Internal/Sessions/Rls/RlsTransactionalConnection.cs),
[external transaction](https://github.com/JasperFx/marten/blob/8285a143d8aef5dcb5bbd094cde9802040d9b072/src/Marten/Internal/Sessions/Rls/RlsExternalTransaction.cs).)

Marten documents two particularly relevant limits: a per-document GUC differing from the
store GUC needs consumer-supplied setup, and event-store tables do not receive these document
policies. Its tests contain migration-change/removal and restricted-role cross-tenant
read cases; reading those tests does not transfer their guarantees to Gloaming.
([Pinned documentation](https://github.com/JasperFx/marten/blob/8285a143d8aef5dcb5bbd094cde9802040d9b072/docs/documents/multi-tenancy.md),
[test source](https://github.com/JasperFx/marten/blob/8285a143d8aef5dcb5bbd094cde9802040d9b072/src/DocumentDbTests/MultiTenancy/row_level_security_with_conjoined_tenancy.cs).)

**Assessment.** Borrow the explicit declaration and coherent schema lifecycle. Review runtime
setup independently against Gloaming's existing native transaction ownership. A store setting
and a per-table setting can diverge; a new capability should validate that its migration and
runtime configuration agree. Marten's catalog check recognizes a known policy/setting shape;
it is not a proof that no additional permissive policy or role can bypass the intended rule.

### Finbuckle: fluent ownership metadata is distinct from database RLS

**Source-backed facts.** `Entity<MyEntity>().IsMultiTenant()` returns a specialized builder
with tenant-aware key/index helpers. In v10.1.4 the implementation records a model annotation,
requires a string `TenantId` property (shadow if needed), and installs a named tenant query
filter parameterized through the current DbContext. Its builder can append the discriminator
to keys, related foreign keys and indexes. These inspected implementations generate EF model
shape and query filtering; they contain no PostgreSQL policy DDL or connection-context setup.
([Versioned documentation](https://www.finbuckle.com/MultiTenant/Docs/v10.1.4/EFCore),
[pinned entity extension](https://github.com/Finbuckle/Finbuckle.MultiTenant/blob/a1e1391a802396360d05d4dadb6075b1736ed5a1/src/Finbuckle.MultiTenant.EntityFrameworkCore/Extensions/EntityTypeBuilderExtensions.cs),
[specialized builder](https://github.com/Finbuckle/Finbuckle.MultiTenant/blob/a1e1391a802396360d05d4dadb6075b1736ed5a1/src/Finbuckle.MultiTenant.EntityFrameworkCore/MultiTenantEntityTypeBuilder.cs).)

**Assessment.** The specialized return type is a concrete precedent for extending an ownership
declaration fluently. It does not establish database enforcement. Gloaming must retain its own
opaque-key/property-mapping semantics and existing write validation rather than silently
adopting Finbuckle's fixed string property, automatic tenant assignment or mismatch modes.

### EF Core: supported custom migration operations, with an extra model-diff obligation

**Source-backed facts.** Microsoft's custom migration example offers two paths: a helper that
emits `MigrationBuilder.Sql(...)`, or a custom `MigrationOperation` interpreted by the provider's
`IMigrationsSqlGenerator`. Its typed-operation example uses SQL generation helpers for identifier
delimiting and literal generation. Registration replaces the provider generator; this is a
provider integration point requiring composition review. ([Official custom migration example](https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations/operations).)

**Assessment.** A typed policy operation can produce reviewable versioned DDL. Merely adding a
model annotation or migration helper does not establish that `dotnet ef migrations add` detects
policy creation, removal, discriminator changes, schema/table/column renames and setting changes.
That complete model/snapshot/diff/code-generation path is a separate implementation and proof
obligation. `EnsureCreated` behavior also needs an explicit supported-or-rejected contract.

### Bytefish: the requested connection-interceptor approach

**Source-backed facts.** Bytefish's 2021-12-05 sample registers a `DbConnectionInterceptor`
with both opened callbacks and sends a session-level SET. Tenant comes from a header lookup
via an AsyncLocal holder. The code interpolates the value, skips setting when the holder is
absent, and explicitly configures `Pooling=false`. SQL provisioning is separate; the customer
discriminator has a database default and its CLR model omits that member. The linked sample
repository was not publicly retrievable during this pass, so these observations use the
code embedded in the [author's article](https://www.bytefish.de/blog/aspnetcore_multitenancy.html).

**Assessment.** The hook fits the proposed capability. Use the already-admitted scoped
accessor, parameterized setup and missing-context rejection. Retain explicit ownership and
the EF filter/write guards. Do not infer pooled safety or automatic ownership stamping from
this sample; those are different Gloaming choices needing their own proofs. Bytefish's
runtime example does not supply a fluent model-to-policy migration capability.

## Native lifetime and interception constraints

The following are source-backed boundaries; the proposed responses are assessments.

| Boundary | Evidence | Consequence for a proposed automatic capability |
| --- | --- | --- |
| Connection-open setup is not transaction-start setup. | EF opens the connection before beginning a native transaction, and skips opening callbacks for an already-open connection. [EF 10.0.12 connection/transaction source](https://github.com/dotnet/efcore/blob/001bfac21c1910cdf1110ca256c9ea0a06f374ca/src/EFCore.Relational/Storage/RelationalConnection.cs#L332). | A local setting established only in `ConnectionOpened` cannot establish every later transaction on that connection. Pre-opened and caller-supplied connections need a deliberate contract. |
| Native transaction callbacks precede publication through EF's `CurrentTransaction`. | `TransactionStarted` and `TransactionUsed` run before the relational wrapper is created/assigned, on both sync and async paths. [Pinned source](https://github.com/dotnet/efcore/blob/001bfac21c1910cdf1110ca256c9ea0a06f374ca/src/EFCore.Relational/Storage/RelationalConnection.cs#L332). | Calling the existing A2.3 DbContext extension directly inside these callbacks would encounter its current-transaction precondition. A hook needs a reviewed internal path using the callback's native transaction, while retaining A2.3's public contract. |
| SaveChanges need not create an EF-managed transaction. | EF's batch executor skips a new transaction for an eligible single batch/command under the normal `WhenNeeded` behavior; ambient/enlisted cases have other paths. [EF batch executor](https://github.com/dotnet/efcore/blob/001bfac21c1910cdf1110ca256c9ea0a06f374ca/src/EFCore.Relational/Update/Internal/BatchExecutor.cs#L60). | A transaction-start interceptor alone does not establish context for every save, read or bulk operation. Decide whether protected EF commands require an explicit transaction and reject otherwise. |
| Local configuration ends or can be rolled back independently of an in-memory readiness flag. | PostgreSQL local settings end on commit/rollback; rolling back before setup undoes it and any prior session value can reappear. [PostgreSQL 18 SET](https://www.postgresql.org/docs/18/sql-set.html), [set_config](https://www.postgresql.org/docs/18/functions-admin.html#FUNCTIONS-ADMIN-SET). | Re-establish on every transaction attempt; a context-lifetime boolean cannot certify transaction/savepoint state. Setup before ordinary EF savepoints preserves the existing preferred order. |
| Native ADO.NET batching is its own path. | `NpgsqlBatch` can run statements in one round trip and supplies an implicit transaction when none exists. This is driver behavior. [Npgsql batching documentation](https://www.npgsql.org/doc/basic-usage.html#batching). | An implicit server/driver transaction is not evidence that EF's `TransactionStarted` callback ran. Direct commands, batches and COPY need explicit same-transaction usage or an unsupported boundary. |
| Adding a result-producing setup statement changes EF's expected batch shape. | Npgsql's EF batch consumer asserts the protocol statement count matches modification commands and consumes per-statement results/row counts. [Provider 10.0.3 batch consumer](https://github.com/npgsql/efcore.pg/blob/5e912bf071068a44e5abbdab497b313ba99d3ee1/src/EFCore.PG/Update/Internal/NpgsqlModificationCommandBatch.cs#L72). | Do not assume SQL-prefixing `SELECT set_config(...)` is transparent. Prefer a separate parameterized native setup command; any SQL/result rewriting would need provider-specific proofs. |
| Pool reset is a physical-connection reuse boundary. | Reset is arranged at close and normally sent with the next user command, using `DISCARD ALL` or equivalent statements preserving prepared statements. `No Reset On Close` disables reset. [Npgsql documentation](https://www.npgsql.org/doc/performance.html#pooled-connection-reset), [10.0.3 reset source](https://github.com/npgsql/npgsql/blob/d3768398c17877b3a916c3c4d87e8e11698991fc/src/Npgsql/Internal/NpgsqlConnector.cs#L2711). | Reset-enabled pooling complements local settings; it does not establish a tenant or repair authorizing session defaults within the same open connection. Pooling, DbContext pooling and external poolers are separate contracts. |

EF interception has separate sync/async methods and some interceptor types are singleton
services. EF also prohibits parallel use of one DbContext. Any scoped tenant-aware extension
must preserve those rules, the current capability installer's context identity and idempotent
registration, and its current exclusion of pooled contexts/singleton options. An EF interceptor
does not observe arbitrary commands executed directly on an ADO.NET connection.
([Official interceptor contracts](https://learn.microsoft.com/en-us/ef/core/logging-events-diagnostics/interceptors),
[DbContext concurrency contract](https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/#avoiding-dbcontext-threading-issues).)

## Fit with Gloaming's added-capability model

This is a recommendation, not implemented support. The more complete interface proposal
lives in [the extraction plan](../plans/library-extraction.md#a24-ownership-declared-postgresql-rls).
The desired chain can preserve today's ownership declaration and add an explicit RLS choice:

```csharp
entity.HasTenantOwnership(
        row => row.OrganizationKey,
        () => RequiredOrganizationKey,
        "OrganizationScope")
    .WithRls(settingName: "app.tenant_key");
```

A parameterless final call is possible with an explicit context-level setting. Configuration
must have one authoritative name shared by policy deployment and runtime binding. Do not
introduce a sample-specific default, or assume per-table setting overrides are applied by
a binder configured for another name.

| Piece | Recommended Gloaming responsibility | Existing behavior retained |
| --- | --- | --- |
| `HasTenantOwnership` | Continue declaring mapped ownership and its save requirement. | Context-bound EF filter, ownership concurrency and tracked-write validation; ordinary usage stays provider-independent. |
| `WithRls` | Verify ownership exists, record static table/column/key/setting metadata and a distinct RLS requirement. | No tenant/accessor/provider/current DbContext captured in the cached model; existing builder return shape can be retained. |
| Provider binding | Explicit typed runtime registration plus native interceptors installed through capability configuration. Missing selected bindings fail before EF business SQL. | Same immutable admitted tenant, scoped DbContext, native queries and caller-owned save/commit. No shared-save callback can substitute for connection/transaction lifecycle. |
| Policy deployment | Reviewed versioned ENABLE/FORCE and USING/WITH CHECK work matching the selected table inventory. | Separate migration authority and actual restricted-role checks. A declaration does not prove deployed enforcement. |

Today [capability configuration](../../src/Gloaming.EntityFrameworkCore/Gloaming.EntityFrameworkCore/CapabilityConfiguration.cs)
installs one SaveChanges interceptor. It would need a focused native-interceptor installation
extension; merely adding a new marker to the existing Prepare/Validate registry cannot cover
reads. The provider's hook implementation should remain feature-owned, with a small shared
installation/selection seam rather than a generic operation/transaction executor. Runtime
binding should use a typed current-context getter or the same scoped accessor as the model's
key source, never a compiled getter capturing the first DbContext used to build the model.

For the owner's ordinary-request ergonomics, the first candidate is a connection-open
binding using parameterized session context under direct reset-enabled Npgsql pooling.
That can cover ordinary autocommit EF reads without owning a new transaction. The chosen
tenant remains fixed while the logical connection is open; every logical open/reopen binds
again. Initial support should guard or reject pre-opened/external connections, missing or
tenantless selected contexts, failed setup, disabled reset, multiplexing and authorizing
session defaults. These are proposed contract choices, not already-proven lifecycle behavior.

Transaction-local setup remains the alternative for explicitly owned transactions and
transaction-pooling proxies. PostgreSQL's local lifetime and EF's native callback ordering
above prevent treating it as an interchangeable implementation of connection-open session
setup. PgBouncer explicitly distinguishes session features from transaction pooling.
([Official feature matrix](https://www.pgbouncer.org/features.html).)

Provider-specific fluent integration belongs behind an optional adapter: core Persistence
and Tenancy must not acquire a mandatory provider or Access/FGA dependency. Keep the current
A2.3 utility independently usable without ownership mapping. Exact package placement and a
supported ownership-metadata seam still need interface review; copying private annotation
keys or changing HasTenantOwnership's public return type is not required by the desired chain.

The explicit per-entity marker also avoids blanket policies over every ownership-mapped
transport/audit table. Parent/child tables require their own declarations and tenant-inclusive
relationships. Authentication, membership admission and within-tenant grant predicates
remain separate consumer rules. Custom GUCs remain forgeable by the runtime credential;
automatic interception does not strengthen that threat model.

## Remaining proof obligations and ownership

An extraction candidate is stable per-table RLS metadata plus coherent migration lifecycle,
and a separately selected scoped adapter that binds context before protected work at every
connection lease or native transaction required by its explicit lifetime mode. This research
does not implement that interface or make the shared save coordinator a connection-lifetime
host. The existing A2.3 explicit utility remains independently useful.

Before claiming support, a bounded implementation needs executable independent consumer usage
covering:

- Migration creation, application, reversal and re-scaffolding without a spurious next diff;
  policy/configuration removal, mapped names, schema changes, quoting and the supported table
  shapes. Reject unproven inheritance, splitting/owned-table or discriminator conversion shapes.
- Native sync/async reads, tracked saves with RETURNING/concurrency results, multiple saves,
  raw SQL and bulk DML; explicit transactions, adopted transactions or their rejection; missing
  tenant/context/binding and commands outside the permitted transaction protocol.
- Setup failure/cancellation, retries from fresh state, reconnect, commit/full rollback and
  rollback across setup savepoints; physical pool reuse with prepared statements and rejected
  reset-disabled/unsupported pooling configurations.
- Restricted runtime credentials with actual positive/negative read and write outcomes;
  enabled/forced flags, both policy expressions, extra permissive policies, owner/superuser/
  BYPASSRLS/membership routes, complete table inventory and tenant-inclusive relationships.
- A mixed-capability context and an unselected context proving installation/ordering and
  omission, with no scoped object captured in cached model metadata and no tenant rebinding.

Consumers continue to own admission, intra-tenant permissions, roles and privileges, migration
authority, table/relationship inventory and global/transport lanes. A mutable custom setting
does not authenticate a tenant against someone who can submit arbitrary SQL with the runtime
credential. Template adoption needs its own composition proof; sample business policy remains
sample-owned. The [existing A2.2 report](a2-2-sales-postgres-rls.md) and
[A2.3 report](a2-3-postgres-tenant-context.md) describe their separately dated evidence.

## Verification and outcome

This increment inspected first-party documentation and pinned implementation/test source.
No .NET or external suite was run, and no implementation or template files were changed.
Documentation validation resolved 601 local links/anchors across the 21 changed Markdown
files and passed whitespace checks. The existing index and staged diff remained unchanged;
all edits in this increment remain unstaged.
The review-worthy artifact is this note, particularly the Marten schema/runtime split and
the EF callback ordering and batch-result constraints. The references establish real design
precedents and native API boundaries; the proposed Gloaming integration and all new lifecycle
guarantees remain unproven. **No new reusable mechanism was proven.**
