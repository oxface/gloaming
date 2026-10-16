# A2.1: Sales resource permissions and complete queries

Implementation selected by the owner on 2026-10-16 after the
[A2 assessment](tenant-authorization-assessment.md). The selected
[bounded scope](../plans/a2-1-sales-resource-permissions.md) is Sales resource permissions
and complete queries; optional PostgreSQL RLS remains separate. Prepared on
`feat/sales-resource-authorization` from fetched main `d35b3ee`, using the existing checkout
and preserving the earlier unstaged assessment. No commit/push is part of this handoff.

## Outcome and consumer policy

Sales now owns positive Read/Edit grants for one Organization/draft and a canonical subject
kind/key. Edit includes Read. A native correlated EXISTS intersects these grants with tenant
and business predicates before detail loading, counts, totals and paging. Overlapping grants
do not duplicate resources. Existing-resource edits and submission select their targets under
Edit permission inside the actual module entry points; a host's earlier authorization boolean
does not authorize a bypass. Missing/inaccessible targets share NotFound.

Creation remains available to a trusted admitted human and now stages that creator's Edit
grant with the new draft in the same native Sales transaction. Changed remains an unsaved
proposal; save/commit/rollback ownership stays with the caller. No role editor, grant setter
or grant-administration/audit API is introduced. Historical
rows receive no guessed creator or default grant during migration.

Human and system subjects with identical key text remain distinct. Systems require their
own explicit grants for reads; commands retain the existing human requirement. Initiator
attribution and Organization membership administration provide no draft permission.
Access remains the independent global identity/current-membership authority; Sales neither
joins its private tables nor adds an Access dependency. Hosts still authenticate identities,
admit fresh human operations and explicitly authorize system mandates.

HTTP adoption supplies JSON list/detail and antiforgery-protected versioned line edits.
Detail/edit invoke native `IAuthorizationService` after tenant establishment, followed by
the module's own grant enforcement. Typed results supply native API response metadata;
unauthenticated calls keep the native 401 challenge. Invalid query/edit input returns 400,
missing/inaccessible targets 404, and stale/submitted/over-limit edits 409. Database faults
propagate; dependency failure is not a complete empty result. No middleware is reordered.

The explicit disposable HTTP setup seeds the same draft ID in both Organizations: Alpha's
North draft is editable by Alpha and totals 40; Beta's South draft is editable by Beta and
totals 70. Alpha's active South membership supplies no access to Beta's draft. The existing
CQRS executable retains its output because its new drafts carry its creator grants.

## Proven boundaries and timing

The two new test files add **22 cases**: 13 CQRS/PostgreSQL cases and nine HTTP/PostgreSQL
cases. They assert independently expected resources, totals, versions and persisted state:

- Two same-tenant subjects see different resources; sparse grants filter before paging and
  exact counts. An overlapping Read/Edit grant does not duplicate a draft. Hidden detail,
  summary and child totals are absent, including tenant-colliding resource IDs.
- A read-only caller cannot edit or submit, through either HTTP or direct module calls.
  An editor can commit an edit, while the next stale expected version conflicts.
- A system subject with a human initiator inherits no authority; an explicit system read
  grant exposes only its permitted draft. System mutation remains excluded by the human gate.
- Revocation is read again without changing actor/tenant. A barrier changes a grant between
  count and page: ReadCommitted may return the old count with the new permitted page;
  RepeatableRead preserves one older snapshot. A fresh operation observes the revocation.
- Missing/anonymous actor context fails before SQL. An injected query timeout propagates
  through Npgsql's native exception wrapper, with no successful empty page or tracked work.
- Creator grant and draft commit/rollback together. A populated pre-grant draft with a valid
  line survives migration without a new grant. A native tenant-inclusive FK rejects a grant
  referring to an Alpha-only draft from Beta.
- Current membership admission still denies a suspended human despite retained resource
  grants; reactivation makes the retained grant eligible under current checks. Already-admitted
  resource work keeps its immutable context, while the next HTTP admission stops. A fresh
  human worker composition stops before publishing tenancy after failed admission.
- Native API challenges, antiforgery and invalid-input rejection remain active on the new
  HTTP routes. The earlier HTTP/profile/membership journeys remain passing regressions.

The existing rich-aggregate and domain-reaction migration cases now compare the complete
native migration history. The latter verifies retained historical draft state through a
native untracked read, while explicitly asserting the authorized Contract returns no draft
without a grant. This preserves its original storage/allowance/event expectations and adds
the deliberate default-deny migration outcome rather than silently provisioning access.

Resource authority follows the database statement/transaction snapshot. No query caches a
permission decision or materializes all permitted IDs. Immutable contexts are not revoked
or rebound when a membership/grant changes. A command checked before revocation can still
commit afterward; no permission-at-commit fence, cross-store revocation or automatic retry
is claimed. Lines and customer display fields in these responses share draft-read permission;
separately protected fields require an independently selected rule and proof.

## Fresh verification

