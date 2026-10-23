# Admitted Sales reservation replies under RLS

The optional `process-sales-replies` role processes retained WF2 reservation outcomes using
the existing PostgreSQL inbox processor and worker. Each process serves one explicitly
admitted Organization (`wholesale-alpha` or `wholesale-beta`); replicas may compete for that
Organization. Every attempt gets its own immutable tenant and `sales.reservation-worker`
system actor through scoped DI factories. This finite policy supplies no dynamic tenant
discovery, human membership lookup or initiator impersonation.

The existing processor begins its native transaction, and the existing `.UseRls()` interceptor
automatically establishes `app.tenant_key` before its raw claim SQL runs. RLS restricts that
claim to the admitted tenant. The module's admitted entry validates the reply and requires the
matching established tenant/system actor; it does not initialize them again. Workflow effects,
the next outgoing command and inbox completion retain one save/commit boundary. Failure rolls
local work back to a savepoint established after delivery locking. It records failure metadata
under the retained lock and admitted tenant, then commits only that accounting. The default policy
defers work; this host registers no stop policy or operator recovery. It discards the scope before
each next attempt. See [inbox recovery](../../../../src/Gloaming.Messaging/docs/inbox-recovery.md).

## Explicit setup and execution

M2 adds native inbox recovery columns in the Sales forward migration. Apply it before deploying
the updated processor, with old processors stopped. Existing restricted reply roles also need
UPDATE on `revision`, `failure_count`, `failures_since_redrive`, `last_failed_at`,
`last_failure_code` and `quarantined_at`; redrive count/time rights are not granted. Fresh finite
setup includes these columns and startup validates the exact grant inventory. Updating an already
provisioned role remains an explicit owner action; setup is not a grant reconciler.

The outbox M2 upgrade also requires SELECT on generated insertion defaults `revision`,
`failure_count`, `failures_since_redrive` and `redrive_count`, alongside the existing
queued/available timestamps. EF reads these through INSERT RETURNING. Fresh provisioning
and effective-grant checks include the exact inventory. Apply an explicitly reviewed
grant update to existing reply roles before upgrading; this adds no outbox payload SELECT
or lifecycle UPDATE authority. See [outbox rollout](../../../../src/Gloaming.Messaging/docs/outbox-recovery.md).

