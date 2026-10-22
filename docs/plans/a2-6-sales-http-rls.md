# A2.6: bounded Sales HTTP RLS adoption

The owner approved continuing on `feat/sales-postgres-rls` after checkpoint `635cb5b`.
This increment adopts the reviewed transaction-local capability in one real host composition.
It keeps the owner's existing decision: request/operation owners begin transactions; no central
request transaction owner, middleware reordering or new operation scope.

## Scope

- A separate `ConnectionStrings:Sales` explicitly selects restricted Sales HTTP RLS.
- Cover existing profile read/edit and draft search/detail/edit, including profile audit.
- Select six existing tenant-ownership mappings with native EF model customization; leave
  ordinary Sales registrations and default AppHost/worker modes available independently.
- Provision policies/grants through a finite command against already migrated/seeded tables.
  Runtime startup inspects the actual Sales and Access/Inventory credentials and finite catalog.
- Support existing unconverted varchar discriminators without changing physical schemas;
  reject blank-padded character ownership rather than weakening exact tenant-key comparison.
- Prove native HTTP admission/antiforgery/resource decisions, database read/write enforcement,
  accepted audit atomicity, restricted table rights and physical connection reuse.

## Boundaries

Database tenant equality does not replace within-tenant draft predicates. Immutable admitted
actor/tenant contexts remain fixed; a new human operation admits again. Endpoint read owners
begin short transactions around SQL; existing profile and draft mutation owners retain saves/
commits. Late edit prechecks use a short read transaction, then the mutation repeats module
permission checks inside its existing transaction. ReadCommitted does not fence permission at commit.

The Sales login has no inbox/outbox/workflow or global Access rights; the Access/Inventory
login has no Sales table authority. Credentials/roles are independently provisioned. The finite
installer has no request-time DDL or password/role lifecycle. Its policy operations are explicit,
not automatic EF migration scaffolding. Its bounded startup inspection does not certify every
policy expression/function/view or protect against a privileged administrator changing them.

Default AppHost credentials/rollout, workers and transport role design, generated template
presets, grant administration, creation/submission adoption, automatic policy scaffolding,
external poolers and provider failover remain separate. No new public Gloaming interface,
Tenancy-to-Access/FGA dependency or transaction execution framework is selected.

The [consumer guide](../../samples/Wholesale/Api/README.md#optional-sales-http-row-security)
contains executable setup. The [report](../reports/a2-6-sales-http-rls.md) records current
verification, review-worthy files and limitations.
