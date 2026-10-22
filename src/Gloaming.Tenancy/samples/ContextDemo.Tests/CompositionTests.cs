using Gloaming.ActorIdentity;
using Gloaming.Tenancy;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.ContextDemo.Inventory.Contracts;
using ModulithFoundry.Samples.Wholesale.ContextDemo.Sales.Contracts;

namespace ModulithFoundry.Samples.Wholesale.ContextDemo.Tests;

public sealed class CompositionTests
{
    [Fact]
    public async Task ActualRegistrationSharesEachHolderAndDisposesAllAliasesSafely()
    {
        await using ServiceProvider provider = CreateProvider();
        IActorContextAccessor actors;
        IActorContextInitializer actorInitializer;
        ITenantContextAccessor tenants;
        ITenantContextInitializer tenantInitializer;
        var actorContext = new ActorContext(Actor.Anonymous);
        TenantContext tenantContext = TenantContext.ForTenant(new TenantId("wholesale-alpha"));
        await using (AsyncServiceScope scope = provider.CreateAsyncScope())
        {
            actors = scope.ServiceProvider.GetRequiredService<IActorContextAccessor>();
            actorInitializer = scope.ServiceProvider.GetRequiredService<IActorContextInitializer>();
            tenants = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
            tenantInitializer =
                scope.ServiceProvider.GetRequiredService<ITenantContextInitializer>();
            Assert.Same(actors, actorInitializer);
            Assert.Same(actors, scope.ServiceProvider.GetRequiredService<ActorContextAccessor>());
            Assert.Same(tenants, tenantInitializer);
            Assert.Same(tenants, scope.ServiceProvider.GetRequiredService<TenantContextAccessor>());
            Assert.NotSame(actors, tenants);
            actorInitializer.Initialize(actorContext);
            tenantInitializer.Initialize(tenantContext);
            Assert.Same(actorContext, actors.Current);
            Assert.Same(tenantContext, tenants.Current);
            Assert.Equal(
                42,
                scope
                    .ServiceProvider.GetRequiredService<IStockAvailability>()
                    .GetAvailableQuantity("WIDGET")
            );
        }

        Assert.Throws<ObjectDisposedException>(() => actors.Current);
        Assert.Throws<ObjectDisposedException>(() => actorInitializer.Initialize(actorContext));
        Assert.Throws<ObjectDisposedException>(() => tenants.Current);
        Assert.Throws<ObjectDisposedException>(() => tenantInitializer.Initialize(tenantContext));
        Assert.Equal("wholesale-alpha", tenantContext.RequireTenant().Value);
        Assert.Same(Actor.Anonymous, actorContext.Actor);
    }

    [Fact]
    public async Task ActorOnlyCompositionNeedsNoTenancyRegistration()
    {
        await using ServiceProvider provider = Build(DemoComposition.CreateActorServices());
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        Assert.Null(scope.ServiceProvider.GetService<ITenantContextAccessor>());
        Assert.Null(scope.ServiceProvider.GetService<ITenantContextInitializer>());
        var human = Actor.Human(new ActorId("global-user-42"));
        scope
            .ServiceProvider.GetRequiredService<IActorContextInitializer>()
            .Initialize(new ActorContext(Actor.System(new ActorId("maintenance")), human));
        ActorContext current = scope
            .ServiceProvider.GetRequiredService<IActorContextAccessor>()
            .Current;
        Assert.Equal("maintenance", current.RequireIdentifiedActor().Id!.Value);
        Assert.Same(human, current.Initiator);
    }

    [Theory]
    [InlineData("wholesale-alpha", 42)]
    [InlineData("wholesale-beta", 7)]
    [InlineData("unknown-tenant", 0)]
    [InlineData("WHOLESALE-ALPHA", 0)]
    public async Task InventoryNeedsOnlyTenancyRegistration(string tenant, int expected)
    {
        await using ServiceProvider provider = Build(DemoComposition.CreateTenantServices());
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        Assert.Null(scope.ServiceProvider.GetService<IActorContextAccessor>());
        Assert.Null(scope.ServiceProvider.GetService<IActorContextInitializer>());
        scope
            .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
            .Initialize(TenantContext.ForTenant(new TenantId(tenant)));
        Assert.Equal(
            expected,
            scope
                .ServiceProvider.GetRequiredService<IStockAvailability>()
                .GetAvailableQuantity("WIDGET")
        );
    }

    [Theory]
    [InlineData("wholesale-alpha", 42, true)]
    [InlineData("wholesale-beta", 7, false)]
    public async Task SameActorCanUseDifferentTenantsThroughSalesAndInventory(
        string tenant,
        int expected,
        bool canFulfil
    )
    {
        await using ServiceProvider provider = CreateProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        Actor human = Actor.Human(new ActorId("global-user-42"));
        Establish(scope, TenantContext.ForTenant(new TenantId(tenant)), human);
        DraftPreview preview = scope
            .ServiceProvider.GetRequiredService<IDraftOrderPreview>()
            .Preview("WIDGET", 10);
        Assert.Equal("WIDGET", preview.Sku);
        Assert.Equal(10, preview.RequestedQuantity);
        Assert.Equal(expected, preview.AvailableQuantity);
        Assert.Equal(canFulfil, preview.CanFulfil);
        Assert.Same(human, preview.RequestedBy);
        Assert.Null(preview.Initiator);
    }

