# A2.3: explicit PostgreSQL tenant-context extraction

Owner-authorized extraction on 2026-10-16 following the
[A2.2 consumer proof](a2-2-sales-postgres-rls.md). The
[selected scope](../plans/a2-3-postgres-tenant-context.md) is implemented on the existing
`feat/sales-postgres-rls` checkout, retaining the uncommitted A2.2 composition. No staging,
commit or push is part of this handoff.

## Outcome

`Gloaming.Tenancy.EntityFrameworkCore.Postgres` exposes one explicit DbContext extension,
`EstablishTenantDatabaseContextAsync(tenancy, settingName, cancellationToken)`. It requires
the operation's selected tenant and caller-owned open native transaction, parameterizes
both the custom setting name and exact opaque tenant value, and checks the returned value.
The original Sales helper is removed; the executable and its proofs now use the public
extension directly. No automatic save/transaction interceptor is introduced.

The optional package depends only on Tenancy core, EF Relational and Npgsql. The explicit
Npgsql 10.0.3 pin matches the already restored driver/provider version. No DI registration,
base DbContext, new abstract interface, mandatory Access/FGA/ActorIdentity dependency or
dependency from Tenancy core/Persistence to this adapter is added.

The [package-local contract](../../src/Gloaming.Tenancy/Gloaming.Tenancy.EntityFrameworkCore.Postgres/README.md)
defines caller responsibilities and failures. The native transaction remains visible:

```csharp
await using var transaction = await database.Database.BeginTransactionAsync(ct);
await database.EstablishTenantDatabaseContextAsync(tenancy, "my_application.tenant_key", ct);
// Protected reads or accepted proposals followed by SaveChanges as needed.
await transaction.CommitAsync(ct);
```

## Proven mechanism and retained limits

The independent consumer references only the adapter and native provider/test packages.
It has a plain DbContext and a `consumer.notes` table with a `partition` discriminator,
`consumer.partition_key` setting and tenant-inclusive key. No Sales/Access model, actor,
ownership utility or save capability is involved. Its runtime authenticates as an actual
separate non-owner/non-bypass login, while fixture administration provisions policies/rows.

Its 11 cases exercise:

- Immutable selected tenant and exact existing transaction retained by setup; Alpha commit,
  Beta rollback and missing context on the same physical backend. Independent stored reads
  confirm the committed change and discarded Beta edit, using separate operation contexts.
- A different configured setting leaves the protected table inaccessible until the matching
  setting is established. An opaque key containing Unicode, quote, newline and dollar text
  is preserved exactly and selects its independently seeded row.
- Missing/tenantless context or no transaction rejects setup without opening/starting anything.
  Null, blank and unqualified setting names fail as configuration errors.
- An aborted native transaction makes setup fail with `25P02`; the error propagates, operation
  tenant remains unchanged, and caller rollback/disposal permits a fresh successful attempt.
  Pre-cancelled setup leaves the next pooled lease without authority.

All 29 original Sales RLS cases now cross this extension. Their actual-role/bypass controls,
native/EF/bulk queries, permission intersection, foreign writes, physical reuse, cancellation,
savepoints, native transient retry, reconnect, concurrent scopes, COPY and setting-forgery
results remain passing after adoption. Those are consumer composition proofs, not automatic
policy/credential verification supplied by the utility.

