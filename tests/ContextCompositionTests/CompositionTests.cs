using Gloaming.ActorIdentity;
using Gloaming.Tenancy;
using Microsoft.Extensions.DependencyInjection;

namespace ModulithFoundry.ContextCompositionTests;

public sealed class CompositionTests
{
    [Fact]
    public void ScopedAliasesShareOneHolderAndAllBecomeUnavailableAfterDisposal()
    {
        using var provider = Build(Services());
        IActorContextAccessor actors;
        ITenantContextAccessor tenants;
        IActorContextInitializer actorInitializer;
        ITenantContextInitializer tenantInitializer;
        var actor = new ActorContext(Actor.Anonymous);
        var tenant = TenantContext.ForTenant(new TenantId("tenant-a"));
        using (var scope = provider.CreateScope())
        {
            var services = scope.ServiceProvider;
            actors = services.GetRequiredService<IActorContextAccessor>();
            tenants = services.GetRequiredService<ITenantContextAccessor>();
            actorInitializer = services.GetRequiredService<IActorContextInitializer>();
            tenantInitializer = services.GetRequiredService<ITenantContextInitializer>();
            Assert.Same(actors, actorInitializer);
            Assert.Same(actors, services.GetRequiredService<ActorContextAccessor>());
            Assert.Same(tenants, tenantInitializer);
            Assert.Same(tenants, services.GetRequiredService<TenantContextAccessor>());
            actorInitializer.Initialize(actor);
            tenantInitializer.Initialize(tenant);
            Assert.Same(actor, actors.Current);
            Assert.Same(tenant, tenants.Current);
        }

        Assert.Throws<ObjectDisposedException>(() => actors.Current);
        Assert.Throws<ObjectDisposedException>(() => tenants.Current);
        Assert.Throws<ObjectDisposedException>(() => actorInitializer.Initialize(actor));
        Assert.Throws<ObjectDisposedException>(() => tenantInitializer.Initialize(tenant));
        Assert.Throws<InvalidOperationException>(() =>
            provider.GetRequiredService<IActorContextAccessor>()
        );
        Assert.Throws<InvalidOperationException>(() =>
            provider.GetRequiredService<ITenantContextAccessor>()
        );
    }

    [Fact]
    public void ActorOnlyConsumerNeedsNoTenancyRegistration()
    {
        using var provider = Build(Services(tenants: false));
        using var scope = provider.CreateScope();
        Assert.Null(scope.ServiceProvider.GetService<ITenantContextAccessor>());
        Assert.Null(scope.ServiceProvider.GetService<ITenantContextInitializer>());
        var initiator = Actor.Human(new ActorId("user-7"));
        var executor = Actor.System(new ActorId("maintenance"));
        scope
            .ServiceProvider.GetRequiredService<IActorContextInitializer>()
            .Initialize(new ActorContext(executor, initiator));
        var current = scope.ServiceProvider.GetRequiredService<IActorContextAccessor>().Current;
        Assert.Same(executor, current.RequireIdentifiedActor());
        Assert.Same(initiator, current.Initiator);
    }

    [Fact]
    public void TenantOnlyConsumerNeedsNoActorRegistration()
    {
        using var provider = Build(Services(actors: false));
        using var scope = provider.CreateScope();
        Assert.Null(scope.ServiceProvider.GetService<IActorContextAccessor>());
        Assert.Null(scope.ServiceProvider.GetService<IActorContextInitializer>());
        var tenant = new TenantId("tenant-a");
        scope
            .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
            .Initialize(TenantContext.ForTenant(tenant));
        Assert.Equal(
            tenant,
            scope
                .ServiceProvider.GetRequiredService<ITenantContextAccessor>()
                .Current.RequireTenant()
        );
    }

