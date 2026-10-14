using Gloaming.ActorIdentity;
using Gloaming.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.ContextDemo;
using ModulithFoundry.Samples.Wholesale.ContextDemo.Inventory.Contracts;
using ModulithFoundry.Samples.Wholesale.ContextDemo.Sales.Contracts;

var options = new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true };
await using var tenantProvider = DemoComposition
    .CreateTenantServices()
    .BuildServiceProvider(options);
await using var actorProvider = DemoComposition.CreateActorServices().BuildServiceProvider(options);
await using var combinedProvider = DemoComposition.CreateServices().BuildServiceProvider(options);

// These are explicit demonstration identities, with no authentication or HTTP ingress.
var human = Actor.Human(new ActorId("demo-user-alex"));
var workflow = Actor.System(new ActorId("sales.order-fulfilment"));
string[] availability = await Task.WhenAll(
    Task.Run(() => ReadAvailability(tenantProvider, "wholesale-alpha")),
    Task.Run(() => ReadAvailability(tenantProvider, "wholesale-beta"))
);
foreach (string line in availability)
{
    Console.WriteLine(line);
}

Console.WriteLine(Preview(combinedProvider, "wholesale-alpha", human));
Console.WriteLine(Preview(combinedProvider, "wholesale-beta", workflow, human));

using (var scope = actorProvider.CreateScope())
{
    scope
        .ServiceProvider.GetRequiredService<IActorContextInitializer>()
        .Initialize(new ActorContext(Actor.Anonymous));
    ActorContext context = scope
        .ServiceProvider.GetRequiredService<IActorContextAccessor>()
        .Current;
    Console.WriteLine($"host-info: actor={context.Actor.Kind}");
}

using (var scope = actorProvider.CreateScope())
{
    scope
        .ServiceProvider.GetRequiredService<IActorContextInitializer>()
        .Initialize(new ActorContext(Actor.System(new ActorId("sample.maintenance"))));
    Actor actor = scope
        .ServiceProvider.GetRequiredService<IActorContextAccessor>()
        .Current.RequireIdentifiedActor();
    Console.WriteLine($"maintenance: actor={actor.Id!.Value}");
}

static string ReadAvailability(ServiceProvider provider, string tenantKey)
{
    using var scope = provider.CreateScope();
    scope
        .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
        .Initialize(TenantContext.ForTenant(new TenantId(tenantKey)));
    int available = scope
        .ServiceProvider.GetRequiredService<IStockAvailability>()
        .GetAvailableQuantity("WIDGET");
    return $"{tenantKey}: availability={available}";
}

static string Preview(
    ServiceProvider provider,
    string tenantKey,
    Actor actor,
    Actor? initiator = null
)
{
    using var scope = provider.CreateScope();
    // Host-owned composition: establish both contexts before invoking Sales.
    scope
        .ServiceProvider.GetRequiredService<IActorContextInitializer>()
        .Initialize(new ActorContext(actor, initiator));
    scope
        .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
        .Initialize(TenantContext.ForTenant(new TenantId(tenantKey)));
    DraftPreview preview = scope
        .ServiceProvider.GetRequiredService<IDraftOrderPreview>()
        .Preview("WIDGET", 10);
    return $"{tenantKey}: requested={preview.RequestedQuantity}, available={preview.AvailableQuantity}, "
        + $"can-fulfil={preview.CanFulfil}, actor={preview.RequestedBy.Kind}:{preview.RequestedBy.Id!.Value}, "
        + $"initiator={preview.Initiator?.Id?.Value ?? "none"}";
}