First apply the existing [module/queue setup](README.md#run-the-executable). The new finite
setup mode operates on those existing tables and needs no broker or Inventory connection:

```sh
ConnectionStrings__SalesRepliesSetup='Host=localhost;Database=sales;Username=postgres;Password=local-owner' \
ConnectionStrings__SalesReplies='Host=localhost;Database=sales;Username=wholesale_sales_replies;Password=local-replies' \
  dotnet run --project samples/Wholesale/Worker -- --role setup-sales-replies
```

The table owner needs role-creation authority. One native transaction creates a new LOGIN,
installs reviewed policies and grants, then commits. A separate actual-runtime connection
verifies the finite role/policy inventory afterward. A post-commit verification failure can
leave that setup installed. Existing roles/policies cause failure rather than reconciliation;
there is no migration, password rotation, upgrade, backfill or automatic repair protocol.

Start each worker with its runtime connection and admitted Organization:

```sh
ConnectionStrings__SalesReplies='Host=localhost;Database=sales;Username=wholesale_sales_replies;Password=local-replies' \
Organization=wholesale-alpha \
  dotnet run --project samples/Wholesale/Worker -- --role process-sales-replies
```

Start a separate instance with `Organization=wholesale-beta` for Beta. Unknown, differently
cased or whitespace-modified Organizations reject before database connections. Remove inherited
`ConnectionStrings__SalesRepliesSetup`, `SalesDispatchSetup`, `SalesDispatch`, `Sales`, `Inventory`,
`Access`, `Setup`, `SalesMigrations`, `RabbitMq` and `Parameters__postgres-password` values from
the worker environment. Nonempty values reject before any connection. These known-key checks
are not a sandbox against arbitrary secrets. The processor needs no broker configuration and
never creates tables, queues or runtime roles.

Broker intake and starting new workflows remain separate authorities. The real-broker proof
retains an Inventory reply through the existing receiver, then processes it in this restricted
child process. It does not provide a new restricted broker intake host or production broker ACLs.
The ordinary combined `run` role retains its wiring; it is not the selected RLS processor.

## Selected persistence and permissions

The host calls `AddSalesReservationReplyProcessing(runtime, organization)` once. It registers
scoped established contexts, `AddSalesReservationReplyDatabase` and `AddAdmittedStockReservationReplies`.
Program then adds the existing inbox worker. There is no per-message manual database-context call,
new transaction owner, adopted transaction or Gloaming interface.

Native EF `ConfigureDbContext` selects a stateless model customizer after ordinary capability
registration. It adds `HasTenantOwnership(...).WithRls("app.tenant_key")` to workflow rows and
selects the inbox's existing ownership mapping. Existing query filters, save guards and workflow
concurrency remain. This model is separate from the HTTP six-table profile and ordinary Sales.
The workflow table's physical nullable `TenantKey` column is not migrated: tenantless stored
rows are denied by the policies, and this selected model requires a tenant.

| Target | Runtime authority | Protection / limitation |
| --- | --- | --- |
| `workflow_instances` | SELECT and UPDATE. | ENABLE/FORCE with tenant equality in USING/WITH CHECK; raw/bulk/filter bypass stays tenant-scoped. No INSERT/DELETE or within-tenant workflow-type/field permission policy. |
| `inbox_messages` | SELECT; UPDATE completion, availability, revision, failure counts/code/time and dead-letter state. | ENABLE/FORCE restricts selection, completion and accounting to the admitted tenant. No intake INSERT, redrive count/time UPDATE, envelope mutation or DELETE. Same-tenant subscriptions are not a separate database permission boundary. |
| `outbox_messages` append | INSERT; SELECT generated timestamps and initial recovery counters. | ENABLE/FORCE with tenant equality specifically for INSERT WITH CHECK. Missing/foreign context rejects native writes. Tracked outgoing ownership/save validation remains. The six generated-default columns are readable across queued tenants; payloads are not. |
| Outbox delivery | Processor has no lifecycle UPDATE or payload SELECT. | Read and delivery UPDATE policies remain global. A2.8's separately granted dispatcher scans every queued tenant, with no INSERT rights and no RLS bypass flag. |
| Other application relations | No table/column/sequence access. | Startup checks effective grants and ownership, including PUBLIC and role memberships. Other databases, routines/SECURITY DEFINER paths, extension/trigger bodies and later privilege changes need separate review. |
| Operation identity | Trusted host configuration establishes tenant and receiver-owned system actor once per scope. | Envelope checks and original continuation/correlation rules remain consumer authorization. RLS does not authenticate tenants or recheck an initiating human's current membership. A credential holder can forge the mutable GUC. |

The command-specific [outbox policies](SalesOutboxPolicies.cs) preserve
[restricted global dispatch](sales-outbox-dispatch.md) while protecting new writes. Provisioning
either runtime login first is supported on fresh migrated storage; selected current binaries
recognize the reviewed queue policies. Unrecognized command/policy inventories reject. The startup
checks validate role/flags/command inventory and clause presence, including global read/delivery
expressions; they do not certify arbitrary replacements of tenant predicates or every SQL path.
Owners must review the deployed tenant expressions. Native
[PostgreSQL row security](https://www.postgresql.org/docs/18/ddl-rowsecurity.html),
[locking SELECT](https://www.postgresql.org/docs/18/sql-select.html#SQL-FOR-UPDATE-SHARE) and
[column grants](https://www.postgresql.org/docs/18/sql-grant.html) supply these boundaries.

## Evidence and remaining scope

Run the existing WorkflowDemo test project. New tests cover both tenants, retained real RabbitMQ
replies, the restricted dispatcher/processor together, exact context matching, raw/bulk and native
outbox write checks, failure of every atomic participant, competing processes and in-flight SIGTERM/
hard-death recovery. Transaction-local context is checked after commit and rollback while the same
connection remains open. The [report](https://github.com/oxface/modulith-foundry/blob/bd2adb7dea58de304aa2eb3df3c587c6fe348320/docs/reports/a2-9-sales-inbox-rls.md) records fresh totals.

This is an optional WF2 adoption. Other module/workflow/table coverage, tenant scheduling, production
role lifecycle, restricted broker intake, external poolers/failover, default rollout and templates
remain separately selectable. **No new reusable library mechanism was introduced.** Existing
transaction interception, persistence/messaging capabilities and native DI/EF/PostgreSQL suffice.

## Receipt generation and optional retention

Apply the forward AddInboxReceiptGeneration migration with deployment authority before the
updated binary. Existing full inbox SELECT covers receipt_id; a column-restricted inspection
or operator role must add that column explicitly. Retry requires the observed ReceiptId and
revision. The processor role still has no redrive or DELETE authority, and restricted outbox
dispatch grants are unchanged.

No automatic Sales cleanup is enabled. A consumer choosing to adopt
[processed-inbox cleanup](../../../../src/Gloaming.Messaging/docs/inbox-retention.md) separately
admits the exact subscription/producer, evidence cutoff and native maintenance credentials/
RLS policy. That policy must deliberately cover SELECT and DELETE of admitted processed work;
existing processor policies do not imply DELETE. Unprocessed/dead-lettered replies remain.
Deletion permits old replies to be received again; workflow/business operation identities
must supply any longer-lived idempotency required by the consumer.