    [Theory]
    [InlineData("missing-actor")]
    [InlineData("missing-tenant")]
    [InlineData("anonymous")]
    [InlineData("tenantless")]
    public void ConsumerChecksEachRequiredSegmentBeforeWork(string mode)
    {
        using var provider = Build(Services());
        using var scope = provider.CreateScope();
        var human = Actor.Human(new ActorId("user-7"));
        if (mode != "missing-actor")
            scope
                .ServiceProvider.GetRequiredService<IActorContextInitializer>()
                .Initialize(new ActorContext(mode == "anonymous" ? Actor.Anonymous : human, human));

        if (mode != "missing-tenant")
            scope
                .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
                .Initialize(
                    mode == "tenantless"
                        ? TenantContext.Tenantless()
                        : TenantContext.ForTenant(new TenantId("tenant-a"))
                );

        // An initiator cannot satisfy the executing-actor requirement; an identified
        // actor cannot satisfy the selected-tenant requirement.
        var consumer = RequiredOperation(scope.ServiceProvider);
        if (mode == "anonymous")
            Assert.Throws<IdentifiedActorRequiredException>(consumer.Read);
        else if (mode == "tenantless")
            Assert.Throws<TenantRequiredException>(consumer.Read);
        else
            Assert.Throws<InvalidOperationException>(consumer.Read);
    }

    [Fact]
    public async Task ConcurrentOperationsKeepTheirTenantsSeparateAndRetainAttribution()
    {
        using var provider = Build(Services());
        var human = Actor.Human(new ActorId("user-7"));
        var executor = Actor.System(new ActorId("workflow"));
        var bothEstablished = new TaskCompletionSource(
            TaskCreationOptions.RunContinuationsAsynchronously
        );
        int established = 0;
        var first = ReadAsync(new TenantId("tenant-a"));
        var second = ReadAsync(new TenantId("tenant-b"));
        var results = await Task.WhenAll(first, second)
            .WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.Equal(new TenantId("tenant-a"), results[0].Tenant);
        Assert.Equal(new TenantId("tenant-b"), results[1].Tenant);
        Assert.All(
            results,
            result =>
            {
                Assert.Same(executor, result.Actor);
                Assert.Same(human, result.Initiator);
            }
        );

        async Task<Snapshot> ReadAsync(TenantId tenant)
        {
            await using var scope = provider.CreateAsyncScope();
            scope
                .ServiceProvider.GetRequiredService<IActorContextInitializer>()
                .Initialize(new ActorContext(executor, human));
            scope
                .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
                .Initialize(TenantContext.ForTenant(tenant));
            if (Interlocked.Increment(ref established) == 2)
                bothEstablished.SetResult();

            await bothEstablished.Task.WaitAsync(
                TimeSpan.FromSeconds(10),
                TestContext.Current.CancellationToken
            );
            return RequiredOperation(scope.ServiceProvider).Read();
        }
    }

