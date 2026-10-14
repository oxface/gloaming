# A1: Access and resource authorization research

Status: consumer-requested research/backlog item, 2026-10-14. Assessment and documentation
only. No runtime implementation, public library interface, engine selection or package
promotion is authorized. [Preliminary intake and primary sources](../reports/access-authorization-research-intake.md)
record initial evidence; they are not a completed comparative evaluation.

## Priority and placement

High-value consumer-driven research. Schedule after the current DDD/domain-reaction and
C1 query-pattern work, before Access-specific T2 template choices and the P2 readiness
review. This does not interrupt active implementation slices or authorize implementation
once research finishes. [A2](a2-tenant-resource-authorization-rls-research.md) can collect its
baseline concurrently; finalize its composition recommendation after A1 defines authority
and scopes. Query requirements may justify moving research forward when Umbrine needs it.

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