Executed 2026-10-16 using .NET SDK 10.0.401, EF/ASP.NET 10.0.12, Npgsql 10.0.3,
PostgreSQL 18.6 Testcontainers and rootless Podman with resource reaping enabled.
Commands follow the [development guide](../development.md#sales-resource-permissions).

| Check | Fresh result |
| --- | --- |
| Active solution build | Passed, zero warnings/errors. |
| CQRS/PostgreSQL | 33 passed, zero failed/skipped, including 13 new cases. |
| HTTP/PostgreSQL | 147 passed, zero failed/skipped, including nine new cases. |
| Rich relational aggregate | 40 passed, zero failed/skipped. |
| Local domain reactions | 30 passed, zero failed/skipped. |
| Architecture | 90 passed, zero failed/skipped; Contracts/module/library boundaries preserved. |
| Native style and analyzers | Both active-solution checks passed. |
| CSharpier | Passed with `--include-generated`, 769 files checked. |
| Frozen archive | All 800 original files match the retained manifest. |
| Documentation and review snapshot | 547 local links/anchors across 15 changed Markdown files resolve; whitespace checks pass. Existing assessment work is retained and the index matches the pre-implementation snapshot. |

The first run exposed the GET API-metadata/challenge issue and an error test incorrectly
expecting an unwrapped timeout. Typed native results and the assertion of Npgsql's actual
wrapper resolved them. Generated migration syntax was brought into the repository's
file-scoped namespace/collection-expression conventions. The populated legacy-draft proof
was strengthened and its CQRS suite rerun successfully. A2's 54 adapter tests and A1.1's earlier
results remain separately dated evidence; the table above is this implementation's result.

## Review-worthy files

| File | Review concern |
| --- | --- |
| [Consumer authorization Contract](../../samples/Wholesale/modules/Sales/Sales.Contracts/IDraftOrderAuthorization.cs) | Narrow action check, stable enum values and no transport/persistence/context types. |
| [Permission predicate](../../samples/Wholesale/modules/Sales/Sales/DraftOrders/DraftOrderAuthorization.cs) | Exact subject kind/key, selected Organization, positive Read/Edit semantics and SQL EXISTS. |
| [Queries](../../samples/Wholesale/modules/Sales/Sales/DraftOrders/DraftOrderQueries.cs), [commands](../../samples/Wholesale/modules/Sales/Sales/DraftOrders/DraftOrderCommands.cs) | Actual entry enforcement, selection before shaping/count/page, creator-grant staging and retained native save ownership. |
| [Grant mapping](../../samples/Wholesale/modules/Sales/Sales/DraftOrders/DraftOrderGrantRow.cs), [migration](../../samples/Wholesale/modules/Sales/Sales/Migrations/20261016084346_AddDraftOrderGrants.cs) | Scoped composite identity/FK, enum/subject checks, subject lookup index and empty default grant population. |
| [HTTP endpoints](../../samples/Wholesale/HttpIdentityDemo/Endpoints/DraftOrderEndpoints.cs), [native handler](../../samples/Wholesale/HttpIdentityDemo/HttpIntegration/DraftOrderAuthorizationHandler.cs), [write operation](../../samples/Wholesale/HttpIdentityDemo/HttpIntegration/DraftOrderEdits.cs) | Late native check, typed API metadata, concealment, antiforgery, cancellation and native transaction/conflict behavior. |
| [Query proofs](../../samples/Wholesale/CqrsDemo.Tests/QueryTests.Authorization.cs), [HTTP proofs](../../samples/Wholesale/HttpIdentityDemo.Tests/DraftOrderTests.cs) | Independent visibility/state expectations, direct/system paths, migration, grant snapshots and admission timing. |
| [Sales guide](../../samples/Wholesale/modules/Sales/README.md), [HTTP usage](../../samples/Wholesale/HttpIdentityDemo/README.md#private-draft-permissions) | Consumer setup, supported policy, migration/backfill and deferred capabilities. |

## Extraction findings and remaining gaps

**No new reusable mechanism was proven or added.** Existing immutable contexts, native ASP.NET
resource handlers, EF relational predicates/transactions and consumer-owned Contracts suffice.
No runtime library source/interface/dependency changed. Simple repeated setup can still earn
later extraction when actual independent adoption demonstrates useful work removed.

**Consumer policy:** Read/Edit semantics, human creation/creator access, system read mandates,
concealment and grant freshness belong to Sales/Umbrine. Grants are provisioned/revoked only
by trusted finite demo/test setup in this slice. A production grant lifecycle, authorization
to delegate access, accepted grant-change audit, backfill and administrator discovery remain
unimplemented. Direct SQL, raw/bulk writes and arbitrary in-process code can bypass Contracts.

**Template:** explicit module registration, late native handlers, permission SQL and native
transaction ownership are demonstrated candidates. T1 output and generation are unchanged;
there is no new preset or runtime framework. **Sample/product:** the named draft list/detail/
edit paths are protected. This does not authorize every other Sales/Inventory capability or
create a Personnel/Projects model, configurable role/group engine, export/AI selection or
grant-at-commit protocol. Snapshot changes across count/page or later HTTP pages remain explicit.

Optional RLS, restricted production credentials, pooled transaction context, retries and
transport-table isolation remain the A2 follow-ups. No database tenant-isolation, production
query-performance, browser-provider deployment or whole-application authorization guarantee
is inferred from these consumer proofs. The earlier assessment and its primary-source note
are retained; all implementation edits remain unstaged and the existing index is preserved.
