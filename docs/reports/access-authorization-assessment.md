# A1 + A1.1: Access and resource authorization assessment

Assessment: 2026-10-16. Source baseline: fetched `origin/main`, `7ea6652`
(`chore: cqrs research`), isolated `docs/access-authorization-assessment` worktree.
Recommendation for owner review; no engine adoption, runtime change, migration, public
library interface or ADR is selected. [A1 scope](../plans/a1-access-resource-authorization-research.md),
[original intake](access-authorization-research-intake.md),
[primary-source/version evidence](access-authorization-primary-sources.md).

Current-code reconciliation, 2026-10-16: A1.1 is merged at `d35b3ee`; its
[implementation report](a1-1-membership-suspension.md) supersedes the baseline absence of
membership commands/revision/audit and the proposed A1.1 protocol below. Those sections
retain their dated assessment meaning. [A2's assessment](tenant-authorization-assessment.md)
reconciles current code and optional RLS; its source/configuration findings are not new
database enforcement proofs.

## Recommendation

Start Umbrine with native ASP.NET imperative resource authorization and focused,
module-owned relational permission predicates. Keep Access authoritative for global
identity and Organization membership; business modules own their permissions, resources,
business responsibilities and resource grants. Use positive grants within explicit scopes.
Organization administration supplies no private business-data permission. Retain current
active-membership admission and immutable admitted-operation context.

Keep OpenFGA as a conditional alternative when graph traversal/delegated sharing removes
enough consumer work to justify operating another authority. If selected, let it own direct
authorization tuples rather than continuously copying every business relationship into it.
Use current module facts and bounded contextual tuples for detail checks first. This does
not solve complete large-set queries: the pinned API can return partial `ListObjects`
results without a completeness marker. No production engine is selected by this comparison.

The smallest follow-up is A1.1's consumer-owned suspend/reactivate journey with native
concurrency and atomic accepted-change audit. It can proceed without OpenFGA, RLS, a role
editor or a general authorization framework. A separate narrow query proof should extend
C1's pattern with real resource permissions. **No new reusable mechanism was proven.**

## Reconciled code baseline

These are source observations, including existing test definitions, not fresh executions.
Historical test results retain their original dates and do not prove A1/A1.1.

| Actual evidence | Consequence for this assessment |
| --- | --- |
| [AccessRows](../../samples/Wholesale/modules/Access/Access/Persistence/AccessRows.cs), [mapping](../../samples/Wholesale/modules/Access/Access/Persistence/AccessDbContext.cs) | Stable users, issuer/subject links, Organizations and Active=1/Suspended=2/Removed=3 memberships exist. One Active/Suspended row per Organization/user is enforced; removed history may coexist. No membership revision/concurrency token, permissions, roles or Personnel model exists. Organization availability/licensing is not modeled here. |
| [ApplicationAccess](../../samples/Wholesale/modules/Access/Access/ApplicationAccess.cs), [Contracts](../../samples/Wholesale/modules/Access/Access.Contracts/IApplicationAccess.cs), [registration](../../samples/Wholesale/modules/Access/Access/AccessRegistration.cs) | Read-only lookup/admission; global identity resolution is independent of selected Organization. Admission reads persisted Active membership. No suspend/reactivate command or Access audit integration exists. |
| [HTTP composition](../../samples/Wholesale/HttpIdentityDemo/DemoComposition.cs), [actor evaluator](../../src/Gloaming.ActorIdentity/Gloaming.ActorIdentity.AspNetCore/ActorContextPolicyEvaluator.cs), [resolver](../../samples/Wholesale/HttpIdentityDemo/HttpIntegration/OrganizationTenantResolver.cs), [ADR 0003](../adr/0003-access-registry-and-operation-admission.md) | Policy authentication → actor establishment → native authorization → actor completion → Organization admission/tenant publication → endpoint. Corrected by A2: native handlers can consume actor context, but not established tenancy. Public Organization catalog access is an intentional exception, including mapped non-members; suspension does not close public routes. |
| [AdmissionTests](../../samples/Wholesale/HttpIdentityDemo.Tests/AdmissionTests.cs), [seed](../../samples/Wholesale/modules/Access/Access/AccessDemoSeed.cs) | Source cases cover suspended/removed denial and next admission with the same cookie while preserving global identity. The in-flight case explicitly uses Removed, not Suspended. Seeded alpha has two active Organizations. Tests mutate SQL directly; they do not prove authorized administration, audited races, reactivation or the combined business read/write/other-Organization journey. |
| [Sales queries](../../samples/Wholesale/modules/Sales/Sales/DraftOrders/DraftOrderQueries.cs), [filters](../../samples/Wholesale/modules/Sales/Sales/DraftOrders/DraftOrderFilters.cs), [transaction cases](../../samples/Wholesale/CqrsDemo.Tests/QueryTests.Transactions.cs) | C1 composes Sales-owned joins/child totals, filters before count and stable total/ID paging. Count/page are separate statements; repeatable read is caller-owned. There is no resource-permission predicate. |
| [C1 composition](../../samples/Wholesale/CqrsDemo/CqrsComposition.cs), [draft commands](../../samples/Wholesale/modules/Sales/Sales/DraftOrders/DraftOrderCommands.cs) | Console setup supplies finite identities directly. Commands check human actor, selected tenant and transaction; those checks do not look up current membership or resource grants. Trusted ingress remains the caller's obligation, despite the phrase “admitted human” in an exception. |
| [Inventory queries](../../samples/Wholesale/modules/Inventory/Inventory/StockPositions/StockPositionQueries.cs) | JSONB availability/header queries stay inside Inventory and cross module boundaries through Contracts. They have no per-resource permissions or paging/count/export guarantee. |
| [Tenancy accessor](../../src/Gloaming.Tenancy/Gloaming.Tenancy/TenantContextAccessor.cs), [ownership utility](../../src/Gloaming.Persistence/Gloaming.Persistence.EntityFrameworkCore/TenantOwnershipExtensions.cs) | Single publication/immutable context and EF tenant equality/tracked-write guards exist. These are neither resource authorization nor RLS. Raw/bulk SQL, disabled filters and already tracked entries remain separate concerns. |

C1 is present in fetched main at `7ea6652`; its plan/report's “uncommitted” and staged
snapshot statements describe its earlier handoff. They are not current branch status.
The initial intake used `824c95b`, and A1.1 used `df83518`/`f55d803`; the table above replaces
those as the current baseline. Earlier HTTP design paragraphs proposing unimplemented
adapters are historical: E3 implemented the ordering shown above. Current EventReactions
and native save/commit ownership supersede earlier Domain/pipeline proposals. C1's owner
decision permits simple useful extraction but selects no general execution pipeline.

## Authority and scope

The following is proposed consumer ownership, not a new module or library contract.
Namespaced permission keys below are examples, not a central product-wide enum.

| Fact | Recommended native authority and updater/read path | OpenFGA alternative and consistency cost |
| --- | --- | --- |
| Application user and external identity | Access registry; trusted account-linking operation; exact validated issuer/subject lookup. Never infer a link from email. | Still Access; FGA user IDs reference canonical application identity, distinguished from system IDs. |
| Organization membership/status | Access; scoped authorized lifecycle commands; current persisted admission before each fresh protected human operation. | Keep Access authority. Do not maintain membership tuples as a second authority; supply the admitted Organization member context for a check if needed. |
| Permission definition and business capability | Owning module code, e.g. `projects.read`, `projects.manage`, `personnel.private.read`; module approves mappings to responsibilities/roles. | Module-owned model files compose into one deployed model; they are not runtime isolation. Trusted tuple writers enforce module/Organization namespace boundaries. |
| Direct resource grants, local role definitions/assignments and local groups | Owning module's relational tables and authorized admin operations; same query owner can join permission and business facts without replication. | FGA may be sole authority for direct grant/role-assignee tuples. Application stores labels/history separately, with an explicit grant-change audit/delivery protocol; no atomic application/FGA commit is assumed. |
| Organization-wide administration and cross-module configurable bundles | Access owns administrative grants and the Organization's bundle metadata/assignments; each target module validates which of its permission keys can be delegated. Consume a bounded complete fact response initially. | FGA can own authorization-role assignment/permission edges instead, but do not also treat Access copies as authoritative. Metadata remains application-owned. |
| Personnel and optional user link | Personnel owner retains records without login. A trusted link to an Access user enables only current explicitly mapped access, with active admission. No retroactive permission or history rewrite. | Direct module query/contextual fact for detail; not continuous copying of all Personnel history. |
| Project lead/developer and effective dates | Projects owns business responsibility/history, including reassignment/backdating/expiry. Derive current access from effective facts and the current trusted user link. | For detail, bounded module facts can become request-local tuples. Persisting their derived edges requires explicit removal/expiry/reconciliation/freshness policy. |
| License, Organization/capability availability, valid business transition | Owning consumer services/modules; independent gates after admission and permission. | An allowed FGA decision cannot replace these gates. |

Invitations, pre-registration and dormant users remain Access workflows, absent from the
current code. A pending invitation or Personnel record is not an authenticated principal.
Activation needs trusted acceptor/account-link verification and current membership/grants;
it must not silently merge matching email addresses or rewrite historical Personnel records.

For a cross-module bundle, a complete bounded Access response can be an operation-local
set of permission keys/scopes/subjects. It is a proposed consumer Contract, not an exported
`IQueryable`, private DbContext join or Gloaming interface. Reject oversize/incomplete facts
instead of truncating them. An Organization-wide scalar permission can become a SQL
parameter; hundreds of resource assignments cannot be assumed to fit this strategy.

For large cross-module facts (including Personnel links), explicitly compare an owner-published
read-only relational fact view with a receiver-owned projection. Neither exists today. A
published view has a reviewed schema/access/lifecycle contract; it does not license reading
private tables. A projection adds duplication, transactional delivery, idempotent revisions,
deletions/unlinks, rebuild/reconciliation and a fail-unavailable freshness boundary. It also
needs an authority-recheck or gating protocol if revocations must take effect before lag
is repaired. Do not introduce it by default to make an authorization join convenient.

### Roles and business derivation

- **Fixed roles:** versioned module-defined bundles and scoped assignments are the cheapest
  starting point. A role on project A contributes no Organization-wide permission and none
  on B. User/group/direct/business-derived positive grants union only within matching scope.
- **Editable defaults:** seed an Organization-owned copy with a baseline version. Later product
  upgrades offer reviewed changes; they do not silently rewrite customized sensitive access.
  Shared live baselines are a separate opt-in policy with tenant-wide impact.
- **Configurable roles:** persist definition revision and assignment scope separately. Restrict
  allowed permission keys/resource kinds and who can edit/assign each bundle. Deletion, newly
  introduced keys and unsupported versions must not silently widen access. Large role edits
  require an atomic definition change or an explicit activation revision.
- **Business roles:** “lead on A, developer on B” is effective domain responsibility, not an
  authorization-role history record. Resolve link, effective interval and responsibility at
  decision time; use a trusted time. Backdating changes current access only when the current
  interval is affected. Reassignment/expiry ends the derived contribution, preserving history;
  independent explicit grants can still allow access. Role-policy edits change current rules,
  not past contributions. Historical reporting does not itself grant current read access.

Recommend positive union initially. If explicit deny is needed, specify its precedence over
groups, inherited scopes and business-derived grants before using it. Separate permission to
administer members from permission to view Personnel, and separate permission to assign a
sensitive role from permission to read its data. If self-escalation is forbidden, constrain
assignment authority/approval as well as withholding the initial read grant. System actors
use explicit consumer grants/mandates; they never inherit human membership automatically.

## Representative native model and query

Analysis-only model: a Projects module owns Project, ProjectRoleDefinition/revision,
RolePermission, RoleAssignment(scope + user/group subject), GroupMembership and current
business responsibilities/history. A resource key always includes Organization and kind.
For this first query example, business responsibilities identify a stable application UserId.
Personnel-only assignments remain valid business records without login access. If Umbrine
instead derives those subjects through a separately owned Personnel link, the complete
published-fact/Contract/projection choice above is an additional proof obligation; the SQL
must not pretend the private link table is already local.

Here `current_responsibility` is an illustrative query over Projects-owned effective rows,
not a proposed persisted projection. `:org_read` is an admitted, complete Access bundle
decision for this Organization/action, not a client-supplied flag. All tables are hypothetical.

```sql
WITH visible AS (
  SELECT p.organization_id, p.id, p.name, p.updated_at
  FROM projects.project p
  WHERE p.organization_id = :admitted_org
    AND p.state = :requested_state
    AND (
      :org_read
      OR EXISTS (
        SELECT 1
        FROM projects.role_assignment a
        JOIN projects.role_permission rp
          ON (rp.organization_id, rp.role_id) = (a.organization_id, a.role_id)
        WHERE a.organization_id = p.organization_id
          AND a.scope_kind = 'project' AND a.scope_id = p.id
          AND rp.permission_key = 'projects.read'
          AND (a.user_id = :actor_user OR EXISTS (
            SELECT 1 FROM projects.group_membership gm
            WHERE gm.organization_id = a.organization_id
              AND gm.group_id = a.group_id AND gm.user_id = :actor_user
          ))
      )
      OR EXISTS (
        SELECT 1 FROM projects.current_responsibility r
        WHERE r.organization_id = p.organization_id AND r.project_id = p.id
          AND r.user_id = :actor_user AND r.kind IN ('lead', 'developer')
          AND r.effective_from <= :decision_time
          AND (r.effective_to IS NULL OR :decision_time < r.effective_to)
      )
    )
)
SELECT * FROM visible
ORDER BY updated_at DESC, id ASC
LIMIT :limit OFFSET :offset;
-- Count uses the same visible definition without LIMIT/OFFSET.
-- Detail adds id=:id; write uses its action's corresponding permission predicate.
```

`EXISTS` prevents overlapping grants/groups/children from duplicating resources or inflating
counts. A Sales extension would place its own permission predicate into C1's `filtered`
query before `CountAsync`, `OrderBy`, `Skip` and `Take`; do not bolt checks onto the returned
page. Use the same action semantics in detail/write handlers and list predicates, with
separately defined export/private-field actions when required. A create action targets an
authorized parent/collection, since its new resource does not exist yet. Loading minimal
tenant-scoped metadata for a handler must not disclose the protected payload first.

ASP.NET supports `AuthorizeAsync(User, resource, requirements)` after admission; that is an
enforcement seam, not an automatic SQL translator. Invoke equivalent consumer checks from
module/worker entry paths too. Preserve native challenge/scheme behavior and reauthorize
direct calls; a trusted internal call sharing an admitted operation need not republish tenancy.
[Native resource authorization](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/resource-based?view=aspnetcore-10.0).

### Complete selection and concurrency

| Surface | Required behavior and practical limit |
| --- | --- |
| Lists, joins and counts | Intersect admitted Organization, resource permission and business predicates before shaping protected fields, grouping/counting and paging. Derived totals must not disclose separately protected child data. Identical permission predicate means identical semantics; multiple statements still need an explicitly selected common snapshot. |
| Paging | Use fully unique sorting, with full ownership key in joins. C1's offset paging is a useful baseline; seek paging can reduce deep offset cost. Neither freezes data/permissions across requests. Bind cursor to actor/Organization/action/filter/sort and define expiry/restart policy; choose a retained snapshot only for a real requirement. |
| Large/sparse visibility | Measure realistic SQL plans and indexes for Organization/subject/scope/permission and sort keys; page cap alone does not bound scanning/count cost. Do not materialize all accessible IDs as a default relational strategy. |
| Exports | Apply the same permission-aware selection before data enters a staged export; define snapshot-at-start versus fresh checks per chunk, failure/partial-file handling and permission at download. A fresh admission is required for each separately admitted chunk/job. Already disclosed bytes cannot be revoked retroactively. |
| AI retrieval/tools | Filter source rows/chunks before ranking/top-k/context/tool output, including labels/snippets and cached answers. Post-filtering an unqualified top-k loses completeness/recall and may already disclose text. A vector index must support the authorization predicate or use an authorized candidate plan; stale ACL copies need the same freshness contract. Distinguish bounded retrieval from a complete export/count. Recheck download/tool execution under its actual actor; an initiator ID is attribution, not authority. |

PostgreSQL read committed takes statement snapshots; repeatable read can keep a query
transaction's count/page aligned but deliberately retains earlier permission facts. It
does not mean “latest at commit,” synchronize remote checks or fix later page requests.
Use a captured decision time for expiring grants when the chosen policy is operation-snapshot
authorization. [PostgreSQL 18 isolation](https://www.postgresql.org/docs/18/transaction-iso.html),
[EF pagination](https://learn.microsoft.com/en-us/ef/core/querying/pagination).

## Equivalent OpenFGA direction and limits

The [source note](access-authorization-primary-sources.md) pins engine/API/SDK versions and
separates official facts from application inferences. This small DSL sketch shows the same
positive project scope. It has not been parsed, deployed or tested.

```fga
model
  schema 1.1
type user
type group
  relations
    define member: [user]
type organization
  relations
    define member: [user]
    define project_reader: [user, group#member, role#assignee]
type role
  relations
    define assignee: [user, group#member]
type project
  relations
    define organization: [organization]
    define lead: [user]
    define developer: [user]
    define reader: [user, group#member, role#assignee]
    define can_read: (lead or developer or reader or project_reader from organization) and member from organization
    define can_manage: lead and member from organization
```

Example: canonical IDs `organization:north`, `project:north.A`, `role:north.A.reviewers`.
FGA-owned direct tuples can assign `user:alice` to that role and its assignees to A's reader.
A trusted request adds `organization:north#member@user:alice` from fresh admission and
`project:north.A#lead@user:alice` from current Projects facts; for B it supplies developer
instead. These business-derived tuples are contextual, not a continuously synchronized
business-history graph. Resource→Organization edges still require either request-local
facts for bounded checks or explicitly maintained authoritative topology in FGA for graph
enumeration. Opaque IDs alone do not prove same-Organization relationships: validate every
write/link and compare the loaded resource's Organization with admitted tenancy.
Keep the Access-owned member fact contextual only: omitting it cannot revoke an accidentally
persisted member tuple. This sketch covers human access; system mandates need an explicit
model/policy, not a membership or wildcard bypass.

Use this same illustrative decision matrix for both candidates; these are expected outcomes,
not executed tests. Alice is an active North/South member, North A lead and B developer,
with an overlapping group-reader grant on A and a separate direct read grant on South D.

| Scenario | Read | Manage / sensitive selection |
| --- | --- | --- |
| North A, lead plus group reader | Allow once in results/count | Allow project management |
| North B, developer | Allow | Deny project management |
| North C, no matching grant | Deny | Deny |
| South A, no local grant despite the same resource ID | Deny | North A grant cannot cross Organization |
| South D, its own direct read grant | Allow | No management implied |
| North private Personnel, membership-admin grant only | Deny | Deny private export/AI selection and unapproved sensitive self-assignment |
| Fresh North operation after North suspension | Deny protected admission even with retained grants | South D/global identity remain usable; already-admitted North context stays fixed |

For dynamic roles, module permission relations link resource→role#assignee; assignment and
role metadata remain distinct. Module files/type extensions compile to a shared model, and
the same trusted-writer limits apply across modules. Pin authorization model ID across
checks and tuple writes; roll out readers/model validation/tuples deliberately rather than
implicitly using “latest.” Definition changes can affect many existing grants immediately.
[Modular models](https://openfga.dev/docs/modeling/modular-models),
[custom roles](https://openfga.dev/docs/modeling/custom-roles),
[model IDs](https://openfga.dev/docs/getting-started/immutable-models).

Contextual tuples have a documented request limit of 100 and share the request's trust and
freshness. They are useful for one resource's current facts; enumerating thousands of
business assignments this way is not a complete query solution. Token-derived tuples retain
token staleness. [Contextual tuples](https://openfga.dev/docs/interacting/contextual-tuples).

| FGA query strategy | Complete pages/totals/exports assessment |
| --- | --- |
| Search ordered candidates, then Check/BatchCheck | Correct pages require scanning onward until enough allowed rows or candidate exhaustion; cursor tracks last scanned candidate, not merely last visible item. Exact counts require evaluating all matches. Sparse visibility can be expensive; each check/error/model/fact snapshot matters. One business page followed by checks is insufficient. |
| ListObjects → database filter/sort | Valid only with a proven complete authorized set at the defined consistency boundary. In pinned v1.22.0, ListObjects has count/deadline bounds and no continuation/completeness marker; successful shorter output can be partial. Streamed enumeration removes the count cap but does not prove deadline completeness. Cannot certify exact totals/global sort/complete exports from these outputs. |
| Relational/search authorization index + checks | Rechecks can remove stale positives, but missing derived entries cause false negatives. Exact results need completeness, deletion/reconciliation/freshness and aligned business/authorization snapshots. This introduces the synchronization contract we are trying to avoid; adopt only if measured benefit warrants it. |

Provider facts and source-code evidence for the table are in the
[pinned ListObjects analysis](access-authorization-primary-sources.md).
The official [search guide](https://openfga.dev/docs/interacting/search-with-permissions)
describes patterns; its pagination wording is not proof that this API supports resumable,
complete authorized-ID pagination. A different engine/version requires new evidence.

## Decision timing, revocation and dependency failures

| Boundary | Recommended contract / unresolved stronger requirement |
| --- | --- |
| Human admission | Reuse persisted Active lookup on a fresh scope. A committed suspension blocks later admission queries; a concurrent earlier snapshot can admit. The immutable already-admitted operation continues. Do not rebind/clear tenant or promise instant cancellation. |
| Resource detail/write | Evaluate current resource permission after admission and before protected effects. Record the decision boundary; a resource version token protects business conflicts, not a concurrent grant revision. Permission rechecks can deny an admitted operation without mutating its context. |
| Strict permission revocation versus commit | If required, in one owning database coordinate every relevant grant/role/reassignment mutation and protected write through a shared authorization revision/lock protocol. Conditional business version alone and an unlocked last-minute recheck leave a race. Negation, group/role edits and cross-module facts complicate that protocol; it needs its own concrete proof. No global protocol is adopted here. |
| OpenFGA consistency | Higher consistency bypasses its caches; it does not atomically join the application transaction or establish a revocation fence. FGA-owned grant updates and relational business/audit writes have dual-write/ambiguous-outcome costs even if deployed beside the database. Outbox intent can make retry recoverable, but delayed propagation is not immediate revocation. |
| Cache/token reuse | Current admission does not cache memberships in the cookie. If any membership/grant/negative-result cache is added, specify revision/invalidation, maximum stale window and fail-unavailable behavior. Never accept an old login cookie/tuple alone as current Organization admission. |
| Exports and background work | Choose snapshot/continuing-human/company mandate policy explicitly. New operation boundaries use current admission/permissions; a long-running operation's snapshot can retain older access. A tighter revocation window requires chunking/recheck and staged output/download policy, not mutation of operation context. |

[FGA consistency](https://openfga.dev/docs/interacting/consistency) documents the read
preference; cross-store non-atomicity above is architectural inference. A native single-store
predicate can share a statement snapshot with business rows, but only genuinely local or
published facts participate. Remote Contract responses and lagging projections cannot gain
that guarantee merely by being passed into SQL.

Recommended outcomes are consumer policy, not current library response mappings:

| Condition | Outcome |
| --- | --- |
| Unauthenticated | Native challenge (API 401 or configured browser login); preserve scheme selection. |
| Missing/foreign/inaccessible Organization or resource | Non-disclosing 404 where selected; the current sample already conceals failed Organization admission. Known visible resource with insufficient action can use 403 by explicit policy. |
| Malformed external input / absent required selection | Validate and report 400 where appropriate. Uninitialized server operation context is a programming/configuration failure, never anonymous or all-tenants fallback. |
| Access/permission store timeout/outage or incomplete required facts | Fail protected operation without unchecked rows/effects; propose 503 for known temporary dependencies, distinct from 403/404 and empty complete lists. Current sample's unmapped database exceptions yield 500, not 503; changing that is future consumer work. |
| FGA false decision versus engine error | False is deny. Timeout/transport/invalid model/condition-evaluation failure is error, not allow or an ordinary no-results page. Known partial query output is unavailable for complete-result endpoints. Undetectable truncation excludes the ListObjects plan from such endpoints. |
| Cancellation, write conflict, ambiguous commit | Propagate cancellation; rollback/dispose a failed local transaction. Report authorized revision conflict (409) and reload/redecide; do not silently overwrite. A lost commit response needs durable operation reconciliation before retry, not an assertion of rollback. |

## A1.1: focused membership suspension/reactivation

Retain the same user, external identities, Personnel records, historical responsibilities,
contributions and original audit attribution. Customer IdP management/global logout is
outside this action. Another active Organization and permitted global account functions
remain usable. Reactivation targets the same Suspended membership; Removed→Active/reinvite
is a separate consumer lifecycle decision.

Propose narrow suspend/reactivate intents with admitted Organization, target membership ID,
expected revision and, if retry identity is required, an operation ID. No arbitrary status
setter. Reauthorize the acting administrator on every call, including repeats/reconciliation,
using `access.membership.suspend` / `access.membership.reactivate` or reviewed equivalents.
Scope every target read/write by admitted Organization despite Access's global registry.
Do not add a tenant filter to global identity lookup. Existing cookie mutations retain native
antiforgery; non-HTTP callers need equally trusted admission and permission entry checks.

### Proposed native protocol and outcomes

Add an explicit monotonically advancing revision in a later reviewed consumer migration;
current MembershipRow has none. Compare the expected revision, not only status: Active→
Suspended→Active must invalidate an old Active request. Native EF original-revision
concurrency can arbitrate tracked writes; SQL below explains the intended predicate only.
[EF concurrency](https://learn.microsoft.com/en-us/ef/core/saving/concurrency).

```sql
-- After scoped administrative authorization and transition validation:
UPDATE access.memberships
SET status = :requested_status, revision = revision + 1
WHERE organization_id = :admitted_org AND id = :membership_id
  AND revision = :expected_revision AND status = :expected_status;
-- One affected row permits staging one accepted-change audit, in this transaction.
-- Zero requires a fresh scoped read/classification, not blind retry/overwrite.
```

| Authorized request state | Proposed result |
| --- | --- |
| Active→Suspended or Suspended→Active, revision matches | Changed; advance revision once, stage one accepted transition audit, save/commit together. |
| Already requested state, revision matches | AlreadyInRequestedState; no transition/revision bump/change audit. Optional operational attempt telemetry is separate. |
| Revision differs, including identical concurrent intent or ABA | Conflict by default, even if status now matches. Reload to issue a new intentional request. A durable exact-operation replay may return its original receipt after reauthorization. |
| Same operation ID, different intent/target/payload | Conflict; do not reinterpret a previous operation or disclose a foreign target. |
| Removed target or forbidden lifecycle transition | InvalidTransition only after scoped visibility/authorization; no implicit reactivation. Missing/foreign/unauthorized target uses the selected non-disclosing result. |
| Unknown commit outcome | If an operation receipt was chosen, read its scoped result before retry; persist receipt and transition audit in the same transaction. Without it, reload reports current state but cannot assert which attempt caused it. No exactly-once claim. |

Competing identical commands on one revision yield one Changed and one Conflict (or its own
exact receipt replay); opposing commands cannot silently overwrite the winner. No-op results
are observations, not promises that a future request will still see that state. If last-admin
or approval invariants are selected, their check and updates need a common concurrent
arbitration boundary; two row versions on different administrators do not prevent write skew.
Administrative permission revoked mid-command retains the resource-decision timing contract
above unless Umbrine separately selects a stronger revision/lock fence.

Compose the existing [transactional audit mechanism](../../src/Gloaming.Auditing/docs/transactional-audit.md)
inside Access's own native transaction, with its explicit mapping/staging/validation wiring.
Access does not install it today. Capture admitted Organization, membership and stable user
reference, acting administrator, previous/new status, revision, timestamp and versioned safe
details; reason visibility is product policy. Use the executing actor, preserving initiator
only when trusted. Audit/save failure or a known rolled-back transaction leaves neither
committed transition nor accepted-change audit; an ambiguous commit may have persisted both
and must be reconciled. Do not publish external effects before commit. Denied attempts and
no-ops need their own logging/audit policy; an accepted-change audit must not claim they changed
membership. Existing audit validation is not automatic completeness or tamper evidence.

### Retained grants, work and reactivation

Suspension closes current human admission even if grant rows/tuples remain. It does not require
deleting them or ending Personnel engagement. Reactivation still checks current resource
permissions, Organization/capability availability and licensing/business gates. Umbrine must
choose retained grants becoming eligible again, explicit reapproval, or revoke/reissue; do not
encode one choice accidentally by restoring deleted identities or all old tuples.

| Work/relationship | Proposed policy boundary; consumer choice still required |
| --- | --- |
| Company-owned work under an authorized system actor | Current company mandate, Organization/capability and business checks apply; initiator suspension alone need not cancel it. Preserve human attribution and the actual system executor. |
| Deferred work requiring continuing personal authority | Fresh human admission/permission at execution; suspension blocks it with a visible blocked/operator outcome. Do not retry a known suspension endlessly or silently upgrade to system authority. |
| Reminders/notifications | Reassess recipient and protected content at delivery. Global account/security messages are distinct from Organization reminders; suspension is no blanket cancellation of company work. |
| Required business responsibilities | Start with manual reassignment and module-owned warnings/transition/draft-definition checks when responsibility cannot be fulfilled. No generic offboarding or assignment engine, history rewrite or automatic workflow cancellation. |

## A2 handoff and operational trade-off

A2 receives four separate boundaries: authentication/canonical actor, current human membership
admission, module permission/selection, and tenant data isolation. Keep the current middleware
order and use a late imperative resource check as the first composition. A native policy
handler needing established tenancy requires a separately reviewed ordering/scheme/anonymous
proof; do not silently move middleware. New worker human operations admit afresh; calls within
one operation share immutable context. Explicit system mandates use a different consumer path.

A2 may assess optional PostgreSQL RLS for cross-tenant rows. Tenant equality cannot enforce
these within-tenant permissions, complete queries, audit/business invariants or cross-store
revocation. Access's pre-admission global identity/membership registry must remain usable
without a fake tenant/filter bypass. No RLS policy, migration, mandatory provider dependency
or Tenancy→Access/FGA dependency is introduced. A2's runtime-role, connection, pooling,
transaction/retry and bypass proof matrix remains open; this is coordination, not completion
of A2 research. The subsequent [A2 assessment](tenant-authorization-assessment.md) now
supplies the role/lifecycle/protection analysis; executable RLS proofs remain proposed.

| Choice | Cost/fit recommendation |
| --- | --- |
| Native + local relational facts | Fits current shared-database/module-owned design and C1 query shaping. No new service; permission SQL/indexes, semantic parity and role migrations are consumer work. Module boundaries can still require a fact contract/projection; native handlers alone do not solve it. |
| OpenFGA direct-grant authority | Useful if graph reuse/sharing across services is substantial. Adds service/datastore availability, authentication/tuple-writer security, model/tuple rollout, observability/backups/capacity and cross-store audit/revocation recovery. Benchmark sparse lists; SDK convenience is not proof of complete selection. |
| Generic custom engine/library | Not justified by this source assessment. First compare small local methods/handlers/predicates and existing audit/EF/context mechanisms. Repeated simple mechanics may later earn extraction with independent consumer evidence. |

## Unresolved consumer decisions and smallest proofs

Before an implementation scope, settle only the decisions it actually needs:

1. Administrative assignment bounds, self-suspension, last-admin protection, approval/reason
   disclosure and target concealment; no private-data bypass implicit in administration.
2. Reactivation grant eligibility and Removed membership lifecycle; trusted Personnel linking
   and whether business responsibilities bind Personnel, UserId or both.
3. Which roles are fixed/configurable, seeded-copy upgrade behavior, scope inheritance,
   explicit deny if needed, and ownership of cross-module bundle/link fact contracts.
4. Permissible revocation window at admission, write decision/commit, export chunks/download,
   AI/cache reuse and worker execution. Strict commit fencing is a separate requirement.
5. Exact versus bounded search/count needs, cardinalities, stable-page expectations, sparse
   visibility budgets, and chosen failure/retry/operation receipt policy.

| Small follow-up | Minimum useful evidence; no implementation authorized here |
| --- | --- |
| **A1.1 first: one authorized Access journey** | Narrow commands plus revision/audit mapping. Unauthorized/foreign direct HTTP and non-HTTP calls have no side effects; same-cookie suspension denies business read/mutation, preserving persisted state/events/outbox/audit. Global identity/account access and second active Organization work. Cover Suspended in-flight continuation as well as existing Removed; next admission denies. Concurrent identical/opposing requests and ABA produce chosen outcomes; status/audit/receipt rollback and ambiguous-commit reconciliation agree. Reactivation reuses identities/history and obeys changed grants/unavailable capability. Pick one system-work and one continuing-human policy. |
| **One module's authorized query** | Extend C1's native pattern with fixed/local scoped user/group/derived grants; independently expected detail/write/page/count/export selection, private-field behavior, overlapping grants and tenant-colliding IDs. Sparse early candidates must still produce complete pages/totals; revoke/reassign between operations and count/page statements. Measure plans at realistic cardinality and explicitly choose snapshot policy. Include AI source selection if that endpoint is in the chosen scope. |
| **Only if cross-module facts are needed** | Prove one complete bounded Contract or owner-published fact view. If a projection is chosen, prove deletion/unlink, lag gating, revocation, restart/rebuild and no missing authorized rows before claiming complete selection. |
| **Only if FGA remains a serious contender** | Pin the documented engine/API/SDK/model, parse/test the representative model, compare the same finite permission matrix and a sparse large-set query under limits/deadlines/errors. Demonstrate the chosen complete-result plan, grant write/audit failure recovery and model rollout. Reject a complete-list claim if the API cannot establish exhaustion. No engine benchmark/service deployment is required for A1.1. |

**Library:** existing immutable contexts, EF concurrency/transactions and explicit audit are
mechanisms to compose. No new reusable mechanism or public interface was proven; no blanket
rejection of future simple extraction follows. **Template:** later editable admission, late
resource checks, module permission queries, administrative audit and work-policy recipes,
after executable adoption. **Sample/product:** roles, grant authority, lifecycle outcomes,
Personnel links, query fields, sensitive-data decisions and revocation semantics remain owned
by Umbrine/modules. T1 generation stays unchanged.

## Verification and review

New work is documentation and primary-source inspection only. No C# build, runtime test,
database experiment, FGA DSL validation or deployment benchmark was executed. C1/E3/E9
results linked above are historical evidence, not new authorization proofs. Documentation
validation checked 349 local links/anchors across the eight changed documents; all resolve.
The temporary TypeScript link/whitespace check and `git diff --check` pass, including explicit
checks of the new untracked reports. Only Markdown changed. The original checkout's status,
HEAD and index SHA-256 are unchanged; the assessment index is still empty and its branch
has no upstream. No files were staged or unstaged.

Review this report's authority/query assumptions, A1.1 transition/concurrency table and
small proof scopes first; then the pinned primary-source note and the A1/A2/roadmap/design
status changes. Remaining gaps are the explicit consumer decisions and unexecuted proofs
above. Changes remain unstaged, with no commit or push.
