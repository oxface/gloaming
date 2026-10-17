# A2.7: optional Aspire provisioning for Sales RLS

The owner approved committing A2.6 and proceeding to the next increment on the same branch.
A2.6 was checkpointed as `521e9b3`. This increment carries its bounded HTTP composition into
the existing AppHost without adding a Gloaming interface or adopting worker transport paths.

## Selected composition

`LocalDevelopment:UseSalesRowSecurity=true` selects two restricted runtime logins. PostgreSQL
and optional Keycloak remain native Aspire resources. The explicit `demo-setup` process receives
administrative setup authority plus the two runtime configurations; it creates logins, runs the
existing finite demo migrations/seed, installs the existing six-table policies/grants and checks
both runtime connections. The API receives only runtime connection references/passwords.

Both setup and API use native explicit start. Fresh databases require successful setup before
API start; retained initialized databases need API start without rerunning seed. This avoids
implicit startup DDL and an automatic seed/migration dependency on every retained session.
The ordinary default graph remains available and claims no RLS boundary.

Runtime passwords stay separate secret parameters until the sample assembles them using
Npgsql's connection-string builder. Known inherited provisioning environment values are
explicitly emptied, and selected normal HTTP startup rejects nonempty provisioning credentials.
Role lifecycle is a one-shot fresh-demo operation, not rotation, reconciliation or rollback.

## Proof obligations

- Graph start alone creates no module schema or runtime roles and does not start the API.
- Finite setup exits 0 before explicit API startup; actual restricted credentials pass the
  existing catalog checks and preserve database read/write tenant equality.
- Passwords containing quotes/semicolons survive setup and runtime connection parsing.
- A real browser/OIDC journey selects both tenants and commits an antiforgery-protected
  profile edit under the RLS composition, with foreign tenant state unchanged.
- Ordinary AppHost/browser behavior remains supported. Normal RLS HTTP rejects retained
  provisioning credentials before opening business connections.
- Use disposable resources for validation; preserve personal volumes and existing work/index.

Workers/transport bootstrap, default RLS selection, production provisioning/upgrade/rotation,
automatic policy scaffolding, template output, external poolers and provider failover remain
separate. The [report](../reports/a2-7-aspire-sales-rls.md) records fresh results and limits.
