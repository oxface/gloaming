using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;

namespace Gloaming.EntityFrameworkCore.Tests;

public sealed class CompositionTests
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommonInstallationIsIdempotentAndOnlyModelSelectedBindingsRun(
        bool synchronous
    )
    {
        var services = new ServiceCollection();
        services.AddScoped<Counter>();
        services.TryAddGloamingCapability<ProbeContext>(
            "probe",
            CapabilityPhase.Validate,
            (provider, _) => provider.GetRequiredService<Counter>().Count++
        );
        services.TryAddGloamingCapability<ProbeContext>(
            "unselected",
            CapabilityPhase.Prepare,
            (_, _) => throw new InvalidOperationException("Unselected work must not run.")
        );
        services.TryAddGloamingCapability<ProbeContext>(
            "probe",
            CapabilityPhase.Prepare,
            (_, _) => throw new InvalidOperationException("The first binding must win.")
        );
        services.AddDbContext<ProbeContext>(
            (provider, options) =>
                options
                    .UseNpgsql("Host=unused;Database=unused;Username=unused")
                    .ConfigureGloamingCapabilities<ProbeContext>(provider)
                    .ConfigureGloamingCapabilities<ProbeContext>(provider)
        );
        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true }
        );
        await using var scope = provider.CreateAsyncScope();
        await SaveAsync(scope.ServiceProvider.GetRequiredService<ProbeContext>(), synchronous);
        Assert.Equal(1, scope.ServiceProvider.GetRequiredService<Counter>().Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task MissingModelRequirementBindingFailsBeforeSelectedPreparation(bool synchronous)
    {
        var services = new ServiceCollection();
        services.AddScoped<Counter>();
        services.TryAddGloamingCapability<MissingContext>(
            "probe",
            CapabilityPhase.Prepare,
            (provider, _) => provider.GetRequiredService<Counter>().Count++
        );
        services.AddDbContext<MissingContext>(
            (provider, options) =>
                options
                    .UseNpgsql("Host=unused;Database=unused;Username=unused")
                    .ConfigureGloamingCapabilities<MissingContext>(provider)
        );
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SaveAsync(scope.ServiceProvider.GetRequiredService<MissingContext>(), synchronous)
        );
        Assert.Contains("missing", failure.Message, StringComparison.Ordinal);
        Assert.Equal(0, scope.ServiceProvider.GetRequiredService<Counter>().Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CachedModelsSelectPerContextWhileExecutionUsesTheCurrentOperationScope(
        bool synchronous
    )
    {
        var services = new ServiceCollection();
        services.AddScoped<Counter>();
        services.TryAddGloamingCapability<ProbeContext>(
            "probe",
            CapabilityPhase.Validate,
            (provider, _) => provider.GetRequiredService<Counter>().Count++
        );
        services.TryAddGloamingCapability<PlainContext>(
            "probe",
            CapabilityPhase.Validate,
            (_, _) => throw new InvalidOperationException("This model has no requirement.")
        );
        services.AddDbContext<ProbeContext>(
            (provider, options) =>
                options
                    .UseNpgsql("Host=unused;Database=unused;Username=unused")
                    .ConfigureGloamingCapabilities<ProbeContext>(provider)
        );
        services.AddDbContext<PlainContext>(
            (provider, options) =>
                options
                    .UseNpgsql("Host=unused;Database=unused;Username=unused")
                    .ConfigureGloamingCapabilities<PlainContext>(provider)
        );
        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true }
        );
        await using var first = provider.CreateAsyncScope();
        var firstDatabase = first.ServiceProvider.GetRequiredService<ProbeContext>();
        await SaveAsync(firstDatabase, synchronous);
        await SaveAsync(firstDatabase, synchronous);
        Assert.Equal(2, first.ServiceProvider.GetRequiredService<Counter>().Count);
        await SaveAsync(first.ServiceProvider.GetRequiredService<PlainContext>(), synchronous);
        await using var second = provider.CreateAsyncScope();
        var secondDatabase = second.ServiceProvider.GetRequiredService<ProbeContext>();
        Assert.Same(firstDatabase.Model, secondDatabase.Model);
        await SaveAsync(secondDatabase, synchronous);
        Assert.Equal(1, second.ServiceProvider.GetRequiredService<Counter>().Count);
        Assert.Equal(2, first.ServiceProvider.GetRequiredService<Counter>().Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task PreparationOrderAndDetectedChangesAreVisibleToValidators(bool synchronous)
    {
        var services = new ServiceCollection();
        var trace = new List<string>();
        services.TryAddGloamingCapability<TrackedContext>(
            "validate-first",
            CapabilityPhase.Validate,
            (_, context) =>
            {
                Assert.Equal(EntityState.Modified, context.Entry(context.Item).State);
                Assert.Equal(2, context.Item.Value);
                trace.Add("validate-first");
            }
        );
        services.TryAddGloamingCapability<TrackedContext>(
            "prepare-first",
            CapabilityPhase.Prepare,
            (_, context) =>
            {
                context.Item.Value = 1;
                trace.Add("prepare-first");
            }
        );
        services.TryAddGloamingCapability<TrackedContext>(
            "validate-last",
            CapabilityPhase.Validate,
            (_, _) =>
            {
                trace.Add("validate-last");
                throw new ValidationStopped();
            }
        );
        services.TryAddGloamingCapability<TrackedContext>(
            "prepare-last",
            CapabilityPhase.Prepare,
            (_, context) =>
            {
                Assert.Equal(1, context.Item.Value);
                context.Item.Value = 2;
                trace.Add("prepare-last");
            }
        );
        AddContext<TrackedContext>(services);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<TrackedContext>();
        database.Attach(database.Item);
        database.ChangeTracker.AutoDetectChangesEnabled = false;

        // A rejected validation stops before SQL; native persistence is proved by adopters.
        await Assert.ThrowsAsync<ValidationStopped>(() => SaveAsync(database, synchronous));
        Assert.Equal(["prepare-first", "prepare-last", "validate-first", "validate-last"], trace);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public async Task SwallowedNestedSaveStillFaultsTheOuterOperation(
        bool synchronous,
        bool nestedSynchronous
    )
    {
        var services = new ServiceCollection();
        services.AddScoped<Counter>();
        services.TryAddGloamingCapability<ProbeContext>(
            "probe",
            CapabilityPhase.Prepare,
            (provider, context) =>
            {
                provider.GetRequiredService<Counter>().Count++;
                Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
            },
            async (provider, context, _) =>
            {
                provider.GetRequiredService<Counter>().Count++;
                await Assert.ThrowsAsync<InvalidOperationException>(() =>
                    SaveAsync(context, nestedSynchronous)
                );
            }
        );
        AddContext<ProbeContext>(services);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<ProbeContext>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => SaveAsync(database, synchronous));
        await Assert.ThrowsAsync<InvalidOperationException>(() => SaveAsync(database, synchronous));
        Assert.Equal(1, scope.ServiceProvider.GetRequiredService<Counter>().Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CallbackFailureRequiresANewOperationScope(bool synchronous)
    {
        var services = new ServiceCollection();
        services.AddScoped<Counter>();
        services.TryAddGloamingCapability<ProbeContext>(
            "probe",
            CapabilityPhase.Validate,
            (provider, _) =>
            {
                if (++provider.GetRequiredService<Counter>().Count == 1)
                    throw new ValidationStopped();
            }
        );
        AddContext<ProbeContext>(services);
        await using var provider = services.BuildServiceProvider();
        await using var failed = provider.CreateAsyncScope();
        var database = failed.ServiceProvider.GetRequiredService<ProbeContext>();
        await Assert.ThrowsAsync<ValidationStopped>(() => SaveAsync(database, synchronous));
        await Assert.ThrowsAsync<InvalidOperationException>(() => SaveAsync(database, synchronous));
        Assert.Equal(1, failed.ServiceProvider.GetRequiredService<Counter>().Count);

        await using var fresh = provider.CreateAsyncScope();
        // The new operation supplies a valid candidate instead of reusing failed state.
        fresh.ServiceProvider.GetRequiredService<Counter>().Count = 1;
        await SaveAsync(fresh.ServiceProvider.GetRequiredService<ProbeContext>(), synchronous);
        Assert.Equal(2, fresh.ServiceProvider.GetRequiredService<Counter>().Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task EarlierSuppressionRejectsRequiredCapabilitiesButPreservesNativeEmptyModels(
        bool synchronous
    )
    {
        var services = new ServiceCollection();
        services.AddScoped<Counter>();
        services.TryAddGloamingCapability<ProbeContext>(
            "probe",
            CapabilityPhase.Prepare,
            (provider, _) => provider.GetRequiredService<Counter>().Count++
        );
        AddContext<ProbeContext>(services, suppress: true);
        AddContext<PlainContext>(services, suppress: true);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SaveAsync(scope.ServiceProvider.GetRequiredService<ProbeContext>(), synchronous)
        );
        Assert.Equal(0, scope.ServiceProvider.GetRequiredService<Counter>().Count);
        var plain = scope.ServiceProvider.GetRequiredService<PlainContext>();
        int result = synchronous ? plain.SaveChanges() : await plain.SaveChangesAsync(Token);
        Assert.Equal(17, result);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AnotherContextsBindingCannotSatisfyARequiredCapability(bool synchronous)
    {
        var services = new ServiceCollection();
        services.TryAddGloamingCapability<PlainContext>(
            "probe",
            CapabilityPhase.Validate,
            (_, _) => throw new ValidationStopped()
        );
        AddContext<ProbeContext>(services);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            SaveAsync(scope.ServiceProvider.GetRequiredService<ProbeContext>(), synchronous)
        );
        Assert.Contains("probe", failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AsyncCallbacksReceiveCancellationAndLaterCapabilitiesDoNotRun()
    {
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Token);
        var services = new ServiceCollection();
        services.AddScoped<Counter>();
        services.TryAddGloamingCapability<MissingContext>(
            "probe",
            CapabilityPhase.Prepare,
            (_, _) => throw new ValidationStopped(),
            (_, _, token) =>
            {
                Assert.Equal(cancellation.Token, token);
                cancellation.Cancel();
                token.ThrowIfCancellationRequested();
                return Task.CompletedTask;
            }
        );
        services.TryAddGloamingCapability<MissingContext>(
            "missing",
            CapabilityPhase.Validate,
            (provider, _) => provider.GetRequiredService<Counter>().Count++
        );
        AddContext<MissingContext>(services);
        await using var provider = services.BuildServiceProvider();
        await using var scope = provider.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<MissingContext>();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            database.SaveChangesAsync(cancellation.Token)
        );
        await Assert.ThrowsAsync<InvalidOperationException>(() => database.SaveChangesAsync(Token));
        Assert.Equal(0, scope.ServiceProvider.GetRequiredService<Counter>().Count);
    }

    private static void AddContext<TContext>(IServiceCollection services, bool suppress = false)
        where TContext : DbContext =>
        services.AddDbContext<TContext>(
            (provider, options) =>
            {
                options.UseNpgsql("Host=unused;Database=unused;Username=unused");
                if (suppress)
                    options.AddInterceptors(new SuppressSave());

                options.ConfigureGloamingCapabilities<TContext>(provider);
            }
        );

    private sealed class SuppressSave : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData,
            InterceptionResult<int> result
        ) => InterceptionResult<int>.SuppressWithResult(17);

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        ) => ValueTask.FromResult(InterceptionResult<int>.SuppressWithResult(17));
    }

    private sealed class ValidationStopped : Exception;

    private sealed class Item
    {
        public int Id { get; set; } = 1;
        public int Value { get; set; }
    }

    private sealed class TrackedContext(DbContextOptions<TrackedContext> options)
        : DbContext(options)
    {
        internal Item Item { get; } = new();

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Item>().HasKey(item => item.Id);
            modelBuilder
                .RequireGloamingCapability("validate-first")
                .RequireGloamingCapability("validate-last")
                .RequireGloamingCapability("prepare-first")
                .RequireGloamingCapability("prepare-last");
        }
    }

    private static Task SaveAsync(DbContext context, bool synchronous)
    {
        if (!synchronous)
            return context.SaveChangesAsync(Token);

        context.SaveChanges();
        return Task.CompletedTask;
    }

    private sealed class Counter
    {
        internal int Count { get; set; }
    }

    private sealed class ProbeContext(DbContextOptions<ProbeContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.RequireGloamingCapability("probe");
    }

    private sealed class MissingContext(DbContextOptions<MissingContext> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) =>
            modelBuilder.RequireGloamingCapability("probe").RequireGloamingCapability("missing");
    }

    private sealed class PlainContext(DbContextOptions<PlainContext> options) : DbContext(options);
}
