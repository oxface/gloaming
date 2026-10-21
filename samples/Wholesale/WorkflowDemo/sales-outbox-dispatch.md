# Restricted Sales outbox dispatch

`WorkflowDemo` includes an optional `dispatch-sales` role. It runs the existing native
PostgreSQL outbox worker and RabbitMQ Sales command publisher with a separate database
login. Its [context](SalesOutboxDbContext.cs) maps only `sales.outbox_messages`; it registers
no business context, tenant accessor, inbox processing or deadline worker. The ordinary
combined `run` role remains available and retains its existing credential composition.

This is the delivery boundary around tenant-scoped business operations. Dispatch needs to
read committed messages for different tenants, so the queue retains global read/delivery authority.
This context does not install `.UseRls()` or a synthetic tenantless business scope. The dispatch login
has no BYPASSRLS, ownership or access to the RLS-protected Sales business tables. Business
operations still establish their own immutable actor/tenant context and own their commits.
The separate [HTTP composition](../HttpIdentityDemo/README.md) covers six business tables;
this dispatch role does not extend those policies to all workflow business state.

## Setup and run

Use the existing [explicit workflow setup](README.md#run-the-executable) first, or an existing
Sales database migrated by its owner. Broker queues must already exist before dispatch starts.
The new finite setup role neither migrates nor seeds and needs no broker connection:

```sh
ConnectionStrings__SalesDispatchSetup='Host=localhost;Database=sales;Username=postgres;Password=local-owner' \
ConnectionStrings__SalesDispatch='Host=localhost;Database=sales;Username=wholesale_sales_dispatch;Password=local-dispatch' \
  dotnet run --project samples/Wholesale/WorkflowDemo -- --role setup-sales-dispatch
```

Only this finite process receives `SalesDispatchSetup`, using the existing outbox table
owner with role-creation authority. It creates a new restricted LOGIN and grants access in
one transaction, commits, then verifies the actual runtime connection. Existing role names
cause failure; no password rotation, role reconciliation or migration is attempted. Failure
of the post-commit connection/verification can leave the new login/grants installed. Production
operators own role lifecycle, database CONNECT policy, credentials and deployment.

Start the worker in a separate environment containing its runtime and broker connections:

```sh
ConnectionStrings__SalesDispatch='Host=localhost;Database=sales;Username=wholesale_sales_dispatch;Password=local-dispatch' \
ConnectionStrings__RabbitMq='amqp://local:local@localhost:5672' \
QueuePrefix=wf1 \
  dotnet run --project samples/Wholesale/WorkflowDemo -- --role dispatch-sales
```

Remove inherited `ConnectionStrings__SalesDispatchSetup`, `Sales`, `Inventory`, `Access`,
`Setup`, `SalesMigrations` and `Parameters__postgres-password` values from this worker's
environment. Nonempty values are rejected before any connection opens. This finite list
guards known composition keys; it is not a sandbox against arbitrary unrecognized secrets.

Startup verifies the actual login, effective table/column grants and the existing outbox,
then passively checks the existing workflow queues. It creates no storage or queues. It accepts
the ordinary unprotected queue or the explicit command policies installed by the optional
[RLS reply processor](sales-inbox-rls.md): tenant-checked INSERT, global SELECT and delivery UPDATE.
Other RLS policy inventories reject; this role never disables policies. Its grants still deny
INSERT and limit UPDATE to delivery columns. The existing broker
adapter opens its normal channels and checks all five workflow queues; narrower broker ACLs
and a publisher-specific topology are separate work. Existing ServiceDefaults and optional
messaging/RabbitMQ telemetry subscriptions are retained.

## Database protection and limits

| Target | Allowed | Denied / limitation |
| --- | --- | --- |
| Outbox envelopes | SELECT all rows, including payloads for every queued tenant and retained completed messages. | No INSERT, DELETE or TRUNCATE. No UPDATE of IDs, owner, payload, route, schema version, attribution/trace fields or enqueue timestamp. |
| Delivery state | UPDATE `lease_token`, `lease_until`, `attempts`, `available_at`, `dispatched_at`, `revision`, `failure_count`, `failures_since_redrive`, `last_failed_at`, `last_failure_code`, `dead_lettered_at`. | No UPDATE on operator-only `redrive_count`/`last_redriven_at`. These privileges do not prove publication or enforce legal transitions. A compromised dispatcher can manipulate permitted delivery fields or publish readable payloads. |
| Other application relations | No effective table/column or sequence access in the connected database. | Startup checks current non-system relation grants, including PUBLIC and column grants. It does not certify every database, function, extension, SECURITY DEFINER path, trigger body or future privilege change. |
| Authority | Sales schema USAGE and the existing native delivery transactions. | No superuser, BYPASSRLS, role membership, database/schema creation, role/database administration, replication or grant options on the outbox. |
| Tenant scope | Queue delivery deliberately scans all tenants without an operation tenant context. | This is transport authority, not cross-tenant business access or within-tenant authorization. A forged tenant GUC does not confer missing table privileges. |
| Publication / recovery | Existing committed claim, confirmed mandatory publication, then separate conditional completion transaction. | At least once; death after publication can redeliver the same MessageId. Inbox deduplication remains the receiver's responsibility. |

Column privileges are native PostgreSQL grants. A table-level UPDATE grant would override
column restrictions; startup therefore checks both levels using the native
[privilege inquiry functions](https://www.postgresql.org/docs/18/functions-info.html#FUNCTIONS-INFO-ACCESS-TABLE).
[PostgreSQL GRANT](https://www.postgresql.org/docs/18/sql-grant.html) also explains PUBLIC,
ownership and grant options. RLS protects permitted row access and remains separate from
these object privileges; its owner/superuser/BYPASSRLS exceptions are described in
[PostgreSQL row security](https://www.postgresql.org/docs/18/ddl-rowsecurity.html).

The role is not passed to the HTTP API or the ordinary workflow `run` process. Native hosting
owns shutdown; each dispatch attempt still gets a fresh scope. Lease duration remains ten
seconds and failed publication delays availability by one second. Durable publisher-exception
accounting is now required. This role keeps its existing indefinite retry default and registers
no failure policy or operator store. Other consumers can independently select
[dead-letter policy and authorized inspection/retry](../../../src/Gloaming.Messaging/docs/outbox-recovery.md).
No lease renewal, exactly-once delivery, production provisioning or default rollout is added.

Before upgrading an existing installation, apply the Sales AddOutboxDeadLetters migration and
an explicitly reviewed grant update for the six new accounting columns listed above. Stop old
dispatchers before relying on dead-letter state; they ignore it. Finite provisioning still
rejects existing role names and startup performs no DDL. Its effective-grant check rejects
missing accounting privileges and extra operator-column privileges. Neither the dispatch login
nor its runtime environment gains producer or business credentials.

An unconfirmed failure-accounting write throws OutboxFailureAccountingException and faults
the optional worker without another poll. Native host supervision owns shutdown; repair grants/
storage before restarting. An accounting commit response may be lost after a real commit;
fresh storage determines the retained state. Completion failures remain ambiguous and are
never counted as publisher failures.

Run the existing workflow test project to exercise the new proofs as well as the retained
workflow journeys. The [slice report](../../../docs/reports/a2-8-sales-outbox-dispatch.md)
records fresh results and limitations. **No new reusable mechanism or template output is
introduced:** context selection, role grants and executable composition remain sample policy.
The [M2 outbox report](../../../docs/reports/m2-outbox-dead-letters.md) records the new library
accounting/retry proofs and restricted-credential follow-up separately from that A2.8 history.
