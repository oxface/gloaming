# Tenancy

A package-free, independently adoptable library for an operation's explicitly selected
isolation boundary. It has no dependency on actor identity, DI, ASP.NET Core, EF, transports,
Aspire or the sample Access model. Membership and permissions remain consumer policy.

## Public interface

- `TenantId`: a validated opaque string, preserved and compared exactly.
- `TenantContext.ForTenant(tenant)`: an immutable selected tenant.
- `TenantContext.Tenantless()`: deliberate execution outside a tenant boundary, distinct
  from missing initialization. It grants no unrestricted data access.
- `ITenantContextAccessor.Current`: read-only access to the established context.
- `ITenantContextInitializer.Initialize`: host-facing, single-assignment establishment.
- `TenantContextAccessor`: scope-owned holder implementing both interfaces and `IDisposable`.
- `RequireTenant()`: explicitly checks selection, throwing `TenantRequiredException` for
  deliberate tenantless execution. It does not establish membership or permission.

Consumers choose and resolve a canonical tenant before initialization. The core prescribes
no route, hostname, principal, user-to-tenant relationship or Organization data model.
Organization is the sample's domain/UI term; Tenant is the technical isolation boundary.
A consumer may map an `OrganizationId`, `WorkspaceId` or another domain identifier into
this key without renaming its domain types.

## Consumer setup

```csharp
services.AddScoped<TenantContextAccessor>();
services.AddScoped<ITenantContextAccessor>(provider =>
    provider.GetRequiredService<TenantContextAccessor>());
services.AddScoped<ITenantContextInitializer>(provider =>
    provider.GetRequiredService<TenantContextAccessor>());

await using var scope = serviceProvider.CreateAsyncScope();
scope.ServiceProvider.GetRequiredService<ITenantContextInitializer>()
    .Initialize(TenantContext.ForTenant(new TenantId(consumerTenantKey)));
```

Business code injects the read accessor. The host finishes required establishment before
business work and disposes after awaiting all branches. Early reads, repeat initialization
and access after disposal fail. Concurrent reads of the established value are supported;
disposal is idempotent. No ambient state, rebinding, permission check or automatic selection
is introduced. Separate operations use separate scopes.

See [tenancy-only Inventory and combined Sales usage](../../../samples/Wholesale/ContextDemo/README.md)
and [the split report](../../../docs/reports/e1-identity-split.md). Optional [HTTP selection/admission utilities](../Gloaming.Tenancy.AspNetCore/README.md)
and [EF ownership validation](../../Gloaming.Persistence/Gloaming.Persistence.EntityFrameworkCore/README.md) are
implemented as separate opt-in packages. No stable wire or persistence format is
established by this library.

## Deferred direction

HTTP tenant selection/admission composition is implemented in the optional sibling adapter;
EF ownership filtering/write validation is implemented in Persistence.EntityFrameworkCore.
Neither becomes a dependency of this core. No cross-process context format or database
isolation mechanism is supplied here. Future worker/message integration needs its own explicit
selection/admission and compatibility contract; membership remains consumer policy.