The mechanism establishes context only. Consumers still own trusted admission, the matching
USING/WITH CHECK policy, complete protected-table inventory, role/membership/privilege review,
pool reset/defaults, deployment drift and every transaction/retry boundary. Custom settings
are mutable input and can be forged with the runtime credential. Within-tenant permissions,
domain/version/audit protocols and already materialized data remain separate. Read-only
operations still require explicit transactions. PostgreSQL local context ends at transaction
completion and may be undone by an earlier savepoint; see the
[source-backed lifetime analysis](tenant-authorization-primary-sources.md#transaction-local-context-and-connection-lifetime).

No default HTTP, worker, creation/submission or transport-table RLS adoption follows. No
sync utility, context pooling, multiplexing, external pooler, ambient/shared transaction,
automatic retry or ambiguous-commit guarantee is added. The current shared SaveChanges
capabilities remain unchanged; automatic transaction interception needs a separate selected
consumer and its own failure/attachment proofs.

## Fresh verification

Executed 2026-10-16 using .NET SDK 10.0.401, EF 10.0.12, Npgsql/provider 10.0.3 and
PostgreSQL 18.6 Testcontainers with rootless Podman and resource reaping enabled.

| Check | Fresh result |
| --- | --- |
| Independent PostgreSQL utility suite | 11 passed, zero failed/skipped. |
| Sales CQRS suite after extraction | 62 passed, zero failed/skipped, including all 29 transferred RLS cases. |
| Tenancy core / HTTP adapter | 17 / 39 passed, zero failed/skipped; independent context/admission behavior retained. |
| Architecture | 93 passed, zero failed/skipped, including three new dependency/adoption cases. |
| Active solution build | Passed, zero warnings/errors. |
| Native style / analyzers | Both active-solution verification commands passed without changes. |
| CSharpier | Passed with `--include-generated`, 780 files checked. |
| Frozen archive | All 800 original files match the retained manifest. |
| Documentation / review snapshot | 587 local links/anchors across 19 changed Markdown files resolve; whitespace passes. All 39 working-tree files, including retained A2.2 work, remain unstaged. The index and staged diff match their pre-extraction snapshots. |

The five suites above total **222 passing tests**. A2.2's earlier counts and A2.1's HTTP/sample
admission results remain separately dated evidence; neither substitutes for new tests of this
public extension. CI configuration now includes the independent suite in the Tenancy lane and
selects that lane for the linked PostgreSQL fixture; CQRS consumer changes select Wholesale.
These are local results; no hosted CI run or publication was triggered.

## Review-worthy files and extraction findings

| File | Review concern |
| --- | --- |
| [Public utility](../../src/Gloaming.Tenancy/Gloaming.Tenancy.EntityFrameworkCore.Postgres/TenantDatabaseContextExtensions.cs) | Single extension, required explicit name, exact operation key/native transaction and two SQL parameters. |
| [Package declaration](../../src/Gloaming.Tenancy/Gloaming.Tenancy.EntityFrameworkCore.Postgres/Gloaming.Tenancy.EntityFrameworkCore.Postgres.csproj) | Only Tenancy core, EF Relational and Npgsql. |
| [Independent proofs](../../src/Gloaming.Tenancy/tests/TenancyPostgresTests/ContextTests.cs), [plain model](../../src/Gloaming.Tenancy/tests/TenancyPostgresTests/ConsumerDatabase.cs), [fixture](../../src/Gloaming.Tenancy/tests/TenancyPostgresTests/ProofDatabase.cs) | Different consumer shape, true restricted login, exact identity and caller-owned lifetime. |
| [Sales adoption](../../samples/Wholesale/IntegrationTests/Scenarios/Cqrs/TenantIsolation/TenantIsolationJourney.cs), [lifecycle proofs](../../samples/Wholesale/IntegrationTests/Queries/TenantIsolationTests.Lifecycle.cs) | Actual public utility usage on every protected attempt; private helper removed. |
| [Dependency declarations](../../tests/ArchitectureTests/AdoptionDependencyTests.cs), [assembly checks](../../tests/ArchitectureTests/AssemblyDependencyTests.cs), [CI](../../.github/workflows/ci.yml) | Optional adoption, no forbidden library/sample dependency and executable coverage. |

**A reusable mechanism is now extracted and independently proven:** explicit PostgreSQL
transaction-context establishment from an immutable Gloaming tenant. One implementation
concentrates native checks and parameterized setup across the Sales and plain consumers.
This updates A2.2's earlier consumer-only finding; it does not turn policy provisioning into
a supported library mechanism.

**Consumer-owned policy:** admission, roles, table inventory, policies, grants, transaction
ownership and product permission semantics. **Template:** explicit begin/setup/query/save/
commit is an editable candidate; T1 generation is unchanged. **Remaining integration:**
automatic native transaction interception and further HTTP/worker adoption require concrete
consumer selection and proof. No generic context/session/transaction framework was introduced.
