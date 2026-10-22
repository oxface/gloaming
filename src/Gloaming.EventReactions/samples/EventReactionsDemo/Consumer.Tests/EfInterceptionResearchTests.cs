using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Tests.Infrastructure;

namespace ModulithFoundry.Samples.EventReactions.Consumer.Tests;

// Native EF research probes, not an implementation of the proposed Gloaming integration package.
public sealed class EfInterceptionResearchTests(PostgreSqlFixture postgres)
    : IClassFixture<PostgreSqlFixture>
{
    private const string Requirement = "Gloaming:Research:RequiredCapability";
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CachedModelRequirementsSelectScopedWorkWithoutActivatingAnotherContext(
        bool synchronous
    )
    {
        var services = new ServiceCollection();
        services.AddScoped<Calls>();
        services.AddScoped<ModelSelectedProbe>();
        services.AddDbContext<RequiredContext>(
            (provider, options) =>
                options
                    .UseNpgsql("Host=unused;Database=unused;Username=unused")
                    .AddInterceptors(new SelectFromModel(provider))
        );
        services.AddDbContext<PlainContext>(
            (provider, options) =>
                options
                    .UseNpgsql("Host=unused;Database=unused;Username=unused")
                    .AddInterceptors(new SelectFromModel(provider))
        );
        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }
        );
        await using var first = provider.CreateAsyncScope();
        var required = first.ServiceProvider.GetRequiredService<RequiredContext>();
        var plain = first.ServiceProvider.GetRequiredService<PlainContext>();
        var calls = first.ServiceProvider.GetRequiredService<Calls>();
        await SaveAsync(plain, synchronous);
        Assert.Empty(calls.Items);
        Assert.Equal(0, calls.Resolutions);
        await SaveAsync(required, synchronous);
        Assert.Equal(["capability"], calls.Items);
        Assert.Equal(1, calls.Resolutions);

        await using var second = provider.CreateAsyncScope();
        var other = second.ServiceProvider.GetRequiredService<RequiredContext>();
        Assert.Same(required.Model, other.Model);
        Assert.Equal("probe", other.Model.FindAnnotation(Requirement)?.Value);
        await SaveAsync(other, synchronous);
        Assert.Equal(["capability"], second.ServiceProvider.GetRequiredService<Calls>().Items);
        Assert.Equal(["capability"], calls.Items);
        Assert.Equal(1, second.ServiceProvider.GetRequiredService<Calls>().Resolutions);
    }

    [Fact]
    public void ModelRequirementWithoutItsAdapterFailsRatherThanSilentlySkippingWork()
    {
        var services = new ServiceCollection();
        services.AddDbContext<RequiredContext>(
            (provider, options) =>
                options
                    .UseNpgsql("Host=unused;Database=unused;Username=unused")
                    .AddInterceptors(new SelectFromModel(provider))
        );
        using var provider = services.BuildServiceProvider();
        using var scope = provider.CreateScope();
        var context = scope.ServiceProvider.GetRequiredService<RequiredContext>();
        var failure = Assert.Throws<InvalidOperationException>(() => context.SaveChanges());
        Assert.Contains(nameof(ModelSelectedProbe), failure.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void StaticRequirementSurvivesRuntimeModelWithoutAddingRelationalSchemaChanges()
    {
        using var required = new RequiredContext(Options<RequiredContext>());
        using var plain = new PlainContext(Options<PlainContext>());
        Assert.Equal("probe", required.Model.FindAnnotation(Requirement)?.Value);
        Assert.Equal(
            "probe",
            required.GetService<IDesignTimeModel>().Model.FindAnnotation(Requirement)?.Value
        );
        var differences = required
            .GetService<IMigrationsModelDiffer>()
            .GetDifferences(
                plain.GetService<IDesignTimeModel>().Model.GetRelationalModel(),
                required.GetService<IDesignTimeModel>().Model.GetRelationalModel()
            );
        Assert.Empty(differences);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task NativeSavingAndSavedCallbacksBothRunInForwardRegistrationOrder(
        bool synchronous
    )
    {
        var calls = new Calls();
        await using var context = new PlainContext(
            Options<PlainContext>(new TraceSave("first", calls), new TraceSave("second", calls))
        );
        context.SavingChanges += (_, _) => calls.Items.Add("context-saving");
        context.SavedChanges += (_, _) => calls.Items.Add("context-saved");
        await SaveAsync(context, synchronous);
        Assert.Equal(
            [
                "context-saving",
                "first-saving",
                "second-saving",
                "first-saved",
                "second-saved",
                "context-saved",
            ],
            calls.Items
        );
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DuplicateNativeRegistrationExecutesTheSameInterceptorTwice(bool synchronous)
    {
        var calls = new Calls();
        var interceptor = new TraceSave("same", calls);
        await using var context = new PlainContext(Options<PlainContext>(interceptor, interceptor));
        await SaveAsync(context, synchronous);
        Assert.Equal(["same-saving", "same-saving", "same-saved", "same-saved"], calls.Items);
    }

    [Fact]
    public async Task AsyncOnlyNativeInterceptorSilentlySkipsSynchronousSaving()
    {
        var calls = new Calls();
        await using var context = new PlainContext(
            Options<PlainContext>(new AsyncOnlyProbe(calls))
        );
        Assert.Equal(0, context.SaveChanges());
        Assert.Empty(calls.Items);
        Assert.Equal(0, await context.SaveChangesAsync(Token));
        Assert.Equal(["async-saving"], calls.Items);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task AThrowingSavingCallbackHasNoMatchingNativeFailureNotification(
        bool synchronous
    )
    {
        var calls = new Calls();
        await using var context = new PlainContext(
            Options<PlainContext>(new ThrowOnSaving(), new TraceSave("later", calls))
        );
        context.SaveChangesFailed += (_, _) => calls.Items.Add("context-failed");
        await Assert.ThrowsAsync<ProbeFailure>(() => SaveAsync(context, synchronous));
        Assert.Empty(calls.Items);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task SavingCallbacksSeeClrEditsBeforeNativeDetectionAndMayDetectForValidation(
        bool synchronous
    )
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        var calls = new Calls();
        await using var context = new PlainContext(Options<PlainContext>(connection));
        await context.Database.EnsureCreatedAsync(Token);
        var row = new Work { Id = 1, Value = 10 };
        context.Add(row);
        await context.SaveChangesAsync(Token);
        var tracked = context.Entry(row);
        context.SavingChanges += (_, _) =>
        {
            calls.Items.Add(tracked.State.ToString());
            context.ChangeTracker.DetectChanges();
            calls.Items.Add(tracked.State.ToString());
        };
        row.Value = 20;
        await SaveAsync(context, synchronous);
        Assert.Equal(["Unchanged", "Modified"], calls.Items);
        await using var observer = new PlainContext(Options<PlainContext>(connection));
        Assert.Equal(20, (await observer.Set<Work>().SingleAsync(Token)).Value);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OrderedPreparationLetsValidationSeeAllGeneratedWrites(bool synchronous)
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using (var setup = new PlainContext(Options<PlainContext>(connection)))
            await setup.Database.EnsureCreatedAsync(Token);

        // Reverse the input deliberately: composition, rather than host call order, owns these phases.
        var phases = new PhaseProbe[] { new ValidatePreparedRows(), new PrepareRows() };
        await using var context = new PlainContext(
            Options<PlainContext>(connection, phases.OrderBy(phase => phase.Phase).ToArray())
        );
        await Assert.ThrowsAsync<ProbeFailure>(() => SaveAsync(context, synchronous));
        await using var observer = new PlainContext(Options<PlainContext>(connection));
        Assert.Empty(await observer.Set<Work>().ToArrayAsync(Token));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task CommitHooksCanRejectAfterSaveButBeforeTheCallerTransactionCommits(
        bool synchronous
    )
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        var calls = new Calls();
        await using var context = new PlainContext(
            Options<PlainContext>(connection, new TraceSave("save", calls), new RejectCommit(calls))
        );
        await context.Database.EnsureCreatedAsync(Token);
        calls.Items.Clear();
        Assert.True(calls.CommitObservations > 0);
        calls.RejectCommit = true;
        await using var transaction = await context.Database.BeginTransactionAsync(Token);
        context.Add(new Work { Id = 1, Value = 10 });
        await SaveAsync(context, synchronous);
        Assert.Equal(["save-saving", "save-saved"], calls.Items);
        await Assert.ThrowsAsync<ProbeFailure>(async () =>
        {
            if (synchronous)
                transaction.Commit();
            else
                await transaction.CommitAsync(Token);
        });
        Assert.Equal(["save-saving", "save-saved", "committing"], calls.Items);
        await transaction.RollbackAsync(Token);
        await using var observer = new PlainContext(Options<PlainContext>(connection));
        Assert.Empty(await observer.Set<Work>().ToArrayAsync(Token));
    }

    [Fact]
    public async Task CommittingUnderlyingProviderTransactionBypassesEfTransactionInterception()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        var calls = new Calls();
        await using var context = new PlainContext(
            Options<PlainContext>(connection, new RejectCommit(calls))
        );
        await context.Database.EnsureCreatedAsync(Token);
        calls.Items.Clear();
        int observedSchemaCommits = calls.CommitObservations;
        Assert.True(observedSchemaCommits > 0);
        calls.RejectCommit = true;
        await using var transaction = await context.Database.BeginTransactionAsync(Token);
        context.Add(new Work { Id = 1, Value = 10 });
        await context.SaveChangesAsync(Token);
        await transaction.GetDbTransaction().CommitAsync(Token);
        Assert.Empty(calls.Items);
        Assert.Equal(observedSchemaCommits, calls.CommitObservations);
        await using var observer = new PlainContext(Options<PlainContext>(connection));
        Assert.Equal(10, (await observer.Set<Work>().SingleAsync(Token)).Value);
    }

    private static Task SaveAsync(DbContext context, bool synchronous)
    {
        if (!synchronous)
            return context.SaveChangesAsync(Token);

        context.SaveChanges();
        return Task.CompletedTask;
    }

    private static DbContextOptions<TContext> Options<TContext>(params IInterceptor[] interceptors)
        where TContext : DbContext =>
        Options<TContext>("Host=unused;Database=unused;Username=unused", interceptors);

    private static DbContextOptions<TContext> Options<TContext>(
        string connection,
        params IInterceptor[] interceptors
    )
        where TContext : DbContext =>
        new DbContextOptionsBuilder<TContext>()
            .UseNpgsql(connection)
            .AddInterceptors(interceptors)
            .Options;

    private sealed class Work
    {
        public int Id { get; set; }
        public int Value { get; set; }
    }

    private sealed class RequiredContext(DbContextOptions<RequiredContext> options)
        : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            Map(modelBuilder);
            modelBuilder.HasAnnotation(Requirement, "probe");
        }
    }

    private sealed class PlainContext(DbContextOptions<PlainContext> options) : DbContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder) => Map(modelBuilder);
    }

    private static void Map(ModelBuilder model)
    {
        model.Entity<Work>().ToTable("work").HasKey(row => row.Id);
        model.Entity<Work>().Property(row => row.Id).ValueGeneratedNever();
    }

    private sealed class Calls
    {
        internal List<string> Items { get; } = [];
        internal int Resolutions { get; set; }
        internal bool RejectCommit { get; set; }
        internal int CommitObservations { get; set; }
    }

    private sealed class ModelSelectedProbe
    {
        private readonly Calls _calls;

        public ModelSelectedProbe(Calls calls)
        {
            _calls = calls;
            calls.Resolutions++;
        }

        internal void Run() => _calls.Items.Add("capability");
    }

    private sealed class SelectFromModel(IServiceProvider provider) : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData,
            InterceptionResult<int> result
        )
        {
            if (eventData.Context!.Model.FindAnnotation(Requirement)?.Value is "probe")
                provider.GetRequiredService<ModelSelectedProbe>().Run();

            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        ) => ValueTask.FromResult(SavingChanges(eventData, result));
    }

    private sealed class TraceSave(string name, Calls calls) : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData,
            InterceptionResult<int> result
        )
        {
            calls.Items.Add(name + "-saving");
            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        ) => ValueTask.FromResult(SavingChanges(eventData, result));

        public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
        {
            calls.Items.Add(name + "-saved");
            return result;
        }

        public override ValueTask<int> SavedChangesAsync(
            SaveChangesCompletedEventData eventData,
            int result,
            CancellationToken cancellationToken = default
        ) => ValueTask.FromResult(SavedChanges(eventData, result));

        public override void SaveChangesFailed(DbContextErrorEventData eventData) =>
            calls.Items.Add(name + "-failed");

        public override Task SaveChangesFailedAsync(
            DbContextErrorEventData eventData,
            CancellationToken cancellationToken = default
        )
        {
            SaveChangesFailed(eventData);
            return Task.CompletedTask;
        }
    }

    private sealed class ProbeFailure : Exception;

    private sealed class AsyncOnlyProbe(Calls calls) : SaveChangesInterceptor
    {
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        )
        {
            calls.Items.Add("async-saving");
            return ValueTask.FromResult(result);
        }
    }

    private sealed class ThrowOnSaving : SaveChangesInterceptor
    {
        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData,
            InterceptionResult<int> result
        ) => throw new ProbeFailure();

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        ) => throw new ProbeFailure();
    }

    private enum SavePhase
    {
        Prepare = 1,
        Validate = 2,
    }

    private abstract class PhaseProbe : SaveChangesInterceptor
    {
        internal abstract SavePhase Phase { get; }
        protected abstract void Run(DbContext database);

        public override InterceptionResult<int> SavingChanges(
            DbContextEventData eventData,
            InterceptionResult<int> result
        )
        {
            Run(eventData.Context!);
            return result;
        }

        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
            DbContextEventData eventData,
            InterceptionResult<int> result,
            CancellationToken cancellationToken = default
        ) => ValueTask.FromResult(SavingChanges(eventData, result));
    }

    private sealed class PrepareRows : PhaseProbe
    {
        internal override SavePhase Phase => SavePhase.Prepare;

        protected override void Run(DbContext database) =>
            database.Add(new Work { Id = 7, Value = -1 });
    }

    private sealed class ValidatePreparedRows : PhaseProbe
    {
        internal override SavePhase Phase => SavePhase.Validate;

        protected override void Run(DbContext database)
        {
            database.ChangeTracker.DetectChanges();
            if (database.ChangeTracker.Entries<Work>().Any(entry => entry.Entity.Value < 0))
                throw new ProbeFailure();
        }
    }

    private sealed class RejectCommit(Calls calls) : DbTransactionInterceptor
    {
        public override InterceptionResult TransactionCommitting(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result
        )
        {
            calls.CommitObservations++;
            if (!calls.RejectCommit)
                return result;

            calls.Items.Add("committing");
            throw new ProbeFailure();
        }

        public override ValueTask<InterceptionResult> TransactionCommittingAsync(
            DbTransaction transaction,
            TransactionEventData eventData,
            InterceptionResult result,
            CancellationToken cancellationToken = default
        ) => ValueTask.FromResult(TransactionCommitting(transaction, eventData, result));
    }
}
