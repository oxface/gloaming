# A2: tenant authorization and optional PostgreSQL RLS assessment

Assessment: 2026-10-16. Code baseline: fetched `origin/main`
`d35b3ee24205e33244add4c61ee69430b4192454`, including merged A1 (`b3e1c11`) and
A1.1 (`d35b3ee`). Prepared on `docs/tenant-authorization-assessment` in the existing
checkout. [Brief](../plans/a2-tenant-resource-authorization-rls-research.md),
[A1 recommendation](access-authorization-assessment.md),
[A1.1 implementation/proofs](a1-1-membership-suspension.md),
[PostgreSQL/Npgsql primary sources](tenant-authorization-primary-sources.md).
This is an assessment and recommendation for review, with no runtime implementation,
policy/migration, middleware reordering, new interface or accepted ADR.

## Recommendation

Keep the existing HTTP composition and immutable operation contexts. Use late imperative
resource checks after tenant admission, backed by the owning module's relational permission
predicates for complete queries. Enforce equivalent checks at non-HTTP capability entry
points. Access owns current human membership admission; each business module owns its
resource permissions. A1.1's membership-administration grant gives no private business-data
permission. Tenant equality alone cannot decide which records an admitted member may see.

Retain EF tenant filters, tracked-write validation, same-tenant relationships, concurrency
and native transaction/audit protocols. Optional PostgreSQL RLS can add database enforcement
against omitted tenant predicates and EF bypass paths under a constrained runtime role.
Do not adopt it as a current guarantee: the default fresh local composition uses the
PostgreSQL superuser, runtime/setup credentials are shared, and no RLS policies or database
context protocol exist. Transport polling also precedes tenant establishment.

If the consumer wants database enforcement, start with one consumer-owned PostgreSQL proof
over ordinary business parent/child tables, a separate migration owner and a non-owner,
non-superuser, non-BYPASSRLS runtime role. Set transaction-local context on each exact native
transaction before its first protected statement, including read transactions and retries.
Leave the global Access registry and cross-tenant messaging lanes explicitly outside that
first proof; their security and integration remain separate, named obligations. Do not
claim whole-application isolation from the bounded proof.

No new package is recommended. Keep Tenancy core package-free and independent of Access,
EF and FGA. A shared setup helper is only an extraction candidate after a second actual
consumer demonstrates repeated, useful mechanics. **No new reusable mechanism was proven.**

## Reconciled code and operation trace

The following are inspected source facts. Existing test definitions and A1.1's recorded
results are historical evidence unless explicitly listed as rerun in Verification.

### HTTP: authentication, actor, admission, permission, persistence

1. [NativeAuthentication](../../samples/Wholesale/HttpIdentityDemo/NativeAuthentication.cs)
   configures the default cookie scheme and explicit OIDC login/code+PKCE flow. Validated
   issuer/subject claims are inputs to application identity mapping; a cookie is not a
   cached membership or resource-permission decision.
2. [DemoComposition](../../samples/Wholesale/HttpIdentityDemo/DemoComposition.cs) registers
   [the actor evaluator wrapper](../../src/Gloaming.ActorIdentity/Gloaming.ActorIdentity.AspNetCore/ActorContextPolicyEvaluator.cs).
   Inside `UseAuthorization`, native policy authentication selects the effective principal;
   the wrapper establishes its actor **before native authorization handlers**. The later
   actor middleware completes policy-free requests or verifies the same publication.
   [ApplicationActorResolver](../../samples/Wholesale/HttpIdentityDemo/HttpIntegration/ApplicationActorResolver.cs)
   accepts exactly one authenticated identity and one issuer/subject pair and resolves a
   stable Access user. Ambiguous/unmapped authenticated identities fail rather than becoming
   anonymous. Unauthenticated execution has explicit anonymous attribution.
