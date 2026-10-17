# A2.9: admitted Sales reservation replies under RLS

Owner authorized the next bounded inbox slice on 2026-10-17 after checkpoint `087a008`.
Reuse the current branch and preserve the index; no commit or push is authorized here.

Use the existing reservation-reply journey because one accepted continuation saves workflow
state, enqueues the next command and completes its inbox delivery in the same native transaction.
Keep the provided inbox processor, row lock, retries, save/commit owner and scoped worker.
Add no Gloaming interface or implementation and no adopted/shared transaction mechanism.

Each selected worker has one explicitly admitted Organization, Alpha or Beta. Every attempt
receives a fresh immutable tenant/receiver-owned actor context before the existing processor
begins its transaction. The current RLS interceptor initializes PostgreSQL automatically;
database policies restrict the processor's raw claim, completion and retry SQL. The module's
admitted reply entry checks the existing context rather than rebinding it. The ordinary handler
retains its existing message-established path through the same continuation logic.

An explicit finite setup role provisions a restricted processing login and USING/WITH CHECK
policies on existing `sales.workflow_instances` and `sales.inbox_messages`. The worker may read/
update tenant workflow state and inbox completion/availability, then append outgoing work.
The outbox retains global read/delivery policies so A2.8 can dispatch it; a command-specific
INSERT policy requires the transaction tenant to match the outgoing owner. The process role
may append and read generated timestamps, with no payload read or delivery-state update.
Tracked outgoing ownership validation and atomicity remain alongside that database write check.
No broader business-table, broker or provisioning credential belongs in this host.

Prove actual restricted-role behavior, both tenants, immutable scope/actor admission, native
raw/bulk denial outside the tenant, missing/tenantless contexts, atomic workflow/outbox/inbox
commit and rollback, retry from a fresh scope, competing processes and process death/restart.
Exercise a real RabbitMQ reply before retained processing and prove A2.8 coexistence. Test startup
rejects unsafe roles/policies and retained credentials without provisioning storage or topology.

This is an opt-in WF2 processing composition. Tenant discovery/enumeration, general global
bootstrap, restricted broker intake, other workflows/modules, default rollout, production role
lifecycle, external poolers/failover and template/policy generation remain separately selectable.
