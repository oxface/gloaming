# Native operation composition

Actor identity and tenancy are independent cores. Select either one, or compose both in
consumer code. Neither needs EF, HTTP, a host, a business module or a template.

Register each holder once per native DI scope, then alias its reader and initializer to
that same instance. This example selects both:

```csharp
var services = new ServiceCollection();
services.AddScoped<ActorContextAccessor>();
services.AddScoped<IActorContextAccessor>(sp => sp.GetRequiredService<ActorContextAccessor>());
services.AddScoped<IActorContextInitializer>(sp => sp.GetRequiredService<ActorContextAccessor>());
services.AddScoped<TenantContextAccessor>();
services.AddScoped<ITenantContextAccessor>(sp => sp.GetRequiredService<TenantContextAccessor>());
services.AddScoped<ITenantContextInitializer>(sp => sp.GetRequiredService<TenantContextAccessor>());

using var provider = services.BuildServiceProvider(
    new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
await using var operation = provider.CreateAsyncScope();
var scope = operation.ServiceProvider;
scope.GetRequiredService<IActorContextInitializer>().Initialize(
    new ActorContext(Actor.System(new ActorId("invoice-worker")),
        Actor.Human(new ActorId("user-7"))));
scope.GetRequiredService<ITenantContextInitializer>().Initialize(
    TenantContext.ForTenant(new TenantId("tenant-a")));

// Resolve and invoke the consumer capability after establishment is complete.
var actor = scope.GetRequiredService<IActorContextAccessor>().Current.RequireIdentifiedActor();
var tenant = scope.GetRequiredService<ITenantContextAccessor>().Current.RequireTenant();
```

Use the `Gloaming.ActorIdentity`, `Gloaming.Tenancy` and native
`Microsoft.Extensions.DependencyInjection` namespaces/packages. The selected actor and
tenant above are demonstration inputs, not authentication or authorization. A real host
maps trusted identity and performs admission before publishing these values.

The operation owns its scope until all of its work completes. Each initializer can publish
only once; reading an uninitialized or disposed holder fails. Scope disposal invalidates
all aliases. Worker iterations use separate scopes; a child scope starts uninitialized and
does not inherit its parent's context. These are ordinary native DI scopes, not ambient state.

Publishing the two independent values is not atomic. If the second establishment fails,
abort and dispose the operation; do not run business work or attempt to reset the first.
Finish establishment before concurrent work. Immutable context reads are safe concurrently;
this does not make another scoped service, such as a DbContext, safe for parallel use.

Consumer capabilities check the segments they require. An initiator cannot satisfy an
executing-actor requirement; an identified actor cannot satisfy a selected-tenant requirement.
Anonymous or tenantless operation is an explicit independent choice, not a default for
missing context and not authority to access another tenant.

The [executable composition consumer](../../../tests/ContextCompositionTests/CompositionTests.cs)
checks this setup without a sample business model. Run it from the repository root:

```bash
dotnet test --project tests/ContextCompositionTests/ContextCompositionTests.csproj
```

For native HTTP authentication/admission, use the selected family's HTTP adapter and its
documented ordering. For application boundaries and persistence, see
[application patterns](../../../docs/application-patterns.md).