3. Only after native authorization succeeds or permits anonymous execution does
   [TenantContextMiddleware](../../src/Gloaming.Tenancy/Gloaming.Tenancy.AspNetCore/TenantContextMiddleware.cs)
   run. [OrganizationTenantResolver](../../samples/Wholesale/HttpIdentityDemo/HttpIntegration/OrganizationTenantResolver.cs)
   delegates canonical slug/current Active membership admission to
   [ApplicationAccess](../../samples/Wholesale/modules/Access/Access/ApplicationAccess.cs),
   then publishes the canonical Organization as tenant. Native challenge/forbid short-circuits
   precede tenant resolution. **Actor is available to native handlers; tenant is not.**
4. Public catalog metadata deliberately admits an existing Organization without membership,
   including anonymous or mapped suspended/non-member callers. `AllowAnonymous` does not mean
   tenantless; `AllowTenantless` does not skip native authentication/authorization. Health,
   identity and login exceptions are explicit. Public selection authorizes only the public
   consumer capability, never every private capability in that Organization.
5. Endpoint work receives fixed contexts. The existing
   [membership Contract implementation](../../samples/Wholesale/modules/Access/Access/MembershipAdministration.cs)
   checks current Active membership plus `CanAdministerMemberships` on every point read,
   command and no-op. Commands lock the Organization before authority/target reads, check
   revision and selected Organization, enforce self/last-admin rules, and commit transition
   plus accepted audit together. This is a concrete consumer authorization example.
6. [CustomerProfiles](../../samples/Wholesale/modules/Sales/Sales/CustomerProfiles.cs)
   uses tenant-filtered queries and requires a human for mutations; it does not independently
   recheck membership or a per-resource grant. HTTP admission supplies its current ingress
   gate. [DraftOrderCommands](../../samples/Wholesale/modules/Sales/Sales/DraftOrders/DraftOrderCommands.cs)
   similarly requires a human, selected Organization and caller transaction; the text
   “admitted human” is not an admission lookup. New private-resource behavior needs actual
   module entry checks, including direct calls.
7. [Sales mappings](../../samples/Wholesale/modules/Sales/Sales/SalesDbContext.cs) and
   [ownership mechanism](../../src/Gloaming.Persistence/Gloaming.Persistence.EntityFrameworkCore/TenantOwnershipExtensions.cs)
   add equality filters, owner concurrency tokens and tracked-write validation. Current
   [registration](../../samples/Wholesale/modules/Sales/Sales/SalesDatabase.cs) binds validation
   through native capability integration. These establish EF ownership behavior, not database
   role isolation, authentication, permission or atomic business acceptance by themselves.

The earlier “native authorization → actor mapping” shorthand in A1/design is corrected:
policy authentication → actor establishment → native authorization → actor completion →
tenant admission/publication → endpoint/resource checks. No runtime order changed.
The A1 baseline saying membership revision/commands/audit were absent describes `7ea6652`;
A1.1 now supplies all three. It still supplies no general business permission model.

### Workers and internal calls

