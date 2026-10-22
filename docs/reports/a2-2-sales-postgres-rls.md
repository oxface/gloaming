# A2.2: opt-in Sales PostgreSQL RLS proof

Owner-authorized implementation on 2026-10-16, following the
[A2 assessment](tenant-authorization-assessment.md) and committed A2.1 Sales permissions
(`b2f0d01`). The [bounded scope](../plans/a2-2-sales-postgres-rls.md) is implemented on
`feat/sales-postgres-rls`, stacked on that local commit; fetched main is `d35b3ee`.
The checkout is reused. This follow-up remains unstaged and uncommitted.

Subsequent owner-selected [A2.3 extraction](a2-3-postgres-tenant-context.md) moves explicit
context setup into the optional Tenancy/PostgreSQL adapter. This report records the original
consumer-only proof and its handoff checks; current executable/tests use that public utility.

## Outcome and recommendation

The CQRS executable now has an explicit `--tenant-isolation` mode with executable
[consumer setup](../../samples/Wholesale/IntegrationTests/Scenarios/Cqrs/TenantIsolation/README.md).
It uses separate migration-owner and restricted runtime credentials, enables/forces RLS on
five Sales tables and sets a parameterized transaction-local tenant on the exact native
connection before each protected operation. It contrasts one raw database row in each
Organization with Alpha's Sales-authorized query returning one in Alpha and zero in Beta.
The tests also commit an authorized existing-draft edit through the actual Sales Contract.

