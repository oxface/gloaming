# A2 primary-source evidence: optional PostgreSQL tenant isolation

Assessed 2026-10-16 (Europe/Kiev), against fetched main `d35b3ee`. All external sources
below were consulted on that date. **Fact** means official documentation or released
source inspection; **assessment** means an integration recommendation or inference for this
repository. No database was started, role catalog inspected, policy installed or runtime
proof executed for this note. Read with the [A2 assessment](tenant-authorization-assessment.md)
and [A2 brief](../plans/a2-tenant-resource-authorization-rls-research.md).

## Versions and the actual configured credential

The [AppHost](../../samples/Wholesale/AppHost/Program.cs) and
[PostgreSQL fixture](../../tests/Support/PostgreSqlFixture.cs) select **PostgreSQL 18.6**.
Use the PostgreSQL **18** manual below, supplemented by `REL_18_6` source where behavior
needs finer evidence. [Central packages](../../Directory.Packages.props) pin EF Core
**10.0.12**, Npgsql EF **10.0.3** and Aspire PostgreSQL **13.6.1**. Existing restored
`HttpIdentityDemo/obj/project.assets.json` resolves driver **Npgsql 10.0.3**; generated
assets corroborate this checkout, rather than defining a new dependency pin. Npgsql and EF
guides are live documentation; tagged source links below fix the relevant driver behavior.

