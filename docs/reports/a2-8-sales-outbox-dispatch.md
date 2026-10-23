# A2.8: restricted Sales outbox dispatch

Implemented for owner review on 2026-10-17 after the owner-approved A2.7 checkpoint `192fe7d`.
The owner selected the [bounded scope](https://github.com/oxface/modulith-foundry/blob/0d43d3cf746bc5b70dec60b8864459cf97c7d65d/docs/plans/a2-8-sales-outbox-dispatch.md): independent
Sales outbox dispatch using the existing PostgreSQL dispatcher, worker and RabbitMQ publisher.
All new changes remain unstaged on `feat/sales-postgres-rls`; no commit or push is part of this slice.

## Outcome

The workflow executable has two optional roles. `setup-sales-dispatch` creates a new restricted
login and grants against an already migrated outbox. `dispatch-sales` accepts only the dispatch
and broker connections, verifies effective database privileges, passively checks the existing
queues and runs the existing hosted worker with a context mapping only `sales.outbox_messages`.
Its [consumer guide](../../samples/Wholesale/Worker/Broker/sales-outbox-dispatch.md) contains
exact configuration, lifecycle and limitations.

The dispatch context deliberately has no business mappings, tenant accessor, query filter or
`.UseRls()`. Dispatch must discover messages belonging to different tenants. Its authority is
limited by native object/column privileges rather than a tenantless exception on protected
business state. The six existing Sales business policies remain enabled/forced in the new proofs;
this does not certify all workflow business tables under RLS. Existing immutable operation
contexts, module Contracts, producer commits and inbox effects are unchanged.

## Protection and limitation matrix

| Boundary | Protection exercised | Limitation |
| --- | --- | --- |
| Queued envelopes | SELECT across tenants; SQL and EF bulk updates of tenant/envelope fields reject with 42501. | Every queued payload and retained completed record is readable by the dispatcher. Broker access must be controlled separately. |
| Delivery state | UPDATE only lease token/expiry, attempts, availability and completion. | A credential holder can manipulate these fields without publishing. Native lease checks describe supported dispatcher behavior, not a defense against a malicious dispatcher. |
| Business relations | No effective access to other non-system application tables/views/sequences; direct Sales reads/deletes deny even after forging a tenant setting. | No claim about other databases, functions, extension code, SECURITY DEFINER paths, trigger bodies or grants changed after startup. |
| Database authority | Actual login must be non-owner/non-superuser/non-BYPASSRLS, with no role memberships, schema/database CREATE or role/database/replication administration. | Runtime checks are finite startup assertions, not continuous privilege monitoring or a general security audit. |
| Table and column grants | Whole-table UPDATE, extra envelope UPDATE, business column SELECT, PUBLIC access, missing delivery-column rights and grant options are rejected. | Future schema/role changes need explicit operator review and a restart/check; default privilege and routine policy remain deployment-owned. |
| Business/provisioning credentials | Seven known retained keys reject before database or broker connections open. | Unknown secret keys are not sandboxed. Process/container isolation and secret distribution remain deployment-owned. |
| Setup | Requires the existing outbox owner; role plus grants are one transaction. Existing roles fail rather than being altered. | Post-commit runtime verification can fail after provisioning committed. No production rotation, reconciliation, upgrade or automatic cleanup protocol. |
| Startup | No migration/seed/queue creation; empty storage and missing topology fail. An RLS-enabled outbox is rejected. | Existing broker adapter still checks all five workflow queues and uses its normal channels. Broker ACL refinement is separate. |
| Claims / recovery | Existing SKIP LOCKED claim, confirmed mandatory publication and separate conditional completion; fresh scopes and native process shutdown retained. | At least once. A crash after broker acceptance can cause redelivery; receiver deduplication remains necessary. |

Native [PostgreSQL GRANT](https://www.postgresql.org/docs/18/sql-grant.html) supports column grants
and explains that table grants and PUBLIC privileges still apply. Therefore the verifier checks
effective table and column privileges, including grant options, through the
[native privilege inquiry functions](https://www.postgresql.org/docs/18/functions-info.html#FUNCTIONS-INFO-ACCESS-TABLE).
The [row-security reference](https://www.postgresql.org/docs/18/ddl-rowsecurity.html) separately
documents RLS owner/superuser/BYPASSRLS behavior. No bypass is used to discover this delivery queue.

## Verification

Fresh checks from this checkout:

| Suite / check | Result |
| --- | --- |
| Complete workflow suite | **90 passed**, zero skipped, in 1m32s: 62 existing cases and 28 new dispatch/security/startup cases. |
| Architecture | **96 passed**, zero skipped; no boundary rules changed. |
| Full solution build | Passed with zero warnings/errors using `--no-restore`. No dependency/audit configuration changed; this is not a fresh audited restore. The historical MessagePack advisory/workaround state in A2.6 remains outside this slice. |
| Full active style / analyzers | Both verify-only checks passed without modifying files. |
| CSharpier / frozen archive | 802 files checked; all 800 original archived files preserved. |
| Documentation / whitespace | 384 local links/anchors across seven changed Markdown documents resolved; changed-file whitespace checks passed. |

**186 tests passed**, counting only the final suite runs. Native fixtures use PostgreSQL 18.6
and RabbitMQ 4.3.6; child processes launch the real workflow executable with the restricted
login and no business/provisioning connection. Both tenants' commands reach the real broker
and Inventory intake while Sales requests remain AwaitingReply and the six business policies
stay enabled/forced. Direct SQL and EF bulk envelope updates reject with 42501; a forged
tenant setting does not change business-table denial.

The native mandatory-publication failure returns NO_ROUTE/312, releases the claim for retry
and succeeds from a fresh scope with the same message. A trigger holds Alpha's claim inside
its transaction while a second process skips it and publishes Beta; both finish with one
attempt each. Another trigger blocks completion only after confirmed publication. Killing
that process rolls back completion while retaining its committed lease. A still-valid lease
produces NoWork; the proof then sets its expiry to a past database timestamp and restarts.
The same MessageId is redelivered and retained once by Inventory inbox deduplication. This
uses controlled database expiry, not a wall-clock lease-duration or throughput benchmark.

Initial compilation required explicit per-test cancellation-token references under the existing
xUnit timeout analyzer. The first native 25-case dispatch run passed 23 cases; two proof oracles
needed correction: inbox storage uses `tenant_key`, and a mandatory return is the concrete
`PublishReturnException`. The corrected test checks its native return code/routing key. An
intermediate full 87-case run then passed. Final review added explicit ownership checks: ordinary
grants can be revoked while object owners retain administration authority. Three additional
regressions reject database/schema/business-table ownership even with those ordinary grants
revoked. All 90 cases passed in the final run. No runtime privilege check, assertion or timeout
was weakened. Prior/intermediate passes are not added to the final totals.

All 13 new/changed files remain unstaged. The index and staged diff match the pre-edit snapshots.

## Extraction and review

**Library:** no new reusable mechanism was proven or introduced. Existing outbox model,
dispatcher, leases and worker suffice. No Gloaming implementation/interface/dependency or
Tenancy-to-Access/FGA change. **Sample:** the context, role grants, startup checks, credentials,
publisher selection and one-shot provisioning are consumer policy. **Template:** no output or
preset changed. CI already includes this workflow project; no new project or lane is added.

Review-worthy files:

- [Executable role composition](../../samples/Wholesale/Worker/Broker/BrokerHost.cs),
  [outbox-only context](../../samples/Wholesale/Worker/Broker/SalesOutboxDbContext.cs) and
  [finite role/grant setup and checks](../../samples/Wholesale/Worker/Broker/SalesDispatchDatabase.cs).
- [Native dispatch proofs](../../samples/Wholesale/IntegrationTests/Reservations/Broker/SalesDispatchTests.cs),
  [startup/privilege controls](../../samples/Wholesale/IntegrationTests/Reservations/Broker/SalesDispatchTests.Startup.cs)
  and [fixture composition](../../samples/Wholesale/IntegrationTests/Reservations/Broker/SalesDispatchProof.cs).
- [Consumer guide](../../samples/Wholesale/Worker/Broker/sales-outbox-dispatch.md),
  [workflow entry point](../../samples/Wholesale/Worker/Broker/README.md), root design/development
  and current extraction status. The workflow guide also corrects an existing count: setup now
  creates five queues, including the retained reservation journey's routes.

Next: inbox admission/tenant establishment while preserving one native transaction for handler
effects, outgoing work and delivery completion. Default rollout, broker credential isolation,
production provisioning/rotation, external poolers/failover, policy scaffolding and template
materialization remain separate. A2.7's browser/HTTP evidence is historical for this slice and
is not counted among new results.