**Recommendation:** retain this as opt-in consumer defense against missing tenant predicates.
Require explicit role provisioning and transaction ownership before adopting another path.
Keep Sales' within-tenant Read/Edit predicates and current membership admission. Do not enable
blanket RLS under the default AppHost credential or extend business policies to transport
tables. The tests confirm the
[documented owner/superuser/BYPASSRLS behavior](https://www.postgresql.org/docs/18/ddl-rowsecurity.html),
and the existing [primary-source note](tenant-authorization-primary-sources.md) records the
fresh-AppHost superuser default and actual inbox/outbox transaction boundaries.

The mutable tenant GUC is not database authentication. A runtime credential holder can
replace it and read another partition; this is demonstrated with an unchanged immutable
Alpha operation accessor and a forged Beta SQL setting. No containment of arbitrary SQL,
credential compromise, whole-application isolation or production readiness is claimed.

## Precise protection and limitation matrix

Every fresh database is provisioned with real distinct owner/runtime logins. The runtime
has no role memberships, protected-table ownership, superuser/BYPASSRLS/CREATEROLE/CREATEDB
attributes or Sales-schema CREATE privilege. It receives SELECT on customer profiles,
addresses, drafts, lines and grants, plus INSERT/UPDATE/DELETE on the first four. Grant
mutation and transport-table access are denied by ordinary privileges, independently of RLS.

| Boundary/path | Freshly demonstrated protection | Limit or remaining obligation |
| --- | --- | --- |
| Authentication and active membership | Finite trusted demo scopes publish actor/Organization once; SQL setup reads that immutable tenant. | This mode does not authenticate or query Access. Real HTTP/job hosts still admit fresh operations and authorize system mandates. A1.1/A2.1 admission tests are historical evidence, not rerun here. |
| Runtime credential | Effective/session user and catalog checks pass under the actual restricted login. SET ROLE owner, ALTER RLS, TRUNCATE, grant deletion and outbox reads fail `42501`. | The migration owner and admin remain privileged. Role/table checks run at setup/start, not continuously; no complete drift/definer-function audit is supplied. |
| Five-table policy | Each table has ENABLE/FORCE and explicit tenant-equality USING/WITH CHECK. Enabled/no-policy hides rows; disabling RLS exposes both seeded partitions. | New tables and later policies require review. Permissive policies combine with OR and can widen access. [CREATE POLICY](https://www.postgresql.org/docs/18/sql-createpolicy.html). |
| Owner/bypass controls | FORCE makes the ordinary owner see zero rows with no context. NO FORCE restores owner access; superuser and BYPASSRLS see both. Runtime validation rejects the changed bypass role. | FORCE cannot contain owner DDL, superuser or BYPASSRLS. No security claim follows from catalog flags alone. |
| Raw/EF reads | Native SELECT and IgnoreQueryFilters see only the selected tenant's draft/root/line/grant rows. | Same-tenant rows remain visible to raw SQL regardless of Sales grants; no protection for already materialized/tracked/cached values is supplied. |
| Resource permissions | Alpha's Sales query sees its Alpha grant but no Beta resources despite a visible Beta database row. Beta edit without a grant returns NotFound; an Alpha authorized edit saves/commits total 50. | RLS checks no actor, grant, initiator, membership or permission-at-commit condition. Direct DML can bypass business/version/audit protocols within a tenant. |
| USING / WITH CHECK | Foreign raw/bulk targets affect zero rows. Foreign INSERT and owner-changing UPDATE fail `42501`; a hidden-row upsert also fails `42501`. | Hidden existing targets and forbidden proposed values have different outcomes. A zero-row result is not a universal denial signal. |
| Tracked and bulk paths | A native Sales edit commits under RLS. ExecuteUpdate/Delete and raw DELETE remain tenant-scoped with no tracked entries. | This does not add aggregate/version/event staging to bulk SQL or certify third-party bulk tools. |
| Missing / tenantless / unknown | Missing and deliberate tenantless application contexts, or no native transaction, reject setup. Missing/empty/whitespace/unrecognized SQL settings expose zero drafts; protected INSERT fails `42501`. | Opaque nonempty keys are not UUID-validated. An existing tenant value is accepted as trusted input, not checked against admission by PostgreSQL. |
| Physical pool reuse | MaxPoolSize=1 proves the same backend for Alpha commit → Beta rollback → missing. The next lease has no effective tenant and zero rows. | Pool reset is hygiene, not admission. A No Reset On Close control leaks a session setting across leases and is rejected by the supported composition. [Npgsql reset](https://www.npgsql.org/doc/performance.html#pooled-connection-reset). |
| Rollback / cancellation / savepoints | Pre-cancelled setup or post-setup SQL aborts the attempt; explicit rollback removes staged rows and the next same-backend lease is tenantless. Savepoint rollback before setup removes it; a later savepoint retains it. | Cancellation is tested with an already-cancelled token, not every in-flight network/server cancellation race. Reestablish after rollback to a point before setup. [SET lifetime](https://www.postgresql.org/docs/18/sql-set.html). |
| Retries / reconnect / concurrency | A controlled native SQLSTATE `40001` causes two execution-strategy attempts. Each uses a fresh scope/transaction and setup from the same captured actor/tenant facts; only one edit commits. A terminated/cleared session reconnects tenantless. Two concurrent operations use distinct backends. | No automatic default retry, commit-ambiguity/idempotency protocol, EF context pooling, multiplexing, external pooler or shared-context support is added. |
| Native COPY | COPY TO exports only Alpha; runtime binary COPY FROM fails `0A000` under effective RLS. | Privileged imports and other bulk tools need separate protocols. [PostgreSQL COPY](https://www.postgresql.org/docs/18/sql-copy.html). |
| Setting forgery / session baseline | The runtime can set Beta while the operation remains Alpha. A transaction-local Beta setting ending restores an earlier session Alpha setting. | A custom GUC is mutable credential-controlled input. Session/role defaults must not authorize missing context. No injection or compromised-credential boundary is claimed. |
| Constraint disclosure | A hidden Beta customer's globally unique ID cannot be read, but inserting the same ID under Alpha reports `23505`. | Unique/FK integrity checks can reveal existence. Tenant equality is not complete confidentiality or a substitute for tenant-inclusive relationships. [PostgreSQL limitations](https://www.postgresql.org/docs/18/ddl-rowsecurity.html). |
| HTTP / workers / other tables | No changes to established middleware, admission or transport paths. The restricted role has no transport-table privilege. | This mode covers Sales reads/existing-draft edits only. Default runtime credentials, creation/creator grants, Inventory, submission/allowances/reactions, inbox/outbox and workflows are not adopted with RLS. |

## Fresh verification

Executed 2026-10-16 with .NET SDK 10.0.401, EF Core 10.0.12, Npgsql/provider 10.0.3 and
PostgreSQL 18.6 Testcontainers on rootless Podman, with resource reaping enabled.

| Check | Fresh result |
| --- | --- |
| CQRS/PostgreSQL suite | 62 passed, zero failed/skipped: 29 new RLS cases plus all 33 existing cases. |
| Executable consumer | The RunAsync path used by `--tenant-isolation` migrates/seeds/installs and produces both expected output lines with separate credentials. |
| Active solution build | Passed, zero warnings/errors. |
| Architecture | 90 passed, zero failed/skipped; library/module/Contracts boundaries preserved. |
| Native style and analyzers | Both active-solution checks passed with no changes. |
| CSharpier | Passed with `--include-generated`, 775 files checked. |
| Frozen archive | All 800 original files match the retained manifest. |
| Documentation and index | 439 local links/anchors across 13 changed Markdown files resolve; whitespace checks pass. All 21 changed files remain unstaged, and both the index and staged diff match their pre-implementation snapshots. |

Commands and provisioning follow the [development guide](../development.md#optional-sales-postgresql-rls)
and consumer instructions linked above. A2.1's 340 tests and earlier A1/A2 adapter results
remain separately dated evidence; no HTTP/worker RLS guarantee is inferred from them.

## Review-worthy files

| File | Review concern |
| --- | --- |
| [SalesRowSecurity](../../samples/Wholesale/IntegrationTests/Scenarios/Cqrs/TenantIsolation/SalesRowSecurity.cs) | Finite table inventory, explicit policy clauses, ENABLE/FORCE, role-membership/owner/bypass checks and read-only grant privileges. |
| [Extracted tenant setup](../../src/Gloaming.Tenancy/Gloaming.Tenancy.EntityFrameworkCore.Postgres/TenantDatabaseContextExtensions.cs) | A2.3 replaces the original private helper; exact open native connection/transaction, required selected immutable tenant and parameterized local setup remain. |
| [TenantIsolationJourney](../../samples/Wholesale/IntegrationTests/Scenarios/Cqrs/TenantIsolation/TenantIsolationJourney.cs), [Program](../../samples/Wholesale/IntegrationTests/Scenarios/Cqrs/TenantIsolation/TenantIsolationJourney.cs) | Explicit opt-in mode, separate credentials, finite setup/admission, native read transaction and independent Sales permissions. |
| [Boundary proofs](../../samples/Wholesale/IntegrationTests/Queries/TenantIsolationTests.cs) | Actual logins, raw/bulk/Contract paths, bypass controls, missing contexts, forged setting and constraint disclosure. |
| [Lifecycle proofs](../../samples/Wholesale/IntegrationTests/Queries/TenantIsolationTests.Lifecycle.cs), [fixture](../../samples/Wholesale/IntegrationTests/Queries/TenantIsolationTests.Support.cs) | Physical backend reuse, rollback/cancellation/savepoints, whole-attempt retry, reconnect, concurrency and independently provisioned disposable roles. |

## Extraction findings and smallest next proofs

**No new reusable mechanism was proven or added.** Native PostgreSQL policies and Npgsql/EF
transactions suffice for this bounded consumer. No runtime library source, public library
interface, dependency or new package changed. The test friend assembly exposes sample-local
helpers only. Tenancy remains independent of Access/FGA and of the persistence provider.

**Consumer policy:** table inventory, privileges, Organization mapping and immutable admission
are explicit sample decisions. The runtime cannot create creator grants. Do not route the
existing creation/submission Contracts through these credentials without completing their
actual Inventory/grant/allowance/reaction transactions. Production policy deployment needs
its own versioned migrations, drift checks and independently managed credentials.

**Template:** editable role provisioning, explicit begin/setup/query/save/commit and a raw-SQL
negative control are candidates only. T1 generation is unchanged. No generic interceptor,
ambient tenant session, automatic middleware or retry framework is extracted.

The smallest further proof depends on a selected consumer:

1. Adopt one authenticated HTTP read/edit using its actual restricted runtime credential and
   native transaction; prove current membership plus Sales grants and error/cancellation paths.
   Keep middleware placement and immutable actor/tenant/initiator unchanged.
2. Before covering creation or submission, inventory that operation's full table/privilege/
   reaction path and prove grant atomicity or allowance/outbox acceptance under the same role.
3. For a selected worker, separately authorize cross-tenant claim/intake/completion lanes and
   the tenant-specific business transaction; prove actual raw SQL before/after establishment,
   rollback/retry and lease completion without giving all workers business-table bypass.
4. For deployment, verify the complete effective role/policy/view/function inventory and pooler
   semantics. Repeat only the lifecycle proofs affected by that concrete environment.

These are remaining consumer choices, not mandatory library interfaces or an accepted
whole-application RLS rollout. Assessment, implemented proof and future adoption remain distinct.
