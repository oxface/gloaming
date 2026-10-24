# Gloaming.EntityFrameworkCore

Opt-in, model-selected integration for existing Gloaming save capabilities. Dependencies are EF Core
and DI Abstractions; this package depends on no feature family or provider.

The independent [coordinator contract suite](tests/CapabilityTests/CapabilityTests.csproj) uses
only this library, native EF/Npgsql and DI, with no database connection or sample dependency:

```sh
dotnet test --project src/Gloaming.EntityFrameworkCore/tests/CapabilityTests/CapabilityTests.csproj
```

[Reaction/ownership/outbox adoption](../Gloaming.EventReactions/tests/AdoptionTests/CapabilityCompositionTests.cs)
separately proves real PostgreSQL saving and rollback. The [test guide](../../tests/README.md)
distinguishes library contracts, adopter wiring and Wholesale business assertions.

## Module composition

```csharp
services.AddTenantOwnershipValidation<OrdersContext, string>(db => db.RequiredTenant);
services.AddOutboxValidation<OrdersContext>();
services.AddEventReactions<OrdersContext, OrdersModule>(events =>
    events.AddReaction<OrderSubmitted, ReserveAllowance>());
services.AddDbContext<OrdersContext>((provider, options) =>
    options.UseNpgsql(connection).ConfigureGloamingCapabilities<OrdersContext>(provider));
```

Feature packages supply the helpers above. Their model helpers declare requirements per context:
HasTenantOwnership, ConfigureOutbox and ConfigureEventReactions in this example. Cached model
annotations contain stable keys, never scoped services. Runtime bindings are typed by context.
Another context's registration cannot enable a capability here. Storage declarations do not start
workers or invent integration-message mappings.

ConfigureGloamingCapabilities currently installs one native SaveChanges interceptor; its name allows
future configuration beyond interception. Repeated installation is idempotent. Call it from scoped
AddDbContext configuration with that operation's provider. Context pooling, singleton options and
sharing options across operation scopes are unsupported: the interceptor holds scoped execution state.

## Execution and extension

Both native sync and async paths run selected Prepare bindings, detect the completed tracked changes,
then run Validate bindings. Registration order is preserved within each phase. Validators therefore
see handler-created work even when registered first. Async validation defaults to the sync callback;
asynchronous preparation supplies its own path. Domain handlers lacking sync support reject sync saves.

Feature authors declare RequireGloamingCapability(key) during model construction and register
TryAddGloamingCapability<TContext>(key, phase, execute, executeAsync). Keys are ordinal, stable and
feature-owned; choose unique keys. The first binding for a context/key wins, like native TryAdd.
All model requirements must have bindings before any preparation executes. Unselected bindings
are not executed. Selection uses the completed model on each save, without a static CLR-type cache.

Messaging's due-intent, recurrence and contribution registration helpers select their dependencies
as separate bindings. Each selected protocol validates once per save rather than recursively
executing complete manual validators. The manual validators still include their dependency checks;
choose one save integration for a context.

This is a two-phase save coordinator. Validators inspect completed candidates and should not introduce
new domain decisions. External context overrides/events/interceptors retain their native order.
Gloaming cannot reorder them. Earlier suppression is rejected for required capabilities; later
suppression is unsupported. Nested saving during capability execution is rejected even if its inner error is swallowed.
Observed capability errors fault the operation. Native write/commit failures have no universal reuse guard.

## Boundaries

The coordinator never starts, commits or rolls back transactions. Feature requirements still apply:
local event reactions require a caller transaction before dispatch. On any failure, roll back/dispose
the transaction and dispose the scope. Retry the whole business operation from fresh state.
There is no acknowledgment, transaction binding, post-save drain or execution-strategy retry.

SQL/bulk writes, direct ADO.NET and options omitting this extension bypass this integration. Explicit
feature validators remain supported for consumers choosing their own save integration. Feature-specific native
interception is installed by the owning optional package, outside this save coordinator. No template
preset is added.

The optional [PostgreSQL ownership adapter](../Gloaming.Persistence/Gloaming.Persistence.EntityFrameworkCore.Postgres/README.md)
installs its own native transaction/command interceptor through UseRls and composes this save
coordinator. This package supplies no general native-interceptor registry and retains only
EF Core and DI abstractions dependencies.
