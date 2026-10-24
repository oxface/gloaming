# Wholesale reference product

Wholesale composes Access, Sales and Inventory through their public Contracts and
separate module-owned persistence. Run its [Api](Api/README.md), [Worker](Worker/README.md) and
finite setup/maintenance roles. [AppHost](AppHost/README.md) orchestrates local PostgreSQL,
optional Keycloak and the API; it never runs migrations/seeds as an API startup side effect.

| Product path | Supported behavior |
| --- | --- |
| API authentication and Organization routes | Native cookie/OIDC identity mapping, current membership admission, public catalog, scoped identity and protected profile changes with accepted-change audit. |
| API Sales drafts | Private permitted lists/reads, creation, add/change/remove lines, root-version concurrency and submission with required local allowance reservation. |
| Worker `--transport broker` | Durable stock issues, compensating two-line reservations, broker intake, inbox/outbox processing, deadlines, operator compensation retry and restricted Sales dispatch/reply roles. |
| Worker `--transport local` | The same reservation business policy using public module intake Contracts and independent native transactions, without connecting to a broker. |
| Worker `--role inventory` | Open, receive, issue and read stock positions through the existing Inventory commands/inline queries. |
| Worker `--regenerate-stock` | Explicit existing-target stock regeneration, progress, cancellation and indeterminate-commit outcomes. |

The normal API's finite `--initialize-demo` applies the Access, Sales and Inventory migrations and
seeds known Access/catalog/customer/draft data plus a EUR 1000 allowance for each sample customer.
Setup supports a disposable database, not repeatable reconciliation of production data. Inventory
operator commands use their Inventory connection string to that existing storage;
they neither migrate nor seed. See [worker operations](Worker/README.md#module-operations).

All new draft writes require authentication, active Organization admission and actor-bound
antiforgery. Creation grants the creating human its native module Edit permission; changes and
submission use late resource authorization plus the module's own predicates. A failed allowance
reaction rolls back both draft and allowance. Changing a draft is a proposal until save/commit.
The sample supplies initial allowances through finite setup; no allowance administration product
or general permission engine is introduced.

The optional restricted **Sales HTTP RLS** composition deliberately retains its previous scope:
profile reads/edits, private existing-draft reads and line changes. It does not register the new
create/submit routes or widen grant/allowance authority. Its separate role, provisioning and
transaction-local setup remain explicit. This is a supported alternative composition, not the
full Sales lifecycle deployment. Broker dispatch and reply processing retain their separate
restricted credentials and startup rejection checks.

[Wholesale.IntegrationTests](IntegrationTests/README.md) owns application integration and
capability scenarios. Finite child processes preserve crash/recovery assertions; the product
does not reference this support code. Modules retain their owned migrations and literal payloads.
Runtime/browser proofs stay in RuntimeComposition.Tests because they own actual
Aspire/Keycloak/Chromium lifetimes.

Independent technical examples live beside their Gloaming families under src/ and do not
depend on Wholesale. See the [test map](../../tests/README.md) for proof ownership.