    [Fact]
    public void ChildScopeStartsUninitializedAndItsDisposalLeavesTheParentUsable()
    {
        using var provider = Build(Services());
        using var parent = provider.CreateScope();
        var human = Actor.Human(new ActorId("user-7"));
        parent
            .ServiceProvider.GetRequiredService<IActorContextInitializer>()
            .Initialize(new ActorContext(human));
        parent
            .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
            .Initialize(TenantContext.ForTenant(new TenantId("parent")));
        using (var child = parent.ServiceProvider.CreateScope())
        {
            Assert.Throws<InvalidOperationException>(() =>
                RequiredOperation(child.ServiceProvider).Read()
            );
            child
                .ServiceProvider.GetRequiredService<IActorContextInitializer>()
                .Initialize(new ActorContext(human));
            child
                .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
                .Initialize(TenantContext.ForTenant(new TenantId("child")));
            Assert.Equal(
                new TenantId("child"),
                RequiredOperation(child.ServiceProvider).Read().Tenant
            );
        }

        Assert.Equal(
            new TenantId("parent"),
            RequiredOperation(parent.ServiceProvider).Read().Tenant
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FailureOrCancellationDisposesTheOperationAndTheNextScopeStartsFresh(bool cancelled)
    {
        using var provider = Build(Services());
        IActorContextAccessor actors = null!;
        ITenantContextAccessor tenants = null!;
        void Run()
        {
            using var scope = provider.CreateScope();
            actors = scope.ServiceProvider.GetRequiredService<IActorContextAccessor>();
            tenants = scope.ServiceProvider.GetRequiredService<ITenantContextAccessor>();
            scope
                .ServiceProvider.GetRequiredService<IActorContextInitializer>()
                .Initialize(new ActorContext(Actor.System(new ActorId("worker"))));
            scope
                .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
                .Initialize(TenantContext.ForTenant(new TenantId("previous")));
            if (cancelled)
                throw new OperationCanceledException(new CancellationToken(true));

            throw new InvalidDataException("Consumer operation failed.");
        }

        if (cancelled)
            Assert.Throws<OperationCanceledException>(Run);
        else
            Assert.Throws<InvalidDataException>(Run);

        Assert.Throws<ObjectDisposedException>(() => actors.Current);
        Assert.Throws<ObjectDisposedException>(() => tenants.Current);
        using var next = provider.CreateScope();
        Assert.NotSame(actors, next.ServiceProvider.GetRequiredService<IActorContextAccessor>());
        Assert.NotSame(tenants, next.ServiceProvider.GetRequiredService<ITenantContextAccessor>());
        Assert.Throws<InvalidOperationException>(() =>
            RequiredOperation(next.ServiceProvider).Read()
        );
    }

    [Fact]
    public void PartialEstablishmentDoesNotPublishAnAtomicPairAndMustAbortTheScope()
    {
        using var provider = Build(Services());
        IActorContextAccessor actors;
        using (var scope = provider.CreateScope())
        {
            actors = scope.ServiceProvider.GetRequiredService<IActorContextAccessor>();
            var human = Actor.Human(new ActorId("user-7"));
            scope
                .ServiceProvider.GetRequiredService<IActorContextInitializer>()
                .Initialize(new ActorContext(human));
            Assert.Throws<ArgumentException>(() =>
                scope
                    .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
                    .Initialize(TenantContext.ForTenant(new TenantId(" ")))
            );
            Assert.Same(human, actors.Current.Actor);
            Assert.Throws<InvalidOperationException>(() =>
                RequiredOperation(scope.ServiceProvider).Read()
            );
        }

        Assert.Throws<ObjectDisposedException>(() => actors.Current);
    }

    private static ServiceCollection Services(bool actors = true, bool tenants = true)
    {
        var services = new ServiceCollection();
        if (actors)
        {
            services.AddScoped<ActorContextAccessor>();
            services.AddScoped<IActorContextAccessor>(provider =>
                provider.GetRequiredService<ActorContextAccessor>()
            );
            services.AddScoped<IActorContextInitializer>(provider =>
                provider.GetRequiredService<ActorContextAccessor>()
            );
        }

        if (tenants)
        {
            services.AddScoped<TenantContextAccessor>();
            services.AddScoped<ITenantContextAccessor>(provider =>
                provider.GetRequiredService<TenantContextAccessor>()
            );
            services.AddScoped<ITenantContextInitializer>(provider =>
                provider.GetRequiredService<TenantContextAccessor>()
            );
        }

        return services;
    }

    private static ServiceProvider Build(ServiceCollection services) =>
        services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
        );

    private static Operation RequiredOperation(IServiceProvider services) =>
        new(
            services.GetRequiredService<IActorContextAccessor>(),
            services.GetRequiredService<ITenantContextAccessor>()
        );

    // Consumer-owned requirements, without an invented business module or a new runtime.
    private sealed class Operation(IActorContextAccessor actors, ITenantContextAccessor tenants)
    {
        public Snapshot Read()
        {
            var actor = actors.Current;
            return new(
                actor.RequireIdentifiedActor(),
                actor.Initiator,
                tenants.Current.RequireTenant()
            );
        }
    }

    private sealed record Snapshot(Actor Actor, Actor? Initiator, TenantId Tenant);
}
