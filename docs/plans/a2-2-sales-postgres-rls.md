# A2.2: opt-in Sales PostgreSQL tenant isolation

Owner-authorized follow-up on 2026-10-16 after the A2/A2.1 review snapshot was committed
as `b2f0d01`. Work reuses the checkout on `feat/sales-postgres-rls`, stacked on that Sales
permission implementation; fetched `origin/main` remains `d35b3ee`. The
[A2 assessment](../reports/tenant-authorization-assessment.md) and
[primary-source evidence](../reports/tenant-authorization-primary-sources.md) supply the
role, transaction and transport boundaries. Commit/push approval is separate.

The owner later selected [A2.3 utility extraction](a2-3-postgres-tenant-context.md). The
scope below records A2.2's original consumer-only increment; its explicit setup is now adopted
through that optional public adapter, with policies/roles still sample-owned.

## Selected consumer

Add `--tenant-isolation` to the existing CQRS executable, using a fresh disposable database,
a separately provisioned migration owner and a restricted runtime login. Native setup
applies existing Sales migrations, seeds two Organizations and installs explicit RLS on
customer profiles, customer addresses, draft roots, draft lines and draft grants. No default
AppHost, HTTP, module registration, worker or migration behavior changes.

Enable and force RLS on all five tables. Use an explicit tenant-equality USING and WITH CHECK
against the opaque text Organization key, normalizing an empty custom setting to SQL NULL.
The runtime role has no superuser/bypass/role-creation/database-creation privileges, role
memberships, protected-table ownership or Sales-schema CREATE privilege. It receives SELECT
on the five tables and INSERT/UPDATE/DELETE on the four business tables; grants are read-only.
The exact role attributes and table flags are inspected through the actual runtime login.

The consumer explicitly begins a native transaction, including for reads, and sets its
transaction-local database context on that exact connection/transaction before protected SQL.
The immutable operation supplies the selected tenant; setup does not authenticate, admit,
rebind or infer any actor/tenant/initiator. SQL parameterization preserves opaque text keys.
Save, commit, rollback, retries and disposal remain native caller responsibilities.

The executable contrasts unfiltered native tenant rows with Sales' permission-filtered
query: Alpha's actor sees one database row in either Organization, but only has a Sales grant
in Alpha. Proofs also exercise an existing-draft edit through the actual Sales Contract.
Creation, submission/allowances, Inventory and durable workflows are outside this composition.
The runtime cannot provision creator grants or inspect transport tables.

## Proof obligations

Use PostgreSQL 18.6 and the actual owner/runtime/admin credentials, rather than an admin
connection temporarily using SET ROLE. Exercise effective-role/catalog checks, ENABLE/FORCE,
enabled/no-policy and disabled/bypass controls; missing/empty/unknown contexts; raw SQL,
disabled EF filters, native tracked edits, ExecuteUpdate/Delete, foreign INSERT/owner changes,
upserts and native COPY. Demonstrate custom-setting forgery and hidden-constraint disclosure
to bound the threat model rather than claiming credential containment.

Force physical pool reuse through Alpha commit, Beta rollback and missing context. Cover
pre-cancelled setup/SQL with rollback, savepoints before/after setup, a terminated session and
reconnect, concurrent separate operations, and one native transient error replayed as a whole
transaction with setup inside each attempt and the same captured immutable operation facts.
Use a No Reset On Close control and reject that setting, multiplexing and connection Options
in the opt-in composition. No EF context pooling or external pooler guarantee follows.

## Boundaries and handoff

RLS supplies database tenant equality only. Authentication, current membership admission,
system mandates and within-tenant Sales grants retain their existing owners. A runtime
credential holder can set a different custom tenant value; privileged roles, owner DDL,
unprotected tables, definer paths and already materialized data require separate analysis.
Do not add blanket policies to inbox/outbox tables whose selection/context/transaction
boundaries differ from business handling.

No middleware reordering, library source/interface, Tenancy-to-Access/FGA dependency, new
package, generic transaction framework or template preset is selected. Record actual results,
remaining adoption proofs and extraction findings in the
[slice report](../reports/a2-2-sales-postgres-rls.md); keep consumer instructions beside
[the executable](../../samples/Wholesale/CqrsDemo/TenantIsolation/README.md).
