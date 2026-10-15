# A1 primary-source evidence: authorization providers and native composition

Assessed 2026-10-16 (Europe/Kiev). This is source inspection for the
[A1 assessment](../plans/a1-access-resource-authorization-research.md), not an engine
selection, runtime proof, package addition or public-interface proposal. **Fact** below
means provider documentation or released source; **assessment** means an inference for
Umbrine. All external links were consulted on that date. Live documentation can change;
released source wins where they disagree.

## Version baseline

| Component | Inspected baseline and primary evidence |
| --- | --- |
| OpenFGA engine | Latest published release resolved to **v1.22.0**, released 2026-10-06, commit `a38d5d0`: [release](https://github.com/openfga/openfga/releases/tag/v1.22.0). This is an assessment pin, not a dependency decision. |
| Engine API | The engine's [tagged go.mod](https://github.com/openfga/openfga/blob/v1.22.0/go.mod#L32) pins `github.com/openfga/api/proto v0.0.0-20260319214821-f153694bfc20`. Inspect the [matching service schema](https://github.com/openfga/api/blob/f153694bfc20/openfga/v1/openfga_service.proto), rather than assuming API `main` is released. |
| .NET SDK | Latest release resolved to **OpenFga.Sdk v0.10.4**, released 2026-07-20, commit `ec8ee04`: [release](https://github.com/openfga/dotnet-sdk/releases/tag/v0.10.4), [tagged usage](https://github.com/openfga/dotnet-sdk/blob/v0.10.4/README.md). No SDK was installed or executed. |
| Native comparison | [ASP.NET Core 10 resource authorization](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/resource-based?view=aspnetcore-10.0), current EF Core guides and [PostgreSQL 18 isolation semantics](https://www.postgresql.org/docs/18/transaction-iso.html). These document capabilities, not validation of an Umbrine implementation. |
| Repository pins at `7ea6652` | [global.json](../../global.json) selects .NET SDK 10.0.401; [central packages](../../Directory.Packages.props) pin ASP.NET/EF packages at 10.0.12 and Npgsql EF at 10.0.3. These were read, not built or executed for this assessment. No FGA dependency exists in the assessment change. |

## OpenFGA facts and their implications

**Authority.** OpenFGA recommends keeping entity hierarchies and filtering/sorting/join
data in application databases. Direct resource permissions and role memberships may be
authoritative in OpenFGA when the application does not need another authoritative copy;
role metadata still needs application storage.
[Source-of-truth guidance](https://openfga.dev/docs/best-practices/source-of-truth).
**Assessment:** Access can remain authoritative for identity, membership and its
administrative grants; business modules own responsibilities and their history. Prefer
local derivation or bounded reads over continuously copying business relationships.
An OpenFGA-only direct-sharing authority is a possible later option, with its own audit,
administration and query contract; it is not a reason to move business history there.

**Contextual tuples.** They are request-local, model-validated relationships, can be used
without persisting the relationship, and take precedence over an identical stored tuple.
The documented limit is **100 per request**, also enforced in the
[pinned tuple schema](https://github.com/openfga/api/blob/f153694bfc20/openfga/v1/openfga.proto#L201-L207).
Token-derived membership can remain effective until token expiry.
[Contextual-tuples guide](https://openfga.dev/docs/interacting/contextual-tuples).
**Assessment:** trusted current facts for one resource/check can avoid mirroring. Context
is supplied by consumer code, not untrusted callers. Omission of one contextual tuple does
not delete a stored grant, and supplying 100 facts does not establish completeness over
thousands of projects or responsibilities. Current persisted admission still applies.

**Modules and roles.** Modular files, `fga.mod` and extensions compile to one authorization
model; extension relations cannot duplicate existing relations.
[Modular-model guide](https://openfga.dev/docs/modeling/modular-models).
Custom roles can be objects with assignees, connected through tuples to permissions;
they need not be new model relations for every customer-defined role.
[Custom-role guide](https://openfga.dev/docs/modeling/custom-roles).
**Assessment:** modular authoring aids ownership/review, but is not runtime isolation or
independent deployment of each module's model. Module permission names, role metadata,
Organization/scope binding, grant authority and baseline/customization rules remain
consumer policy. An administrator's assignment power can itself grant sensitive access
unless the product bounds it. Neither engine prevents that product-level escalation alone.

**Versions.** Authorization models are immutable. Supplying `authorization_model_id`
pins evaluation/validation; omitting it uses the latest model. Some changes also require
tuple and application migration.
[Immutable-model guidance](https://openfga.dev/docs/getting-started/immutable-models).
**Assessment:** pin model IDs per rollout and coordinate module compatibility and tuple
migrations. A pinned model is not a snapshot/version of mutable tuples or business data.
Editable role contents still require their own revision and rollout semantics.

**Consistency.** `MINIMIZE_LATENCY` may use caches; `HIGHER_CONSISTENCY` bypasses caches.
The guide describes consistency tokens like Zanzibar's zookies as future work.
[Consistency guide](https://openfga.dev/docs/interacting/consistency).
The released PostgreSQL adapter sends higher-consistency reads to the primary and other
reads to a configured secondary:
[routing, lines 300–311](https://github.com/openfga/openfga/blob/v1.22.0/pkg/storage/postgres/postgres.go#L300-L311).
**Assessment:** do not generalize the guide's cache-disabled consistency claim to a lagging
secondary. Specify routing/cache configuration and stale-access bounds. Higher consistency
does not atomically synchronize Access/business EF work with a separate OpenFGA request,
freeze relationships across an operation, or close the check-to-commit race.

**Writes and failures.** One tuple write request is transactional within OpenFGA; duplicate
writes/missing deletes error by default, with explicit ignore options in the
[pinned API](https://github.com/openfga/api/blob/f153694bfc20/openfga/v1/openfga_service.proto#L136-L158).
The PostgreSQL adapter opens its own transaction:
[write, lines 544–604](https://github.com/openfga/openfga/blob/v1.22.0/pkg/storage/postgres/postgres.go#L544-L604).
The SDK offers transaction mode and an explicitly disabled/chunked mode, retries 429/5xx,
and supports both unary and streamed ListObjects.
[SDK v0.10.4 usage](https://github.com/openfga/dotnet-sdk/blob/v0.10.4/README.md).
**Assessment:** API-level no-op flags do not supply expected-revision/CAS semantics for
opposing administrative decisions or atomicity with consumer audit. A timeout after a
write is an ambiguous result, requiring reconciliation before a conflicting retry.

## ListObjects: exact release evidence and documentation drift

| Fact in the pinned release | Evidence and consequence |
| --- | --- |
| Unary request has no page size/continuation token; response contains only objects | [API lines 935–987](https://github.com/openfga/api/blob/f153694bfc20/openfga/v1/openfga_service.proto#L935-L987). There is no exposed completeness flag. `Read` tuple pagination does not evaluate the authorization graph and cannot substitute for this API. |
| Defaults are 3 seconds and 1,000 results | [Engine config lines 26–27](https://github.com/openfga/openfga/blob/v1.22.0/pkg/server/config/config.go#L26-L27). They are server configuration, not caller pagination. |
| Unary results are bounded and unordered | [API description lines 882–904](https://github.com/openfga/api/blob/f153694bfc20/openfga/v1/openfga_service.proto#L882-L904). Repeated calls are not a resumable traversal. |
| Unary pipeline can return accumulated objects after its internal deadline | [Engine lines 583–607](https://github.com/openfga/openfga/blob/v1.22.0/pkg/server/commands/list_objects.go#L583-L607): cancellation/deadline errors are suppressed before returning the response. The fallback also suppresses them [at lines 451–458](https://github.com/openfga/openfga/blob/v1.22.0/pkg/server/commands/list_objects.go#L451-L458), then returns accumulated objects [at lines 622–646](https://github.com/openfga/openfga/blob/v1.22.0/pkg/server/commands/list_objects.go#L622-L646). |
| Streaming avoids the configured unary count cap, still has an internal deadline | [API lines 867–879](https://github.com/openfga/api/blob/f153694bfc20/openfga/v1/openfga_service.proto#L867-L879), [engine initialization lines 648–658](https://github.com/openfga/openfga/blob/v1.22.0/pkg/server/commands/list_objects.go#L648-L658). The streamed pipeline suppresses cancellation/deadline errors [at lines 763–797](https://github.com/openfga/openfga/blob/v1.22.0/pkg/server/commands/list_objects.go#L763-L797); fallback reuses the suppression above and returns normally [at lines 804–830](https://github.com/openfga/openfga/blob/v1.22.0/pkg/server/commands/list_objects.go#L804-L830). |

**Assessment:** success, fewer than 1,000 objects, or normal stream termination does not
prove all accessible objects were enumerated. A shorter client timeout detects some
interrupted requests; a longer one cannot detect successful truncation at the engine's
earlier deadline. Removing/raising the deadline or cap changes operational exposure, not
the lack of a completion signal or a stable snapshot. Transport/error handling must be
proven separately; no timeout measurement or SDK execution was performed here.

The live [search guide](https://openfga.dev/docs/interacting/search-with-permissions)
describes `GET /list-objects` and traversing multiple pages. The released API above uses
**POST** and exposes no continuation token. The SDK README's “No Pagination Limit” wording
for streaming refers to the unary result cap; it does not create resumable pagination.
The guide's useful comparison remains: search then check, change-fed local index then
check, or accessible IDs then database search. **Assessment:** full authorized pages need
an ordered candidate scan until enough allowed rows or verified exhaustion; exact counts
need exhaustive evaluation under a stated consistency boundary. An index recheck can
remove stale allows, but cannot recover newly granted rows absent from a stale index.
Exports and AI source selection share these completeness and disclosure obligations.

## Native facts and the focused relational baseline

**Fact:** ASP.NET resource authorization is imperative through
`IAuthorizationService.AuthorizeAsync(principal, resource, policy/requirements)`;
attributes alone run before resource loading and do not establish resource permission.
[ASP.NET Core 10 guide](https://learn.microsoft.com/en-us/aspnet/core/security/authorization/resource-based?view=aspnetcore-10.0).
**Assessment:** call it after existing admission for detail/write paths and keep the
owning business capability's check for direct module/worker callers. Authorization handlers
do not automatically translate into SQL predicates or make all modules depend on Access.

**Fact:** EF concurrency tokens compare original versions in update/delete predicates;
zero affected rows raise `DbUpdateConcurrencyException`. Application-managed tokens are
supported, and conflict handling remains application work.
[EF concurrency guide](https://learn.microsoft.com/en-us/ef/core/saving/concurrency).
Transactions make changes atomic, and native explicit transactions can span multiple
SaveChanges calls; sharing between contexts requires both the connection and transaction.
[EF transaction guide](https://learn.microsoft.com/en-us/ef/core/saving/transactions).
**Assessment:** membership status plus accepted-change audit can compose locally without
an authorization engine; its expected revision must protect against status changing away
and back. A target-row token alone does not protect a concurrently revoked administrator
grant or a last-admin invariant. Select and prove those policies separately.

**Fact:** deterministic pagination needs a fully unique ordering; keyset paging avoids
offset movement and supports forward/backward traversal.
[EF pagination guide](https://learn.microsoft.com/en-us/ef/core/querying/pagination).
PostgreSQL Read Committed gives statement snapshots, so successive statements can differ;
Repeatable Read shares a transaction snapshot, while Serializable adds anomaly detection
and applications must handle retries.
[PostgreSQL 18 isolation](https://www.postgresql.org/docs/18/transaction-iso.html).
**Assessment:** module-owned relational permission predicates can filter before pagination,
counting and export selection; use EXISTS/union semantics to avoid duplicate business rows.
Counts and page rows need one statement or an explicitly shared snapshot if reported as
one consistent result. A transaction alone does not imply latest-at-commit authorization.

## Recommendation and proof boundary

**Recommendation:** native resource handlers plus focused module-owned relational queries
are the smaller initial fit. Access owns stable identity, admission and Access administration;
business modules define permissions, direct business grants and current responsibility-derived
authorization. Cross-module configurable bundles need an explicit bounded, complete authorized
fact/read contract first; no arbitrary joins into private module DbContexts are assumed.
If that contract cannot be complete, fail the operation or select a separately owned
projection with deletion, reconciliation, freshness and failure rules. OpenFGA becomes useful
when an actual graph/query workload outweighs those integration and operational obligations.
This is an assessment, not comparative latency/load evidence.

The smallest follow-ups are one native membership administration journey and one business
query proof covering sparse visibility, stable pages, counts and private export/AI selection.
If OpenFGA is reconsidered, use the same model/scenario matrix and prove the pinned engine's
truncation, cache/replica revocation and cross-store failure behavior before adoption. A2
can compose tenant isolation beneath these permissions without Tenancy depending on Access
or OpenFGA. No RLS implementation follows from these sources.

Identity/grant authority, role editing, permission derivation, disclosure/error policy,
snapshot/revocation timing and worker mandates are consumer policy. Registration/order,
module predicate examples and fail-closed failure translation are editable template material.
Native authorization/EF/audit composition supplies candidate mechanisms; **no new reusable
Gloaming mechanism was proven**. No runtime tests were run for this evidence note.
