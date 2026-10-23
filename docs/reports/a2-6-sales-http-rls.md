# A2.6: Sales HTTP row security adoption

Implemented for owner review on 2026-10-16 after the owner approved continuation on
`feat/sales-postgres-rls`, starting from `635cb5b`. The [scope](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/a2-6-sales-http-rls.md)
and [executable consumer guide](../../samples/Wholesale/Api/README.md#optional-sales-http-row-security)
describe the bounded optional composition. All new changes remain unstaged.

## Outcome

`ConnectionStrings:Sales` selects `AddSalesDatabase(connection, useRowSecurity: true)`.
The module registers the existing capability and `.UseRls<SalesDbContext>(provider)` once.
A stateless native EF model customizer selects six already-owned tables with `.WithRls`;
it captures no operation services and leaves relational columns/snapshots unchanged. Ordinary
Sales options retain their separate cached model and existing behavior.

The same admitted immutable tenant now reaches PostgreSQL automatically when an endpoint or
existing mutation owner begins its native transaction. Reads and resource prechecks use a
small host-local `SalesReadTransaction` bridge: endpoints own/dispose the returned native EF
transaction while calling module Contracts. This preserves the existing architecture rule
against direct module-implementation dependencies in endpoint classes. No middleware reordering,
new operation scope, central transaction owner or generic execution interface is introduced.

Profile changes retain their module-owned transaction and two saves, including accepted audit.
Draft edits retain the host's existing save/commit owner. Their earlier native Edit precheck
uses a short read transaction; the command repeats the module-owned permission predicate in
the mutation transaction. This retains existing statement-snapshot authorization semantics,
not permission-at-commit fencing. Count/page still use separate ReadCommitted statements.

## Database protection and precise limits

| Boundary | Implemented protection | Limitation / consumer ownership |
| --- | --- | --- |
| Four profile/draft root/child tables | ENABLE/FORCE, exact tenant equality USING/WITH CHECK; runtime SELECT/INSERT/UPDATE/DELETE. | Aggregate rules, version checks, composite FKs and audit remain required. |
| Draft grants | Tenant equality and runtime SELECT only. | This lane edits existing drafts; no grant administration or draft creation/submission adoption. |
| Profile audit | Tenant equality, SELECT/INSERT only; accepted audit shares the profile transaction. | Audit classification, completeness and query authorization remain Sales policy. |
| Missing SQL tenant predicate | PostgreSQL restricts covered rows even for deliberately unfiltered raw SQL. | Same-tenant private rows remain visible to raw SQL; module permission predicates must govern output. |
| Foreign update/delete targets / foreign new owner or audit tenant | USING hides targets; WITH CHECK rejects new results. | A credential holder can forge `app.tenant_key`; tenant authentication is outside custom-GUC RLS. |
| Missing transaction/context or deliberate tenantless | Existing interceptor rejects protected EF commands/begin; failed setup publishes no EF transaction. | Native commands must not alter settings/transaction lifetime behind EF. |
| Physical pool | Max-size-one Alpha/Beta/Alpha requests verify backend identity and immediate empty baseline after transactions. | No DbContext pooling, external pooler or provider-failover guarantee. |
| Runtime credentials | Sales login restricted to six tables; Access/Inventory login has no Sales table authority. Startup rejects superuser/bypass/owner/membership and forbidden table rights. | Role creation/passwords, views/functions, other secrets and later administrator changes remain deployment obligations. |
| Policy catalog | Startup requires ENABLE/FORCE, exactly one expected all-command/public policy with both clauses and required table privileges. | Checks policy shape/count, not expression-body equivalence or arbitrary definer/view escapes. Review explicit deployed SQL. Readiness does not continually re-attest it. |
| Access bootstrap and public catalog | Separate restricted global lookup/catalog credential; existing native admission order preserved. | Access/Inventory receive no new RLS policy in this increment. |
| Inbox/outbox/workflows/other Sales tables | Sales HTTP runtime has no table privileges. | Worker transport bootstrap needs its own role/transaction composition and proofs. |

The adapter now accepts unconverted PostgreSQL `character varying`/`varchar` as well as `text`,
using the resolved native mapping base name. Blank-padded `character` remains rejected.
The existing policy's `COLLATE "C"` comparison preserves case and trailing-space distinctions.
[PostgreSQL's character-type reference](https://www.postgresql.org/docs/18/datatype-character.html)
defines the significant trailing spaces for varchar/text and the different character semantics.
The native [EF model-customizer contract](https://learn.microsoft.com/en-us/dotnet/api/microsoft.entityframeworkcore.infrastructure.relationalmodelcustomizer?view=efcore-10.0)
requires a stateless/thread-safe singleton. Pinned .NET inspection verified its constructor
and EF 10.0.12 `RelationalTypeMapping.StoreTypeNameBase`; the implementation uses those APIs.

## Provisioning and extraction findings

After ordinary migrations/seed under the table owner, independently provision two restricted
runtime logins. The owner grants Access/Inventory rights explicitly. The finite
`--initialize-sales-rls` command uses `ConnectionStrings:SalesMigrations` to apply the existing
explicit migration-helper operations and finite Sales grants atomically, then checks both
runtime credentials. It exits without OIDC/HTTP/exporters. The normal host never migrates,
creates roles or installs policies. This is one-shot fresh-database setup, not reconciliation,
automatic policy scaffolding or another EF migrations history. Do not retain owner credentials
in HTTP configuration or combine it with the older CQRS demonstration's policy inventory.

**Library:** the proven mechanism remains A2.5's native transaction/command capability. Only
its supported unconverted string storage mapping is broadened; no new reusable mechanism,
public Gloaming interface or mandatory Access/FGA dependency was proven or introduced.
**Sample:** finite model selection, credentials/grants, startup checks and transaction bridge
are consumer-owned composition/policy. **Template:** no preset or generated file changes.
Default single-connection HTTP mode remains supported and also uses the explicit read
transactions, without enabling database RLS. AppHost provisioning and transport workers
retain their existing behavior. These are separate from selecting RLS for all Sales
profile/draft requests in the optional mode.

## Verification

Fresh verification from this checkout, after retaining the endpoint/Contracts architecture rule:

| Suite / check | Result |
| --- | --- |
| HTTP/PostgreSQL composition | **161 passed**, zero skipped, including 14 new RLS cases and the finite setup child process. |
| Independent RLS/declaration adapter | **31 passed**, zero skipped, including the new fixed-width rejection before opening a connection. |
| Existing Wholesale CQRS / explicit RLS composition | **62 passed**, zero skipped. |
| Architecture | **96 passed**, zero skipped; boundary assertions unchanged. |
| Existing ownership/model | **23 passed**, zero skipped. |
| Full solution build | Zero warnings/errors. Restore used the per-invocation `-p:NuGetAudit=false` workaround for the unrelated MessagePack advisory already recorded in A2.4; no dependency/audit configuration change. Final source rebuilt with `--no-restore`. |
| Full active style/analyzers | Both passed without modifying files. |
| CSharpier / frozen archive | 794 files checked; all 800 original archived files preserved. |
| Changed documentation / whitespace | 443 local links/anchors across nine Markdown documents resolved; changed-file whitespace checks passed. |

**373 native tests passed**, without counting reruns twice. PostgreSQL 18.6 proofs use the
actual restricted Sales login and a separate restricted Access/Inventory login; migration/admin
credentials remain only in the disposable fixture. Alpha/Beta/Alpha checks observe the same
backend, raw SQL lacking tenant predicates and an empty setting after transaction disposal.
Case/trailing-space variants remain hidden in the actual varchar columns. Database rejection
and target hiding are verified independently of tracked-write validation. Normal HTTP edit
responses, underlying foreign rows and accepted audit contents provide separate expectations.

The complete HTTP suite covers both ordinary and RLS-selected cached models, native challenge,
public catalog access, immutable admitted work after suspension and fresh-request denial.
The new audit-fault proof rejects the second save, checks profile/audit rollback and verifies a
fresh request succeeds after removing the injected constraint. Catalog cases reject disabled/
extra policies, transport table privileges, a missing audit INSERT grant and a privileged
alternate Access credential. These are bounded test cases, not a full deployment audit.

Earlier A2.2–A2.5 counts, browser/AppHost and template proof results remain historical and are
not counted as new A2.6 results. Shared template sources were not modified. The index matches
its pre-edit snapshot, and all 24 changed/new files remain unstaged.

## Review-worthy files and remaining work

- [Sales registration](../../samples/Wholesale/modules/Sales/Sales/SalesDatabase.cs) and
  [finite model selection](../../samples/Wholesale/modules/Sales/Sales/SalesRowSecurityModelCustomizer.cs).
- [HTTP selection](../../samples/Wholesale/Api/DemoComposition.cs),
  [transaction bridge](../../samples/Wholesale/Api/HttpIntegration/SalesReadTransaction.cs),
  [draft endpoints](../../samples/Wholesale/Api/Endpoints/DraftOrderEndpoints.cs) and
  [profile endpoint](../../samples/Wholesale/Api/Endpoints/CustomerProfileEndpoints.cs).
- [Finite installer/catalog checks](../../samples/Wholesale/Api/SalesHttpRowSecurity.cs)
  and [program modes](../../samples/Wholesale/Api/Program.cs).
- [HTTP proofs](../../samples/Wholesale/IntegrationTests/Api/RowSecurityTests.cs),
  [restricted-role fixture](../../samples/Wholesale/IntegrationTests/Api/RowSecurityTests.Support.cs),
  [mapping validation](../../src/Gloaming.Persistence/Gloaming.Persistence.EntityFrameworkCore.Postgres/TenantRowSecurityExtensions.cs)
  and [fixed-width rejection](../../src/Gloaming.Persistence/tests/PostgresTests/DeclarationTests.cs).

The remaining follow-ups are default AppHost rollout with restricted provisioning, worker/
transport role separation, automatic policy scaffolding, creation/submission/grant-administration
adoption, and real external pooler/failover proofs. No production deployment or new browser/OIDC
journey is claimed. Changes remain unstaged; no commit or push is authorized by this handoff.