    [Fact]
    public async Task WorkflowAttributionKeepsExecutingSystemAndOriginalHumanDistinct()
    {
        await using ServiceProvider provider = CreateProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        Actor workflow = Actor.System(new ActorId("sales.order-fulfilment"));
        Actor initiator = Actor.Human(new ActorId("global-user-42"));
        Establish(
            scope,
            TenantContext.ForTenant(new TenantId("wholesale-beta")),
            workflow,
            initiator
        );
        DraftPreview preview = scope
            .ServiceProvider.GetRequiredService<IDraftOrderPreview>()
            .Preview("WIDGET", 10);
        Assert.Equal(7, preview.AvailableQuantity);
        Assert.False(preview.CanFulfil);
        Assert.Same(workflow, preview.RequestedBy);
        Assert.Same(initiator, preview.Initiator);
    }

    [Fact]
    public async Task UnestablishedAndTenantlessCallsFailRatherThanReturningOtherTenantData()
    {
        await using ServiceProvider provider = CreateProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        IStockAvailability stock = scope.ServiceProvider.GetRequiredService<IStockAvailability>();
        Assert.Throws<InvalidOperationException>(() => stock.GetAvailableQuantity("WIDGET"));
        scope
            .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
            .Initialize(TenantContext.Tenantless());
        Assert.Throws<TenantRequiredException>(() => stock.GetAvailableQuantity("WIDGET"));
    }

    [Fact]
    public async Task HumanInitiatorCannotMakeAnAnonymousActorEligibleForSales()
    {
        await using ServiceProvider provider = CreateProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        Establish(
            scope,
            TenantContext.ForTenant(new TenantId("wholesale-alpha")),
            Actor.Anonymous,
            Actor.Human(new ActorId("global-user-42"))
        );
        Assert.Throws<IdentifiedActorRequiredException>(() =>
            scope.ServiceProvider.GetRequiredService<IDraftOrderPreview>().Preview("WIDGET", 10)
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SalesRejectsAnUnestablishedRequiredSegment(bool establishActor)
    {
        await using ServiceProvider provider = CreateProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        if (establishActor)
        {
            scope
                .ServiceProvider.GetRequiredService<IActorContextInitializer>()
                .Initialize(new ActorContext(Actor.Human(new ActorId("user"))));
        }
        else
        {
            scope
                .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
                .Initialize(TenantContext.ForTenant(new TenantId("wholesale-alpha")));
        }

        Assert.Throws<InvalidOperationException>(() =>
            scope.ServiceProvider.GetRequiredService<IDraftOrderPreview>().Preview("WIDGET", 10)
        );
    }

    [Fact]
    public async Task IdentifiedActorDoesNotMakeTenantlessSalesEligible()
    {
        await using ServiceProvider provider = CreateProvider();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        Establish(scope, TenantContext.Tenantless(), Actor.Human(new ActorId("user")));
        Assert.Throws<TenantRequiredException>(() =>
            scope.ServiceProvider.GetRequiredService<IDraftOrderPreview>().Preview("WIDGET", 10)
        );
    }

    [Fact]
    public async Task ConcurrentOperationsKeepTheSameActorAndDifferentTenantsIsolated()
    {
        await using ServiceProvider provider = CreateProvider();
        var bothEstablished = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        Actor human = Actor.Human(new ActorId("global-user-42"));
        int established = 0;
        Task<DraftPreview> alpha = PreviewAsync("wholesale-alpha");
        Task<DraftPreview> beta = PreviewAsync("wholesale-beta");
        DraftPreview[] results = await Task.WhenAll(alpha, beta)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(42, results[0].AvailableQuantity);
        Assert.True(results[0].CanFulfil);
        Assert.Equal(7, results[1].AvailableQuantity);
        Assert.False(results[1].CanFulfil);
        Assert.All(results, preview => Assert.Same(human, preview.RequestedBy));

        async Task<DraftPreview> PreviewAsync(string tenant)
        {
            await using AsyncServiceScope scope = provider.CreateAsyncScope();
            Establish(scope, TenantContext.ForTenant(new TenantId(tenant)), human);
            if (Interlocked.Increment(ref established) == 2)
                bothEstablished.SetResult();
            await bothEstablished.Task.WaitAsync(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken
            );
            await Task.Yield();
            return scope
                .ServiceProvider.GetRequiredService<IDraftOrderPreview>()
                .Preview("WIDGET", 10);
        }
    }

    // Consumer-owned composition, not a combined library context or registration protocol.
    private static void Establish(
        AsyncServiceScope scope,
        TenantContext tenant,
        Actor actor,
        Actor? initiator = null
    )
    {
        scope
            .ServiceProvider.GetRequiredService<IActorContextInitializer>()
            .Initialize(new ActorContext(actor, initiator));
        scope.ServiceProvider.GetRequiredService<ITenantContextInitializer>().Initialize(tenant);
    }

    private static ServiceProvider CreateProvider() => Build(DemoComposition.CreateServices());

    private static ServiceProvider Build(ServiceCollection services) =>
        services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
        );
}
