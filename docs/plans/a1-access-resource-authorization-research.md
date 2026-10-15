# A1: Access and resource authorization research

Status: A1 + A1.1 assessment prepared for owner review, 2026-10-16, against fetched
`origin/main` at `7ea6652`. The [comparative assessment](../reports/access-authorization-assessment.md)
recommends native resource checks/module-owned relational predicates initially and a focused
consumer suspension/reactivation proof. [Pinned primary-source evidence](../reports/access-authorization-primary-sources.md)
records OpenFGA API/query limits. Assessment and documentation only: no runtime implementation,
public library interface, engine adoption or package promotion is authorized. The
[intake](../reports/access-authorization-research-intake.md) retains preliminary dated evidence;
the scope below records the requested questions and follow-up obligations.

## Priority and placement

The owner started this assessment after DDD/domain-reaction and C1 query-pattern work;
C1 is present in the fetched baseline. Assessment findings inform Access-specific T2 and
P2 decisions without authorizing implementation or adding a pre-development gate.
[A2](a2-tenant-resource-authorization-rls-research.md#a1-assessment-input-2026-10-16)
now has A1's proposed authority, scopes and query boundary as input; its composition/RLS
assessment and executable proofs remain separate. A1.1 stays a focused follow-up within A1.

## Consumer and boundaries

Umbrine is a modular company-operations SaaS built on Modulith Foundry and Gloaming. It needs:

- Stable application users linked explicitly to external identities, multiple Organization
  memberships, invitations and pre-registration. Identity-linking and invitation lifecycle
  policy remain consumer-owned; an email address alone is not a trusted identity link.
- Organization-scoped Personnel records that can exist without login accounts. Assess the
  optional Personnel-to-user link, dormant/invited users and trusted activation. A Personnel
  record or business assignment does not itself authenticate a principal.
- Organization administrators who manage administration without automatically gaining private
  business-data access. Clarify whether administrators may grant themselves sensitive access;
  if not, assignment authority needs its own bounds/approval policy, not just a missing read grant.
- Permission definitions owned by their modules; avoid a central product-wide permission enum
  or an Access dependency inside every technical capability. Evaluate fixed roles, editable
  default roles and Organization-configurable roles, including whether editing a default
  changes a shared baseline or only an Organization-owned copy.
- Project responsibilities that imply permissions in their own scope: one person may lead A
  and develop B. Overlapping positive grants combine within matching scope; no role on A must
  become an Organization-wide grant. Explicit-deny behavior, if required, needs a defined rule.

Licensing entitlements and business-state invariants remain separate consumer-owned gates.
An authorization decision cannot make an inactive license valid or authorize an impossible
business transition. FGA is not the product's business state machine.

## Working vocabulary to resolve in the research

These definitions frame the assessment, not new settled entries in CONTEXT.md or library types.

| Term | Working meaning and separation |
| --- | --- |
| Principal | The authorization subject: an admitted application user or explicit system identity. Native ClaimsPrincipal carries authentication/claims; Gloaming Actor identifies the operation's actor. Define their mapping rather than equating them. |
| Resource | The object or scoped collection an operation concerns: Organization, project, document or Personnel record; identify owner module and tenant. |
| Permission | A module-defined action/capability evaluated against a resource/scope, such as reading a private Personnel record. |
| Grant | An assignment or relationship that contributes permission for a principal/userset within a defined scope. Establish who may create/revoke it and where it is authoritative. |
| Authorization role | A permission bundle used for access decisions; distinguish its definition from a user's scoped assignment. |
| Userset | A set of principals defined by a relation, such as a group's members or a role's assignees; not automatically a business-role history record. |
| Business role | A domain responsibility, such as project lead/developer, potentially with effective dates/history. It may imply permissions through explicit consumer rules. |
| Licensing entitlement | A product/plan feature entitlement checked separately from resource grants and domain invariants. |

## Questions and comparative method

Use the same scenario matrix for OpenFGA and a focused native/relational baseline. The latter
is native ASP.NET resource handlers plus explicit relational permission predicates, not a
mandated custom engine. Assess a custom engine only if actual semantics require it. Compare:

1. **Authority.** For each membership, grant, role definition, business responsibility and
   identity link, name its authoritative owner/store, updater and read path. Evaluate OpenFGA
   as the authority for direct grants versus module-owned business relationships. Avoid
   continuous relationship mirroring by default. Assess contextual relationships and direct
   queries first. Any unavoidable derived copy/index needs an update, deletion, reconciliation,
   freshness and failure contract; do not hide a second source of truth behind an adapter.
2. **History and derivation.** Separate effective/current authorization from historical business
   responsibilities. Explain how assignment, reassignment, backdating, expiry and role edits
   affect permissions without rewriting business history or unintentionally retaining access.
3. **Models and roles.** Inspect OpenFGA modular models, custom roles, model/version rollout and
   shared namespaces. Modular authoring does not prove runtime module isolation. Compare fixed,
   editable-default and configurable roles, their upgrade/customization semantics, and prevent
   scoped assignments widening authority.
4. **Consistency.** Define grants/revocations at check, transaction, commit, long-running export
   and worker execution boundaries. Cover concurrent role changes, caches/tokens, TOCTOU and
   caller retries. A higher-consistency engine read alone does not atomically synchronize an
   application database with a separate authorization store.
5. **Queries.** Produce equivalent detail/write/list semantics. Authorized filtering must compose
   with business predicates, deterministic sorting, pagination, counts, exports and AI-source
   selection. Include sparse visibility, joins and large sets. Filtering one already-paged
   business result is not proof of complete pages or correct totals. Assess bounded/partial
   accessible-ID results and actual pagination APIs against pinned engine/SDK versions. The
   relational baseline must honor module context/schema ownership: name the query owner and
   authorized fact contract/read model instead of assuming arbitrary joins across private
   module DbContexts. Include any required projection/duplication in the comparison.
6. **Native composition.** Show imperative IAuthorizationService resource checks alongside actor
   establishment and admitted tenancy; connect to A2's ordering assessment. Preserve non-HTTP
   module/worker entry checks and module contracts. Do not assume HTTP enforcement protects
   direct calls or that tenant selection grants resource permission.
7. **Unavailability.** Distinguish deny, missing/malformed identity/context, dependency outage,
   timeout and incomplete authorization results. Recommend explicit outward responses and
   retry behavior. Do not return unchecked records or silently label partial lists/counts as
   complete. Specify revocation exposure for any cached/offline policy; do not assume fail-open.
8. **Cost and ownership.** Compare deployment, operational effort, query plans/cardinality,
   schema/model migration and coupling. A small relational predicate may be preferable without
   becoming a generic engine. OpenFGA is an evaluation candidate, not a selected dependency.

## Representative scenarios and bounded proof proposals

Research returns representative models, request paths and conceptual SQL/LINQ/FGA examples;
these are analysis artifacts, not promoted runtime interfaces. Propose the smallest follow-ups:

| Scenario | Required question/proof |
| --- | --- |
| Same user, two Organizations | Independent membership/admission; foreign grants cannot cross the selected tenant. |
| Personnel without login, later linked user | Business responsibility history survives; access begins only through an admitted trusted link and relevant grants. |
| Organization admin and private Personnel | Administration alone gives no private read/export/AI-source visibility; delegated grant authority is explicit. |
| Lead on A, developer on B, plus group grant on A | Positive-grant union produces expected per-resource permissions without scope expansion or duplicate query results. |
| Role definition edit or concurrent revocation | Document decision timing for an in-flight write/export and the permissible stale-access window; prove the chosen contract later. |
| Sparse visibility across a large ordered set | Correct authorized filters, joins, totals and deterministic pages; no naive page-then-check shortcut. |
| Export and AI retrieval | The same permission boundary applies before data enters exports, retrieval context, cached results or tools; cover revocation/selection timing. |
| HTTP, worker and internal module call | Consistent admitted actor/tenant/resource checks, explicit system principals and missing-context/outage outcomes. |

## A1.1: Organization membership suspension follow-up

Added at Umbrine's request on 2026-10-15. This is a focused addition to A1's Access lifecycle
assessment and candidate implementation/proof scope, not a separate pre-development gate.
It does not authorize runtime changes, new Contracts or a Gloaming abstraction. It can become
one small consumer adoption when the owner selects Access work; neither a complete FGA engine
assessment nor A2's optional RLS implementation must precede a narrow consumer-owned proof.

### Requirement and existing boundary

An authorized Organization administrator can suspend a person's membership in that Organization.
The application user, external-identity links, Personnel records and completed contributions
remain intact. The person can still authenticate, use permitted global account functions and
enter other Organizations with active memberships. Customer IdP administration stays with the
customer. Suspension is distinct from ending a Personnel engagement or deleting business history.
Reactivation reuses the same identities and retained records, subject to current membership,
permissions and Organization/capability availability; it never links accounts by email.

The consumer cited HEAD `f55d803`. On 2026-10-15, inspection in research baseline `df83518`
confirmed the selected Access and AdmissionTests files are unchanged from that HEAD. Existing
source/test definitions establish the following evidence; no tests were executed for this intake:

| Foundation | Inspected evidence and limit |
| --- | --- |
| Membership lifecycle values already exist | [AccessRows](../../samples/Wholesale/modules/Access/Access/Persistence/AccessRows.cs) has Active, Suspended and Removed. No new admission enum/model is needed. |
| Fresh persisted membership admission | [ResolveMemberOrganizationAsync](../../samples/Wholesale/modules/Access/Access/ApplicationAccess.cs) selects only Active memberships. ResolveUserAsync looks up the stable user through issuer/subject independently. |
| Current public surface is read-only | [AccessRegistration](../../samples/Wholesale/modules/Access/Access/AccessRegistration.cs) registers queries through [IApplicationAccess](../../samples/Wholesale/modules/Access/Access.Contracts/IApplicationAccess.cs). No authorized suspend/reactivate business command is present in these selected files. |
| Membership rules stay with the consumer | [OrganizationTenantResolver](../../samples/Wholesale/HttpIdentityDemo/HttpIntegration/OrganizationTenantResolver.cs) calls the Access admission query; Gloaming tenancy establishes the selected context. Explicit public-Organization routes are a separate policy, not protected member capabilities. |
| Denial after status change, global identity retained | [AdmissionTests](../../samples/Wholesale/HttpIdentityDemo.Tests/AdmissionTests.cs) defines MissingSuspendedAndRemovedMembershipDenyWithoutPublishingTenant and RevocationBlocksNextAdmissionWithSameCookieButKeepsGlobalIdentity. These exercise direct requests and identity/context endpoints, not the new administrative lifecycle. |
| An admitted operation keeps its context | AlreadyAdmittedOperationRetainsContextWhenMembershipIsRemoved explicitly uses Removed. Extend the focused proof to Suspended as well; immutable operation context is already the foundation. No instant cancellation of admitted work is claimed. |
| Multiple memberships and historical rows are possible | [AccessDemoSeed](../../samples/Wholesale/modules/Access/Access/AccessDemoSeed.cs) gives application-alpha two active Organizations. [AccessDbContext](../../samples/Wholesale/modules/Access/Access/Persistence/AccessDbContext.cs) enforces one Active/Suspended membership per Organization/user with a filtered unique index; removed records can remain. |

### Remaining consumer/template behavior to assess

1. **Narrow administrative operations.** Propose consumer-owned suspend/reactivate intent,
   trusted Organization scope, target membership identity and explicit outcomes before adding
   Contracts. Reauthorize every administrative request, including repeats. Assess changed,
   already-in-requested-state, conflict, invalid transition and missing/unauthorized target
   outcomes without revealing foreign memberships. Do not expose arbitrary status assignment
   or global user/IdP management through this action. Removed-to-active behavior remains an
   explicit separate product decision; current reactivation scope targets Suspended membership.
2. **Concurrency and repeat semantics.** MembershipRow has no explicit revision/concurrency
   token in the inspected model. Assess native expected-revision or conditional-write handling,
   including suspend versus reactivate races, repeated requests, ambiguous commits and status
   changes that return to an earlier value. Opposing commands must not silently overwrite an
   accepted decision. Equal repeats must not duplicate a status transition or its business
   change audit. A generic command/idempotency engine is not a prerequisite; define outcomes
   and prove the selected native protocol first.
3. **Authority and transactional business audit.** The actor must hold the consumer's scoped
   membership-administration permission, independently of private business-data visibility.
   Change the membership and append its accepted business audit in one owning native EF
   transaction. Assess reuse of existing [Gloaming audit integration](../../src/Gloaming.Auditing/Gloaming.Auditing.EntityFrameworkCore/README.md),
   without claiming it is already installed in Access. Include Organization, target membership
   and stable user reference as needed, acting administrator, previous/new status, time and
   versioned safe details. AccessDbContext is a global registry used before tenant establishment;
   scope the administrative write by admitted Organization and target membership without
   making global identity queries tenant-dependent. Rollback must leave neither a status
   change nor an accepted-change audit. Existing tests mutate status directly as admission
   setup, which does not prove an authorized/audited administrative path. Decide separately
   how denied attempts/no-ops are recorded. Umbrine owns last-admin,
   self-suspension, approval and reason/disclosure policy; selecting such a rule also requires
   its concurrent-change proof. Do not invent those policies during extraction.
4. **Admission at operation entry.** Keep current request timing: subsequent fresh human
   admission is denied, while an already admitted operation retains its context. Backend
   routes and new non-HTTP human operations need current admission as well as resource checks;
   a tenant key, queued user ID or retained FGA relationship alone is insufficient. Internal
   calls within one admitted operation retain that boundary. Explain this in adoption guidance
   rather than adding per-call tenant mutation or operation-wide revocation machinery.
5. **Reactivation and grants.** Change the existing suspended membership, not the application
   user or linked identities. Access afterward still passes current permissions, membership,
   Organization and capability checks. Decide whether retained grants remain eligible, require
   review or are revoked/reissued. Neither unconditional restoration nor blanket grant deletion
   is assumed. Completed contributions, original attribution and Personnel history remain.
6. **Caching.** The inspected admission query reads persisted Active membership; no new cache
   was added. If membership/grant/token caching is proposed later, specify invalidation, stale
   admission exposure and retry/outage behavior before claiming next-request suspension takes
   effect. An already issued login cookie alone does not grant current Organization admission.

### Queued work, reminders and responsibilities: consumer decisions

| Work or relationship | Decision to record; no automatic offboarding behavior selected |
| --- | --- |
| Company-owned work executed by an authorized system identity | Define the company's mandate and current Organization/capability/business checks; suspension of an initiator alone need not stop it. Keep original human contributions attributable and the actual executing actor explicit. |
| Deferred work requiring the person's continuing authority | Define execution-time admission/permission checks and a visible blocked/retry/operator outcome. A system worker must not silently turn an old human request into unlimited system authority. |
| Reminder or notification delivery | Decide recipient admission and content disclosure at delivery, including global account/security messages versus protected Organization reminders. Suspension is not a blanket cancellation of all company reminders. |
| Required responsibilities held by a suspended person | Umbrine starts with manual reassignment and module-owned warnings when responsibilities cannot be fulfilled. Transition validation and draft-definition checks belong to each business module; no generic assignment engine or automatic workflow cancellation is requested. |
| Stale FGA/resource grants | Current human admission remains required. Retention, cleanup and reactivation eligibility are explicit policy; grant presence alone is not proof of usable membership. |
| Personnel engagement and completed history | Keep engagement lifecycle separate. Suspension does not erase prior work or rewrite historical role/contribution attribution. |

### Smallest useful later implementation and proof slice

Propose one **consumer-owned Access administration journey**: an admitted authorized
administrator suspends/reactivates one membership through a narrow business Contract, with
explicit repeat/conflict outcomes and native transactional business audit. Keep the existing
active-admission query, HTTP resolver, identities and tenancy core. No FGA service, new role
editor, automatic offboarding, global logout or customer IdP provisioning is required. A
focused native/consumer permission check is a valid first adoption while A1's engine comparison
remains open. Any actual Contract/schema/proof changes need their own owner-reviewed scope.

Its bounded proof set should cover:

- Unauthorized administrator and foreign-Organization target attempts, including direct API
  calls; administration grants do not themselves give private Personnel access.
- Repeated suspend/reactivate, competing identical/opposing commands and audited rollback
  after native database/audit failure. Exactly the chosen accepted transitions/audits survive.
- Protected business read and mutation denied after suspension using the same issued cookie;
  verify persisted business state, events/outbox/audit as applicable have no denied mutation
  effects. Global identity/permitted account functions and a second active Organization remain
  usable by that same user. Verify fresh identity resolution still returns the same linked user.
- Suspension after admission preserves the original operation; the next operation is denied.
  Retain the existing Removed case and add the corresponding Suspended case, without asserting
  instant in-flight cancellation. Document intentional public-route exceptions.
- Reactivation of the same membership/user/identity keys and retained records; changed/revoked
  permissions or unavailable Organization/capability still deny the affected protected work.
- A fresh non-HTTP human entry that denies suspended admission, plus representative company-owned
  system work and person-authorized work following their explicitly chosen different policies.
  Do not expand this into cancellation/reassignment tests for every business workflow.

No narrow reusable gap is demonstrated yet. Existing tenancy, native EF and explicit audit
are candidate mechanisms to compose; the missing administrative lifecycle, authority, repeated
request semantics, guidance and combined proofs remain consumer/sample/template work. A1/A2
keep their existing priority. This follow-up adds no dependency from Tenancy to Access/FGA.

## Deliverables and completion gate

Return an evidence-backed recommendation with primary-source/version references, representative
models/queries, an authority/consistency matrix, query limitations, explicit unresolved decisions
and bounded follow-up proofs. Mark facts, inference and proposals separately. It is valid to
recommend consumer-owned Access code/template patterns and no new Gloaming mechanism.

Classify each candidate: consumer Access policy, editable template setup, or a small reusable
technical mechanism with independent-adoption evidence. Preserve the package-free tenancy
core and optional families. No mandatory OpenFGA, Access module, mediator or authorization
framework follows from this item. Any runtime prototype/proof or public-interface work needs
a separately bounded owner-reviewed implementation scope.