**Fact:** Aspire 13.6.1 uses `postgres` when `userName` is omitted, including in its
connection-string expression. [Tagged PostgresServerResource](https://github.com/microsoft/aspire/blob/v13.6.1/src/Aspire.Hosting.PostgreSQL/PostgresServerResource.cs).
The official PostgreSQL image initializes `POSTGRES_USER` as a superuser, defaulting to
`postgres`; initialization environment variables only affect an empty data directory.
[Official image initialization contract](https://hub.docker.com/_/postgres).

**Assessment from source configuration:** the AppHost omits `userName` and passes the
same database reference to the API and setup command. A fresh, unmodified default
installation consequently gives runtime code a superuser credential that bypasses RLS.
`WithDataVolume` preserves existing catalog state, and externally supplied connection
strings can differ. There is no live-role evidence here: production/effective credentials,
memberships, table owners and grants must be inspected before claiming enforcement.
Adding `FORCE ROW LEVEL SECURITY` alone cannot fix a superuser runtime role.

## Role and policy facts

| Primary-source fact | Assessment consequence |
| --- | --- |
| Superusers and `BYPASSRLS` roles bypass RLS. Owners normally bypass; `FORCE ROW LEVEL SECURITY` subjects owners to ordinary RLS checks. Owners can change/disable policies. [PostgreSQL 18 row security](https://www.postgresql.org/docs/18/ddl-rowsecurity.html) | Use a separate runtime role with no ownership, superuser or bypass attributes and no path to administer the protected table. Keep migration/setup authority separate. FORCE is defense against accidental owner execution, not containment of a table owner who can alter the table. |
| The released enforcement code checks bypass privilege before ownership/FORCE; referential-integrity checks have an internal owner exemption even under FORCE. `row_security=off` makes an affected ordinary query error; it does not grant bypass. [18.6 check_enable_rls](https://github.com/postgres/postgres/blob/REL_18_6/src/backend/utils/misc/rls.c) | Catalog flags alone do not establish that the actual query is protected. Prove the effective role on each connection and each deliberate privileged path. |
| Object privileges can be inherited; a membership chain with `SET TRUE` permits becoming its role. [Role membership](https://www.postgresql.org/docs/18/role-membership.html), [SET ROLE](https://www.postgresql.org/docs/18/sql-set-role.html). Owner checking uses `has_privs_of_role`, not only literal owner-name equality. [18.6 object_ownercheck](https://github.com/postgres/postgres/blob/REL_18_6/src/backend/catalog/aclchk.c) | Audit inherited owner privileges and reachable privileged roles, not only `rolsuper`/`rolbypassrls` on the login. Starting as an admin and temporarily using a restricted role leaves a route back for the same credential holder. |
| A policy exists independently of whether RLS is enabled. Disabled RLS adds no row restriction. Enabled RLS with no applicable policy denies ordinary rows. [CREATE POLICY](https://www.postgresql.org/docs/18/sql-createpolicy.html) | An omitted policy can deny access; an omitted ENABLE, a new unprotected table or a bypass role can expose rows. Deployment drift is a separate failure class. |
| Applicable permissive policies combine with OR; restrictive policies combine with AND, and at least one permissive policy must allow a row. [18.6 add_security_quals/add_with_check_options](https://github.com/postgres/postgres/blob/REL_18_6/src/backend/rewrite/rowsecurity.c) | A later permissive policy can broaden an earlier tenant policy. Review the complete effective policy set. A restrictive tenant condition is one possible composition only alongside an explicit permissive allowance; no such schema is selected here. |
| `USING` controls existing-row visibility/targeting; false/null normally suppresses rows. `WITH CHECK` validates proposed insert/update values and errors on false/null. For ALL/UPDATE, omitted CHECK reuses USING. Checks see values after BEFORE triggers. SELECT policies can additionally apply to modifying statements/RETURNING. [CREATE POLICY](https://www.postgresql.org/docs/18/sql-createpolicy.html) | Prove both row selection and attempted owner changes, including trigger behavior and RETURNING. A hidden row/zero affected rows is not a reliable application-level “permission denied” signal. |

**Assessment:** tenant equality restricts the Organization partition admitted by trusted
application code. It does not authenticate that Organization, verify active membership,
or decide which private resources within it an actor can read/change. Permission-aware
lists, counts, exports and AI selection still require the A1 module-owned predicates and
complete authorization contract. No Tenancy → Access/FGA dependency follows from these facts.

## Transaction-local context and connection lifetime

**Fact:** `set_config(name, value, true)` applies during the current transaction;
`current_setting(name, true)` returns SQL NULL for an unknown setting. Without
`missing_ok=true`, an unknown setting errors. Passing NULL as the new setting value resets
it to its default; it does not create a SQL-null setting.
[Configuration functions](https://www.postgresql.org/docs/18/functions-admin.html#FUNCTIONS-ADMIN-SET).
`SET LOCAL` ends at commit or rollback and is canceled by rollback to an earlier
savepoint. Outside a transaction block, `SET LOCAL` warns and has no effect. Ending a
local setting reveals any pre-existing session setting again.
[SET](https://www.postgresql.org/docs/18/sql-set.html).

**Fact:** arbitrary valid two-part custom setting names become user-settable string
placeholders. A placeholder can remain known with an empty effective value: the released
getter renders a null/empty string value as `''`. Do not assume a previously used setting
becomes an unknown setting after reset/transaction end.
[18.6 add_placeholder_variable, GetConfigOptionByName and ShowGUCOption](https://github.com/postgres/postgres/blob/REL_18_6/src/backend/utils/misc/guc.c).
**Assessment:** an application-populated custom tenant setting is trusted session input,
not an unforgeable database identity. An attacker who can execute arbitrary SQL using
that role can supply another valid tenant value. RLS with such a setting helps against
omitted row predicates, not full credential compromise or every SQL-injection path.

**Fact:** Npgsql normally pools physical connections; closing a logical connection returns
it for reuse. [Npgsql basic usage](https://www.npgsql.org/doc/basic-usage.html#connections).
The reset is normally queued and sent with the first subsequent user statement; prepared
statements use equivalent reset operations instead of discarding them.
[Pooled connection reset](https://www.npgsql.org/doc/performance.html#pooled-connection-reset).
`No Reset On Close` defaults to false; enabling it permits state leakage.
[Npgsql connection settings](https://www.npgsql.org/doc/connection-string-parameters.html).
Tagged 10.0.3 code rolls back an outstanding transaction before queuing its ordinary
reset, and treats multiplexing separately.
[NpgsqlConnector.Reset/Open](https://github.com/npgsql/npgsql/blob/v10.0.3/src/Npgsql/Internal/NpgsqlConnector.cs).

**Fact:** EF context pooling and driver connection pooling are independent. EF normally
opens just before an operation and closes afterward; context reset generally does not
reset underlying driver state. A pooled context's `OnConfiguring` runs once, so request
tenant state cannot be assigned there per lease.
[EF pooling and state guidance](https://learn.microsoft.com/en-us/ef/core/performance/advanced-performance-topics).
**Assessment:** one initialization when constructing a DbContext, opening a physical
connection for the first time, or creating a request scope cannot establish every later
transaction's context. Existing fixture `Pooling=false` proofs establish no physical
connection reuse guarantee.

**Proposed consumer protocol, unimplemented:** begin an explicit native transaction on
the exact connection used by protected SQL; establish/validate the immutable operation's
tenant with a parameterized transaction-local setting before its first protected command;
run all participating queries/writes there; commit/rollback/dispose. Reject required-context
setup failure. Otherwise nontransactional protected reads need an explicit transaction
too: an autocommit setup command's local value expires before the next command. Every new
transaction/connection/reconnect/retry requires setup again. Do not rely on pool reset as
the admission protocol, and do not allow session defaults to authorize missing context.
If multiplexing or an external pooler is proposed, prove its actual transaction/session
binding separately; ordinary Npgsql-pool evidence does not cover that deployment.

**Fact:** EF creates a savepoint before SaveChanges inside an existing transaction and
rolls back to it on failure; shared EF contexts must share both native connection and
transaction. [EF transactions](https://learn.microsoft.com/en-us/ef/core/saving/transactions).
**Assessment:** establish tenant context before any savepoint that a recoverable save
can roll back to. A rollback to a point preceding setup invalidates it. Rolling back a failed
database attempt and retrying a new transaction must use the same immutable operation context;
it must not silently rebind tenant/actor. Fresh membership admission belongs to a fresh
operation according to consumer policy, separately from replaying a database attempt.

**Fact:** Npgsql's default execution strategy reports `RetriesOnFailure=false`.
[Tagged default strategy](https://github.com/npgsql/efcore.pg/blob/v10.0.3/src/EFCore.PG/Storage/Internal/NpgsqlExecutionStrategy.cs).
The provider offers opt-in `EnableRetryOnFailure`.
[Provider execution strategy](https://www.npgsql.org/efcore/misc/other.html#execution-strategy).
EF retrying strategies require user transactions to be replayed as one delegate; a
connection loss during commit can leave the outcome unknown.
[EF retry and commit guidance](https://learn.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency).
**Assessment:** put transaction creation and context setup inside each retry attempt.
Retrying business effects/accepted audit needs its existing atomicity and ambiguous-commit
contract; tenant context setup does not supply idempotency. Cancellation/setup errors must
abort the attempt, not continue with fallback tenant state.

## Missing, tenantless and error outcomes

These are **proposed semantics to prove**, not an implemented policy. Tenant keys in
Gloaming remain opaque strings; current Wholesale Organization keys are text such as
`wholesale-alpha`, not UUIDs. If a different consumer later compares a UUID owner against a parsed
custom setting, its missing/empty/malformed cases differ:

| Input or database result | Precise source-backed behavior and proposed handling |
| --- | --- |
| Unknown setting | `current_setting(..., true)` returns NULL. Equality against NULL cannot yield true. An ordinary USING match is suppressed and CHECK fails if evaluated. Reject missing operation context before protected work; database denial is a second boundary. [Configuration functions](https://www.postgresql.org/docs/18/functions-admin.html#FUNCTIONS-ADMIN-SET), [SQL comparison nulls](https://www.postgresql.org/docs/18/functions-comparison.html) |
| Known empty setting | It can be `''`, not NULL. A deliberate `NULLIF(value, '')` maps it to NULL; this is a proposed policy choice. [18.6 string setting getter](https://github.com/postgres/postgres/blob/REL_18_6/src/backend/utils/misc/guc.c), [NULLIF](https://www.postgresql.org/docs/18/functions-conditional.html#FUNCTIONS-NULLIF) |
| Nonempty invalid UUID text | A UUID cast raises `22P02` when evaluated; NULLIF does not validate arbitrary text. A query with no qualifying evaluation need not raise that error. Validate the trusted mapping first, and prove failure behavior rather than promising every read throws. [18.6 uuid_in/string_to_uuid](https://github.com/postgres/postgres/blob/REL_18_6/src/backend/utils/adt/uuid.c), [SQLSTATE codes](https://www.postgresql.org/docs/18/errcodes-appendix.html) |
| Deliberate tenantless context | It is a consumer operation state, not a PostgreSQL feature or permission to read every partition. Keep global pre-admission Access tables and independently authorized maintenance paths explicit. Do not encode tenantless as an unrestricted policy default. |
| RLS CHECK violation | The released executor raises insufficient privilege (`42501`), while ordinary USING suppression can return an empty result/zero affected rows. EF may wrap write failures; do not equate a database error with an HTTP 403 automatically. [18.6 ExecWithCheckOptions](https://github.com/postgres/postgres/blob/REL_18_6/src/backend/executor/execMain.c), [SQLSTATE codes](https://www.postgresql.org/docs/18/errcodes-appendix.html) |

## SQL paths and residual limits

| Path | Verified database behavior and limit |
| --- | --- |
| EF filters disabled, raw SELECT/INSERT/UPDATE/DELETE | RLS is enforced during database query rewriting for the effective role/table, regardless of the .NET caller. [18.6 get_row_security_policies](https://github.com/postgres/postgres/blob/REL_18_6/src/backend/rewrite/rowsecurity.c). **Assessment:** this can cover an omitted EF predicate, but only with valid role/policies/context on that command's connection. It does not validate aggregates, accepted audit or outbox staging. |
| ExecuteUpdate/Delete and other bulk SQL | EF executes these immediately outside tracking/SaveChanges and does not automatically apply tracked concurrency handling. [EF bulk operations](https://learn.microsoft.com/en-us/ef/core/saving/execute-insert-update-delete). **Assessment:** effective RLS can restrict normal database DML; bulk tools remain unproven until their actual SQL, credentials, connection, transaction and staging paths are inspected. |
| Already materialized/tracked/cached data | **Assessment:** RLS cannot retract rows already present in process memory or require a query when an identity-map/cache lookup serves a value. Operation immutability and fresh-scope rules remain essential; tenant isolation does not establish within-tenant resource revocation. |
| INSERT ON CONFLICT | INSERT checks apply even if the row ultimately is not inserted; the UPDATE path also checks existing-row USING and proposed-row CHECK, and can error instead of silently skipping. SELECT/RETURNING rules also matter. [CREATE POLICY per-command rules](https://www.postgresql.org/docs/18/sql-createpolicy.html). Do not infer ordinary UPDATE error semantics for an upsert. |
| COPY/import/export | Under effective RLS, table COPY TO applies SELECT policies; COPY FROM is unsupported, and PostgreSQL recommends INSERT instead. [COPY](https://www.postgresql.org/docs/18/sql-copy.html). Npgsql binary import/export is COPY, not a different authorization boundary. [Npgsql COPY](https://www.npgsql.org/doc/copy.html). A privileged import succeeding demonstrates its privilege, not safe ordinary-role bulk support. |
| Views and functions | Default views use the view owner's base-table RLS context; `security_invoker=true` uses the caller's. Functions independently use SECURITY INVOKER by default or SECURITY DEFINER ownership. [CREATE VIEW](https://www.postgresql.org/docs/18/sql-createview.html). Definer functions need controlled search_path and execute grants; new functions grant EXECUTE to PUBLIC by default. [CREATE FUNCTION](https://www.postgresql.org/docs/18/sql-createfunction.html). Audit reachable definer/view paths before claiming isolation. |
| Constraints, relationships and table-wide operations | Unique/primary-key and referential-integrity checks bypass RLS and can disclose existence through errors; TRUNCATE and REFERENCES are outside RLS. PostgreSQL also documents policy-subquery races and optimizer treatment of leakproof functions. [Row-security limitations](https://www.postgresql.org/docs/18/ddl-rowsecurity.html). Tenant equality is not a substitute for tenant-inclusive relational constraints. TRUNCATE needs its own privilege. [TRUNCATE](https://www.postgresql.org/docs/18/sql-truncate.html). |
| Cross-tenant transport orchestration | **Repository fact:** inbox selection and handler work share one native transaction; selection precedes handler establishment, and failure deferral uses a new transaction after rollback. [Inbox processor](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/PostgresInboxProcessor.cs), [storage](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/PostgresInboxStorage.cs). Intake uses raw SQL in its caller's transaction. [Inbox intake](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/PostgresInbox.cs). Outbox claim/completion each own a transaction and reject a caller's transaction. [Outbox dispatcher](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/PostgresOutboxDispatcher.cs). **Assessment:** a business-handler setting cannot cover earlier/different transactions. Blanket tenant RLS on these tables needs a separately authorized dispatch/intake design; bypassing business-table policies for every worker is not justified. |

## Smallest useful follow-up evidence

These are candidate proofs only; no new reusable mechanism was proven here.

1. **Role/table boundary:** one consumer-owned tenant table and tenant-inclusive relationship,
   a separate migration owner and restricted application login, actual catalog/effective-role
   assertions, alpha/beta/missing reads and writes, owner mutation, enabled/no-policy,
   disabled-policy and privileged-role controls. Use both EF and direct SQL; demonstrate
   that mutable tenant input can be forged by the credential holder to bound the threat model.
2. **Lifecycle:** a physical pool forced to one reusable connection, alpha → beta → missing
   after commit, rollback, cancellation, context-setup failure and reconnect. Include an
   explicit read transaction, savepoint rollback both before/after setup, one transient
   retry and a control with `No Reset On Close=true`. Verify context before each protected
   attempt; inspect physical reuse rather than assuming it. Add DbContext pooling only as
   its own state-lifetime variant.
3. **Real paths and business separation:** one raw/bulk/upsert/COPY boundary and one actual
   worker selection → admitted handler → same-transaction completion / new-transaction deferral path. Prove a
   same-tenant actor remains denied a private resource through complete selection/count/export
   even when tenant RLS allows its rows. Keep global Access admission usable without fabricated
   tenant state. These establish consumer setup first; only repeated independent adoption can
   justify a later narrowly reviewed package/interface proposal.