| Boundary and actual path | Authority and immutable-operation requirement | RLS implication / remaining gap |
| --- | --- | --- |
| Fresh human job | [A1.1's non-HTTP case](../../samples/Wholesale/HttpIdentityDemo.Tests/MembershipTests.Transactions.cs) resolves the global user and rechecks current membership; after suspension it stops before publishing tenancy. | A copied human/tenant ID is not admission. A real resource job must also recheck its action permission. No generic continuing-human job host exists. |
| Company-owned work | That same case explicitly permits a named system stock reader and retains the human only as initiator. [Inventory handler](../../samples/Wholesale/modules/Inventory/Inventory/Messaging/StockIssueInboxHandler.cs) establishes a receiver-owned system actor and validated message tenant. | System mandate is consumer policy; no automatic human permission inheritance or initiator membership lookup. Header equality/finite allowlists are not sender authentication. Broker ACLs/trusted local callers remain host obligations. |
| Internal synchronous call | [Draft commands → IStockCatalog](../../samples/Wholesale/modules/Sales/Sales/DraftOrders/DraftOrderCommands.cs) uses a public Contract and the same scoped immutable context. | Each capability owns its authorization; private DbContext joins and re-publication are unnecessary. Separate module connections require separate database context setup; one operation is not a distributed transaction. |
| Durable module intake | [Reservation intake](../../samples/Wholesale/modules/Inventory/Inventory/Reservations/StockReservationIntake.cs) validates its Contract/envelope, creates a new scope and commits raw inbox intake. Broker [intake](../../samples/Wholesale/WorkflowDemo/WorkflowBroker.cs) also creates a fresh transaction before ACK. Neither initializes operation tenancy there. | This currently works because raw intake does not require EF filters/context. Effective tenant-only inbox RLS would require an admitted database context before INSERT/duplicate comparison, not merely in the eventual handler. |
| Inbox attempt | [Processor](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/PostgresInboxProcessor.cs) starts its own ReadCommitted transaction and [locks the next delivery](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/PostgresInboxStorage.cs) across tenants **before** invoking the handler. Handler then initializes tenant/system actor; effects, completion and save share that transaction. | Blanket tenant-only RLS would hide the bootstrap row. A future consumer must choose tenant-partitioned polling or a tightly limited transport lane with no arbitrary business-table bypass. Neither is implemented here. After a failure, retry deferral opens another transaction and needs its own role/context treatment. |
| Outbox dispatch | [Dispatcher](../../src/Gloaming.Messaging/Gloaming.Messaging.EntityFrameworkCore.Postgres/PostgresOutboxDispatcher.cs) forbids an existing caller transaction; raw cross-tenant claim commits, transport publishes, then a separate finish/retry transaction updates the lease. | Setting context once in a producer or handler cannot cover dispatch. No transaction may span broker publication merely to retain a local setting. Each storage transaction needs an independently reviewed transport role/context protocol. |
| Maintenance scan | [Deadline worker](../../samples/Wholesale/WorkflowDemo/WorkflowPollingWorkers.cs) creates a fresh tenant scope per finite Organization; [deadline capability](../../samples/Wholesale/modules/Sales/Sales/StockIssues/StockIssueDeadlines.cs) owns its transaction. | This lane establishes tenancy but no actor or general administrative authorization check. Its finite host mandate is not a general privileged worker guarantee. |

The [tenant accessor](../../src/Gloaming.Tenancy/Gloaming.Tenancy/TenantContextAccessor.cs)
rejects missing, repeated and disposed access; actor has the corresponding single-assignment
lifecycle. Finish all required establishment before concurrent business work. Share immutable
values within an operation, not a mutable DbContext across parallel branches. A new scope does
not automatically inherit contexts. A new message/tenant/acting identity needs its own operation.
Retrying a transaction must not reinitialize an already published accessor: retain the same
admitted identity for that operation, or dispose and freshly admit a new operation under an
explicit continuing-human/system policy. Cancellation/failure disposes the failed scope;
never switch its tenant in place. [EF threading limits](https://learn.microsoft.com/en-us/ef/core/dbcontext-configuration/#avoiding-dbcontext-threading-issues).

## Permission, selection and revocation boundaries

Late `IAuthorizationService.AuthorizeAsync` is a native resource-check seam, not a SQL query
translator. Invoke the module's equivalent permission rule from workers/internal entry points
as well. Load only the minimal tenant-scoped metadata needed for a decision before returning
protected payload. Named/default/fallback policies still select native requirements; a named
policy does not inherit the fallback. Policy-specific schemes can replace/merge the principal
before actor mapping. Anonymous metadata skips enforcement but still allows authentication;
the sample can reject an unmappable authenticated caller on a public endpoint.
[ASP.NET 10 resource authorization](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/resource-based?view=aspnetcore-10.0),
[10.0.12 policy combination](https://github.com/dotnet/aspnetcore/blob/v10.0.12/src/Security/Authorization/Core/src/AuthorizationPolicy.cs),
[middleware](https://github.com/dotnet/aspnetcore/blob/v10.0.12/src/Security/Authorization/Policy/src/AuthorizationMiddleware.cs),
[scheme selection/challenge/forbid](https://github.com/dotnet/aspnetcore/blob/v10.0.12/src/Security/Authorization/Policy/src/PolicyEvaluator.cs).

A tenant-aware early policy integration is an alternative only if a concrete consumer needs
it. It would have to admit the effective policy principal before publishing tenancy exactly
once, handle no-policy/anonymous/tenantless paths and preserve native challenge/forbid and
failure behavior. Moving middleware before `UseAuthorization` alone could bind context to
the default cookie rather than a policy-selected identity. It is not recommended or implemented
by A2; there is no reason to make Tenancy depend on Access or FGA.

| Surface | Required consumer permission boundary, in addition to tenant isolation |
| --- | --- |
| Detail/create/update/delete | Action-specific check for this Organization/resource or authorized creation parent. Organization administration alone gives no Personnel/private-resource access. A public catalog grant must not flow into private mutation. |
| List/count/page/joins | Intersect tenant, business and resource-permission predicates before count, totals, ordering and paging, including protected children/fields. [C1 queries](../../samples/Wholesale/modules/Sales/Sales/DraftOrders/DraftOrderQueries.cs) currently have business/tenant predicates only. Counting and paging are separate statements; a stable common snapshot is caller-owned. |
| Export | Permission-aware selection before bytes are staged; choose snapshot-at-start or fresh chunk/job admission, partial-output cleanup and download authorization. A new operation admits again. Already disclosed data cannot be recalled. |
| AI/search/cache | Restrict source rows/chunks before ranking/top-k, snippets, tool output and cached answer disclosure. Tenant-only vector filtering cannot enforce private-resource permissions; stale ACL indexes need explicit completeness/freshness contracts. These endpoints are absent today. |
| Internal/system entry | Trusted identity/tenant source plus explicit capability mandate. Actor presence, message correlation and initiator attribution grant no permission. |

A1.1/ADR 0003 deliberately preserve the immutable admitted operation when membership changes;
the next protected human admission denies the same cookie. Membership administration additionally
rechecks its current authority inside its own protocol. Business-resource grant freshness remains
an explicit future policy: ReadCommitted reads statement snapshots; RepeatableRead can align
count/page but keeps older permission facts. Neither guarantees resource permission at commit,
cross-store atomic revocation or a frozen sequence of HTTP pages. Do not add a membership join
to every RLS policy and silently change admission-time semantics. See
[A1 selection/consistency analysis](access-authorization-assessment.md#complete-selection-and-concurrency)
and [PostgreSQL 18 isolation](https://www.postgresql.org/docs/18/transaction-iso.html).

## Actual database authority and conditional RLS protection

Pinned repository inputs are ASP.NET/EF 10.0.12, Npgsql EF provider/driver 10.0.3 and
PostgreSQL image 18.6. The [source note](tenant-authorization-primary-sources.md) distinguishes
versioned source from living documentation and the inferences below.

| Current composition | Evidence and consequence |
| --- | --- |
| Fresh local AppHost | [Program](../../samples/Wholesale/AppHost/Program.cs) omits a PostgreSQL username; Aspire 13.6.1 defaults to `postgres`. The official image initializes that user as superuser on an empty data directory. **Source/configuration inference:** this default credential bypasses RLS even with FORCE. A retained data volume can have different roles; no live catalog was queried. |
| HTTP/setup/module credentials | API and explicit setup share the same database reference. [DemoComposition](../../samples/Wholesale/HttpIdentityDemo/DemoComposition.cs) passes the Access connection to Access, Sales and Inventory. [DemoSetup](../../samples/Wholesale/HttpIdentityDemo/DemoSetup.cs) migrates and seeds through the supplied credential. No least-privilege runtime/migration split is provisioned. With a custom non-superuser credential, migration-created table ownership is another bypass unless FORCE applies. |
| PostgreSQL proofs | [Fixture](../../tests/Support/PostgreSqlFixture.cs) uses Testcontainers' default user to CREATE DATABASE and sets `Pooling=false` in returned proof connections. Existing successful tests are not evidence of a restricted runtime role, RLS enforcement or physical pool reuse. A specialized event-view test's role does not configure the HTTP runtime. |
| Context/lifecycle | [Sales](../../samples/Wholesale/modules/Sales/Sales/SalesDatabase.cs)/[Inventory](../../samples/Wholesale/modules/Inventory/Inventory/InventoryDatabase.cs) use ordinary scoped AddDbContext/UseNpgsql, without tenant GUC setup, DbContext pooling or EnableRetryOnFailure. Native reads may open/close connections lazily outside explicit transactions. Runtime connection options remain externally configurable; defaults are not a deployed catalog attestation. |
| RLS schema | Active migrations provide no ENABLE/FORCE ROW LEVEL SECURITY or policies. RLS is currently absent, including on tenant-mapped messaging/audit tables. PostgreSQL “default deny” begins only once RLS is enabled and the executing role is subject to it. |

Before any RLS support claim, inspect the **actual runtime connection**, without exposing
credentials: `session_user`/`current_user`, `pg_roles.rolsuper/rolbypassrls`, role memberships
and ability to `SET ROLE`, table owners, `pg_class.relrowsecurity/relforcerowsecurity`,
`pg_policies`, schema/table/function grants and view/function execution context. Verify runtime
cannot become owner/admin, alter/disable policies or use a privileged helper. Migrations,
restores and maintenance use separately authorized credentials. FORCE subjects a table owner
to policies during ordinary access; it neither constrains superusers/BYPASSRLS nor removes
the owner's ability to change table policy. Source/configuration review cannot certify an
uninspected deployment. [Role and policy evidence](tenant-authorization-primary-sources.md#role-and-policy-facts).

The following matrix is conditional on enabled, correctly inventoried policies; a subject
runtime role; correct admitted transaction context; and no privileged definer/view escape.
It describes a possible integration, **not current runtime guarantees**.

| Path / threat | Current application boundary | Additional tenant-only RLS protection and precise limitation |
| --- | --- | --- |
| Ordinary EF SELECT | Named equality filter on registered entities. | USING limits rows seen by SQL. Same-tenant private rows remain visible unless the application adds resource permission predicates. |
| Tracked INSERT/UPDATE/DELETE | Guard validates current/original tenant and forbids owner change; owner concurrency protects stored targeting. | USING controls existing UPDATE/DELETE targets; WITH CHECK validates inserted/new updated rows. Hidden targets normally yield zero rows; failed new-row checks raise an error and abort the statement. Keep version/audit/aggregate checks. |
| IgnoreQueryFilters / direct SQL / another connection | Disabled filters/direct commands bypass EF query guard; separate connections inherit no operation context automatically. | Effective RLS still applies to covered tables under that connection's role/context. A missing predicate is contained; a privileged credential or forged tenant setting is not. Each connection/transaction must establish its own context. |
| ExecuteUpdate/Delete / third-party bulk tools | Translated LINQ filters can still apply, but no tracked SaveChanges validation, aggregate reaction, audit or automatic concurrency protocol. | UPDATE/DELETE policies still apply if SQL runs as the subject role. Tool-created connections/roles, upserts and import paths need independent proof. RLS does not supply bypassed business obligations. |
| COPY/import/export | Native path lies outside tracked guards. | COPY TO observes applicable row policies; COPY FROM is unsupported with effective RLS. Use permitted INSERT paths for the proof; privileged import is an explicit exception, not an isolation proof. Files/backups/external search stores need their own controls. |
| Already tracked/local/cached object | Immutable scopes prevent intentional rebinding; bypass-loaded objects still exist in memory. | No database statement means no RLS check. Find can return a tracked object; navigation fixup/cache disclosure is outside RLS. Dispose operation state and authorize output. |
| Missing/tenantless context | Required accessor rejects missing/tenantless on the supported business paths; raw transport bypasses it. | A fail-closed policy can hide rows/reject writes, but empty results alone cannot detect broken setup. Application must reject missing required context before executing protected SQL. Tenantless never means all tenants. |
| Same-tenant FK / ownership transfer | Composite owner+ID foreign keys and explicit write guards where configured. | Equality policies on two rows do not establish their relationship or forbid every ownership-transfer protocol. Keep composite FKs/unique constraints and owner immutability. RI/unique checks can bypass row security and disclose existence through errors. |
| Views/functions / table-wide work | No general restricted-role boundary is established. | Default owner-context views and SECURITY DEFINER functions can escape caller policy; review invoker views/privileges and function search_path. TRUNCATE and REFERENCES are outside RLS; deny unnecessary privileges. Do not advertise elimination of SQL injection, timing/covert channels or constraint leaks. |
| Cross-tenant dispatch/global registry | Explicit global lookup or raw cross-tenant transport SQL. | Blanket tenant equality breaks bootstrap/discovery. Narrow authorized global/transport lanes are separate exceptions with restricted table rights; neither tenantless nor global BYPASSRLS should authorize business access. |
| Runtime credential/SQL attacker | Currently potentially superuser/owner; no policies. | A runtime able to set `app.tenant_key` can impersonate another tenant value. A mutable custom setting authenticates nothing. RLS chiefly limits accidental SQL scope errors in trusted application code; stronger credential-holder protection needs a separately designed trusted context authority. |
| Resource revocation / accepted audit | Admission timing and A1.1 native locks/revision/audit are consumer-owned. | Tenant-only RLS provides no within-tenant grant decision, immediate revocation fence, event delivery or transactional business audit. |

EF source evidence: [filters/IgnoreQueryFilters](https://learn.microsoft.com/en-us/ef/core/querying/filters),
[bulk update/delete limitations](https://learn.microsoft.com/en-us/ef/core/saving/execute-insert-update-delete),
[10.0.12 tracked Find path](https://github.com/dotnet/efcore/blob/v10.0.12/src/EFCore/Internal/EntityFinder.cs).
Database semantics and role/SQL escape details are cited separately in the
[RLS source matrix](tenant-authorization-primary-sources.md).

## Proposed policy and transaction discipline

This is a bounded consumer design to prove, not supplied SQL migrations or a new abstraction.
Choose one positive tenant equality policy for the proof role with explicit USING and
WITH CHECK. PostgreSQL combines permissive policies with OR, restrictive policies with AND;
a restrictive tenant rule needs an applicable permissive rule too. An additional permissive
allow-all policy can defeat a sole permissive tenant rule. Review every table/command/role
combination, including new tables and partitions/direct grants; enabled-with-no-applicable-policy
denies, while disabled/omitted RLS provides no such boundary. Base-table privileges are still
required. UPDATE/upsert may also require SELECT policies; test ON CONFLICT behavior rather
than inferring it from ordinary INSERT. [Policy semantics](https://www.postgresql.org/docs/18/sql-createpolicy.html).

For current opaque text tenant keys, the illustrative predicate input is
`NULLIF(current_setting('app.tenant_key', true), '')`, compared to a required non-null owner
column with compatible exact-key semantics. Missing/empty becomes NULL and cannot satisfy
equality. Reject invalid application keys before setup; do not trim/change case, invent a
wildcard, or cast every key to UUID. A nonempty unknown key ordinarily matches no rows, not
an authenticated tenant. If a consumer uses typed keys, conversion errors must fail without
fallback. This expression is not installed or tested here.

| Lifecycle boundary | Smallest proposed discipline / failure semantics |
| --- | --- |
| Entry | Resolve trusted actor, admit canonical tenant and authorize the capability under the selected freshness policy. Global identity/admission queries must remain possible before tenant establishment; do not fabricate a tenant to open Access. |
| Connection + transaction | On the owning DbContext's native connection, begin an explicit transaction, then parameterize `set_config('app.tenant_key', admittedKey, true)` before the first protected query/write. Validate setup success; no swallowed errors. A command on a separately opened pooled connection configures the wrong session. |
| Read paths | Otherwise nontransactional reads need an explicit short transaction for transaction-local context. SET LOCAL in an autocommit setup statement expires before the next statement. One ReadCommitted transaction binds context but still has statement snapshots; count/page consistency needs its own isolation choice. |
| Success / rollback | Commit/disposal ends the local setting and restores the prior session value. Never use session-wide tenant settings as a baseline. SET LOCAL is not proof that the restored session baseline was safe; prohibit session defaults that carry authority and test the missing-context lease. |
| Savepoints | Set context before business savepoints. Rollback to a savepoint preceding setup undoes setup; reestablish/verify before any further protected command. EF savepoint rollback does not automatically rebuild an application context protocol. |
| Cancellation / fault | Stop work, roll back/dispose the transaction/connection and discard tracked proposals. No use of a canceled/broken transaction to continue business queries; rollback/disposal failures must not return a reusable live operation. Cancellation during commit can leave outcome unknown. |
| Physical connection pool | Npgsql pooling defaults on; reset on return/next use is useful hygiene, not the authority protocol. No Reset On Close=true is incompatible with the proposed proof baseline. Test actual backend reuse, not only new connection objects; do not infer pooling coverage from the fixture with Pooling=false. |
| DbContext pool | Independent of physical pooling; the sample uses scoped unpooled contexts. A pooled context cannot retain a previous scope's accessor/tenant/state. Lease-time binding/reset needs its own proof if later selected; do not introduce pooling to earn this assessment. |
| Retry / reconnect | A retry recreates native connection/transaction setup inside the retried delegate, then repeats the checks required by that operation's explicit permission-freshness policy. Do not silently replace admission-time authority with continuous readmission; a separately created human operation admits again. Do not reuse tracked failed state or rebind operation identities. Current sample enables no provider automatic retry; a future execution strategy must retry the complete transaction unit with context setup included. |
| Lost commit response | Unknown success is distinct from rollback or denial. Reconcile by an authorized durable operation identity/receipt when required; A1.1 currently permits fresh state read but has no receipt/automatic retry. RLS adds no idempotency. |
| Multiple modules/connections | Establish the same admitted immutable tenant independently for each native transaction. Preserve module save/commit ownership and existing independent durable delivery; no hidden shared transaction or request-wide auto-commit. |

[SET LOCAL/savepoint semantics](https://www.postgresql.org/docs/18/sql-set.html),
[set_config/current_setting](https://www.postgresql.org/docs/18/functions-admin.html#FUNCTIONS-ADMIN-SET),
[EF connection/context pooling](https://learn.microsoft.com/en-us/ef/core/performance/advanced-performance-topics),
[retry transaction units](https://learn.microsoft.com/en-us/ef/core/miscellaneous/connection-resiliency),
and [pinned Npgsql behavior](tenant-authorization-primary-sources.md#transaction-local-context-and-connection-lifetime)
support these obligations. Operational costs include explicit read transactions, extra setup
commands, role/grant/migration inventory, policy/index plans, long-read snapshot/vacuum cost,
pool/retry diagnostics and independently secured transport/admin lanes. Measure the selected
query under the actual role; a policy existing in a migration is insufficient deployment proof.

Missing initialization is a server/configuration error; deliberate tenantless is valid only
for explicitly authorized global capabilities. RLS hidden rows/zero-row writes, failed
WITH CHECK, concurrency conflict and dependency outage are different outcomes. Preserve
native challenge/forbid, sample admission concealment (404), actor mapping failure (403) and
missing required tenant (400). Current unmapped database faults use native 500 fallback;
known temporary-dependency 503 handling would be future consumer work. Never turn setup,
database or permission-service failure into allowed access or a supposedly complete empty list.

## Smallest useful follow-up proofs

These are proposals, not new tests, approved implementation or a mandatory sequence.

| Bounded proof | Independent expected evidence / decision unlocked |
| --- | --- |
| One module's private resource selection | Two same-tenant users with different grants plus a foreign tenant; detail/write and C1-style list/count/page use the same action semantics before shaping/paging. Include sparse/overlapping grants and protected child totals. Test direct/internal and freshly admitted human/system entry, native selected-scheme/anonymous/tenantless behavior, suspension between operations and immutable admitted work. Add exports/AI only when a real endpoint is selected. This earns a consumer permission pattern without requiring RLS/FGA. |
| One PostgreSQL parent/child RLS slice, only if selected | Separate owner/runtime roles; catalog assertions and attempted role/policy escalation. Alpha/beta SELECT/INSERT/UPDATE/DELETE with filters enabled/disabled, direct SQL, ExecuteUpdate/Delete and tenant-changing writes; composite FK/constraint failures; explicit USING/WITH CHECK and enabled-no-policy/disabled/owner/FORCE/BYPASS controls. Show same-tenant private rows still need permission predicates. This earns only the named tables/roles/paths. |
| Lifecycle extension of that same slice | Pool max size 1 plus backend identity to prove alpha→beta→missing reuse; success, rollback, cancellation, savepoint-before-setup rollback, reconnect and injected transient retry. Setup must precede the first protected command on every attempt; test malformed/empty/tenantless and setup failure. Add concurrent separate scopes/connections. Keep DbContext pooling out unless actually required. This earns the connection/transaction protocol, not credential-holder isolation. |
| Transport integration only if RLS must cover messaging | Demonstrate that current inbox selection occurs before handler admission and that outbox claim/finish/deferral have separate transactions. Select a consumer partitioned or narrow transport-role design; prove intake duplicates, failed attempts, completion/deferral, lease takeover and cross-tenant dispatch without business-table privilege. No global tenantless bypass and no transaction spanning publication. Until this proof exists, exclude messaging tables explicitly from RLS claims. |

The first useful product follow-up remains a consumer resource-permission query, because
A1.1 already supplies membership lifecycle. RLS proof is optional and independent. Outstanding
choices are the concrete private resource/action model, permission decision freshness for writes
and exports, whether database enforcement is required, the deployed runtime role split and
whether transport data belongs inside that future RLS boundary. No generic execution pipeline,
FGA tuple store, provider dialect or Tenancy→Access dependency follows from these choices.

## Verification and extraction findings

Source review reconciled A1.1, actor policy ordering, HTTP/non-HTTP ingress, EF ownership,
global registry, raw transport SQL, local credentials and connection configuration against
`d35b3ee`. Official versioned ASP.NET, PostgreSQL and Npgsql sources support provider facts;
proposed application protocols and source/configuration inferences are marked explicitly.
Fresh verification on 2026-10-16 used SDK 10.0.401 and the pinned 10.0.12 packages:

| Check | Result and scope |
| --- | --- |
| ActorIdentityAspNetCoreTests | 15 passed, zero failed/skipped; includes actor availability inside native handlers and policy-selected identity. |
| TenancyAspNetCoreTests | 39 passed, zero failed/skipped; includes native challenge/forbid before resolution, named/anonymous policies and tenant requirements. |
| Documentation checks | 407 local links/anchors across nine changed Markdown files resolved; changed-file whitespace checks passed. Topic-branch index matched its pre-edit snapshot; the staged diff remained empty. No runtime source changed. |

Both suites were built/executed from this checkout. Sandbox MSBuild initially failed before
tests; a trial runner invocation with extra flags discovered zero tests and is not counted.
Host execution with the repository's documented test arguments then passed. These reruns
confirm existing HTTP adapter behavior; no new permission or RLS test was added. A1.1's
138 HTTP/PostgreSQL and 90 architecture passes remain its historical results, not new RLS
or A2 permission proofs.

**Library:** no implementation/interface/dependency changed; no new reusable mechanism was
proven. Existing context/ownership mechanisms retain their documented limits. A possible
future PostgreSQL transaction setup helper still needs independent adoption and interface review.
**Template:** no generated files/presets changed; future least-privilege credentials and explicit
setup are consumer composition candidates, pending proofs. **Sample:** current membership
administration is real authorization; general private-resource grants/authorized selection,
RLS roles/context/policies and transport integration remain unimplemented. Public catalog and
company mandates remain deliberately consumer-owned exceptions.

Review-worthy files are this report, the [primary-source note](tenant-authorization-primary-sources.md),
[A1 reconciliation](access-authorization-assessment.md), [design correction](../design.md),
and local [Tenancy](../../src/Gloaming.Tenancy/docs/capabilities.md) /
[Persistence](../../src/Gloaming.Persistence/docs/capabilities.md) capability records.
The A2 brief, roadmap and extraction plan synchronize assessment/merged-implementation status.
Review the HTTP actor-order correction, raw worker bootstrap exceptions, conditional
protection matrix and proposed transaction lifecycle line by line. No live role catalog,
RLS policy, pooling/retry fault proof, production query benchmark or new business-resource
permission test was executed for this assessment. Changes remain unstaged; no commit or push
is part of this handoff.
