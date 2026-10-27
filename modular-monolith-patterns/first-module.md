# First protected state-stored module

Use this route for one application-selected operation backed by native EF/PostgreSQL,
tenant ownership and accepted-change audit. Start with [structure](structure.md) and
[bootstrap](bootstrap.md); the application team chooses the module, business behavior,
identity mapping, admission and permissions.

## 1. Select packages and matching examples

Use the [package and version instructions](gloaming.md#package-integration) to configure the
local feed and pin the selected roots to one snapshot. Current packages target .NET 10.
Select a compatible native PostgreSQL EF provider and pin its version in the application.

| Application responsibility | Selected packages |
| --- | --- |
| Actor and tenant values/accessors | `Gloaming.ActorIdentity`, `Gloaming.Tenancy` |
| Native HTTP context establishment | `Gloaming.ActorIdentity.AspNetCore`, `Gloaming.Tenancy.AspNetCore` |
| EF ownership mapping and save protection | `Gloaming.Persistence.EntityFrameworkCore` |
| Explicit transactional audit | `Gloaming.Auditing.EntityFrameworkCore` |
| Native PostgreSQL persistence | `Npgsql.EntityFrameworkCore.PostgreSQL` |

Reference packages from the projects that use their APIs. The host selects the HTTP adapters;
the module owns its persistence and audit wiring. Follow transitive Gloaming dependencies from
the selected snapshot. Add RLS, command receipts or durable messaging when the requested
behavior requires their separate guarantees.

Use these executable examples at the adopted source revision:

- [Independent HTTP consumer](https://github.com/oxface/gloaming/blob/main/tools/release/consumers/HttpEstablishment.cs): native authentication, actor mapping, tenant admission and failed requests.
- [Personnel consumer](https://github.com/oxface/gloaming/blob/main/tools/release/consumers/PersonnelSlice.cs): versioned module migration, tenant-owned business rows, guarded saving and transactional history.
- [Ownership example](https://github.com/oxface/gloaming/blob/main/src/Gloaming.Persistence/samples/PersistenceDemo/README.md): separate contexts, schemas and migration histories.

The Personnel example's entities and membership/permission rules are disposable test policy.
Its direct invocation proves neither HTTP authentication nor production RLS; use it to locate
the native persistence and transaction composition.

## 2. Establish a request before resolving protected persistence

Register native authentication/authorization and implement the application's actor resolver
and tenant admission resolver. Follow the exact
[actor](https://github.com/oxface/gloaming/blob/main/src/Gloaming.ActorIdentity/Gloaming.ActorIdentity.AspNetCore/README.md) and
[tenant](https://github.com/oxface/gloaming/blob/main/src/Gloaming.Tenancy/Gloaming.Tenancy.AspNetCore/README.md)
registration contracts. Their HTTP pipeline is:

```csharp
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseHttpActorContext();
app.UseHttpTenantContext();
```

The actor adapter establishes after the policy's authentication and before its authorization
handlers; the completion middleware also covers policy-free requests. Tenant admission follows
authorization and actor completion. Native authorization handlers therefore cannot use the
selected tenant. Perform tenant-aware resource permission checks in the module operation after
admission, before business changes or history disclosure.

Resolve the protected scoped DbContext only after the required contexts are established.
Keep anonymous and tenantless endpoints explicit. A selected tenant or attributed actor alone
does not grant a business permission.
An application's global account/membership registry may need to resolve identity and admission
before tenancy exists. Its explicit query policy is separate from this walkthrough's tenant-owned
business context; proving registry access alone does not prove persistence ownership protection.

## 3. Map the module and deploy its migration

Create the module's Contracts and implementation, scoped native DbContext, schema, migration
history and named connection. Register its operation implementation and DbContext explicitly
through the composition root; endpoints call Contracts. Use the
[module steps](modules.md#common-module-steps) for dependencies and commit ownership.
Keep individual native `IEntityTypeConfiguration<T>` classes in the owning module.

For the first path, follow the Personnel consumer's manual save integration:

- Map protected business rows with `HasTenantOwnership`, using the established tenant getter.
  Supply owner values and complete tenant-bearing keys/relationships in the module.
- Map history through `ConfigureAudit`, select PostgreSQL `jsonb` for `Details`, and configure
  tenant visibility for audit reads explicitly. Audit mapping supplies no tenant query filter.
- Call `ValidateTenantChanges` and `ValidateAuditChanges` before the base implementation in
  both `SaveChanges(bool)` and `SaveChangesAsync(bool, CancellationToken)` overrides.
- Register `IAudit<ModuleDbContext>` with `EfAudit<ModuleDbContext>` in native scoped DI,
  substituting the owning context type. Ownership mapping and validators are described in the
  [ownership contract](https://github.com/oxface/gloaming/blob/main/src/Gloaming.Persistence/Gloaming.Persistence.EntityFrameworkCore/README.md);
  audit setup is in the
  [audit contract](https://github.com/oxface/gloaming/blob/main/src/Gloaming.Auditing/Gloaming.Auditing.EntityFrameworkCore/README.md).

The typed save coordinator is an alternative to these overrides; choose one integration for
the context. It still leaves save/commit with the native operation owner.

Apply the module's versioned migration through the finite Migrator before runtime work.
Keep setup credentials separate from runtime credentials. If RLS is selected, deploy its
policies/grants and transaction-local tenant setup before protected reads and writes; the
ownership query filter and save guards do not establish that database protection.

## 4. Implement one accepted operation and its read

In a fresh admitted operation scope, resolve the owning context and begin a native transaction.
Load the tenant-visible resource, check current resource permission and expected business
version, then apply the module's accepted change. Stage its explicitly selected `AuditEntry`
through `IAudit<ModuleDbContext>` in that same transaction. The module chooses the action,
subject, actor/tenant attribution, clock, ID and versioned safe details.

Call native save and commit before returning the accepted Contract result. Audit staging
neither saves nor commits, and its guard cannot detect a business change that omitted staging.
On failure, roll back/dispose the transaction and context. Reconcile an uncertain commit from
fresh state before repeating a business action; add logical receipts when retained action/result
recovery is required.

Expose the corresponding read through Contracts with current permission and tenant predicates.
Protect history disclosure as well as the business row. Record the transaction owner and
configuration names in the application's architecture/setup documentation.

## 5. Prove the slice with the native provider

Use the application's own integration suite and actual PostgreSQL to show:

- An accepted operation commits exactly the expected business change and audit attribution/details.
- A denied or newly suspended operation commits neither business changes nor accepted-change audit.
- Direct Contracts calls reject an unauthorized actor or inadmissible tenant even when HTTP
  middleware is absent; include history reads as well as writes.
- Two tenants can use equal resource IDs while business and history reads remain isolated.
- Invalid/stale work and a native SQL failure preserve the earlier committed state.
- Rollback after saving removes both proposed business and history writes; a fresh operation recovers.
- Every supported sync/async save path runs the selected guards.

Inject a real database failure into a selected audited operation, then query from fresh state
to prove both business and audit writes rolled back. Verify restricted runtime credentials
cannot alter the schema or update/delete accepted-change audit, while permitted operations work.

The [full verification guide](verification.md) adds host startup, migration failure and actual
OIDC checks when those are selected. The
[package-adoption guide](https://github.com/oxface/gloaming/blob/main/tools/release/README.md#full-package-adoption-proof)
describes the separate Gloaming-owned proof and its PostgreSQL prerequisites. Run those library
proof commands from the Gloaming checkout; run the new module's acceptance tests in its application.
