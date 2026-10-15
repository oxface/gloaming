using System.Text.Json;
using Gloaming.EntityFrameworkCore;
using Gloaming.EventReactions;
using Gloaming.EventReactions.EntityFrameworkCore;
using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Gloaming.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Tests.Infrastructure;

namespace ModulithFoundry.Samples.EventReactions.Consumer.Tests;

public sealed class CapabilityCompositionTests(PostgreSqlFixture postgres)
    : IClassFixture<PostgreSqlFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task DomainHandlersStageTenantOwnedRowsAndOutboxBeforeValidationAndOneNativeSave(
        bool synchronous
    )
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using var provider = Provider(connection, "valid");
        await using (var scope = provider.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<CompositeContext>();
            await database.Database.EnsureCreatedAsync(Token);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            var root = Work.Draft();
            database.Add(root);
            root.Submit();
            if (synchronous)
                database.SaveChanges();
            else
                await database.SaveChangesAsync(Token);

            Assert.Equal(0, root.Events.Count);
            await transaction.CommitAsync(Token);
        }

        await using var observer = provider.CreateAsyncScope();
        var read = observer.ServiceProvider.GetRequiredService<CompositeContext>();
        Assert.True((await read.Set<Work>().AsNoTracking().SingleAsync(Token)).Submitted);
        Assert.Equal("alpha", (await read.Set<Receipt>().AsNoTracking().SingleAsync(Token)).Owner);
        var message = await read.Set<OutboxMessageRecord>().AsNoTracking().SingleAsync(Token);
        Assert.Equal("orders.accepted", message.MessageName);
        Assert.Equal("alpha", message.TenantKey);
        Assert.Null(message.DispatchedAt);
        Assert.Equal(0, message.Attempts);
    }

    [Theory]
    [InlineData("tenant", false)]
    [InlineData("tenant", true)]
    [InlineData("outbox", false)]
    [InlineData("outbox", true)]
    public async Task ValidatorsRejectHandlerCreatedForeignOwnershipOrTamperedEnvelopesBeforeAnyWrite(
        string fault,
        bool synchronous
    )
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using var provider = Provider(connection, fault);
        await using (var scope = provider.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<CompositeContext>();
            await database.Database.EnsureCreatedAsync(Token);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            var root = Work.Draft();
            database.Add(root);
            root.Submit();
            var failure = await Assert.ThrowsAnyAsync<Exception>(() =>
                SaveAsync(database, synchronous)
            );
            if (fault == "tenant")
                Assert.IsType<TenantOwnershipException>(failure);
            else
                Assert.IsType<InvalidOperationException>(failure);

            await transaction.RollbackAsync(Token);
        }

        await using var observer = provider.CreateAsyncScope();
        var read = observer.ServiceProvider.GetRequiredService<CompositeContext>();
        Assert.Empty(await read.Set<Work>().ToArrayAsync(Token));
        Assert.Empty(await read.Set<Receipt>().ToArrayAsync(Token));
        Assert.Empty(await read.Set<OutboxMessageRecord>().ToArrayAsync(Token));
    }

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

    [Fact]
    public async Task MissingModelRequirementBindingFailsBeforeSelectedPreparation()
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
            scope.ServiceProvider.GetRequiredService<MissingContext>().SaveChangesAsync(Token)
        );
        Assert.Contains("missing", failure.Message, StringComparison.Ordinal);
        Assert.Equal(0, scope.ServiceProvider.GetRequiredService<Counter>().Count);
    }

    [Fact]
    public async Task CachedModelsSelectPerContextWhileExecutionUsesTheCurrentOperationScope()
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
        await firstDatabase.SaveChangesAsync(Token);
        await firstDatabase.SaveChangesAsync(Token);
        Assert.Equal(2, first.ServiceProvider.GetRequiredService<Counter>().Count);
        await first.ServiceProvider.GetRequiredService<PlainContext>().SaveChangesAsync(Token);
        await using var second = provider.CreateAsyncScope();
        var secondDatabase = second.ServiceProvider.GetRequiredService<ProbeContext>();
        Assert.Same(firstDatabase.Model, secondDatabase.Model);
        await secondDatabase.SaveChangesAsync(Token);
        Assert.Equal(1, second.ServiceProvider.GetRequiredService<Counter>().Count);
        Assert.Equal(2, first.ServiceProvider.GetRequiredService<Counter>().Count);
    }

    private static ServiceProvider Provider(string connection, string fault)
    {
        var services = new ServiceCollection();
        services.AddSingleton(new Fault(fault));
        // Validators are intentionally registered first. Preparation must precede both.
        services.AddTenantOwnershipValidation<CompositeContext, string>(context =>
            context.RequiredOwner
        );
        services.AddOutboxValidation<CompositeContext>();
        services.AddPostgresOutbox<CompositeContext>();
        services.AddEventReactions<CompositeContext, Module>(events =>
            events.CollectFrom<Work>(work => work.Events).AddReaction<Submitted, HandleSubmitted>()
        );
        services.AddDbContext<CompositeContext>(
            (provider, options) =>
                options
                    .UseNpgsql(connection)
                    .ConfigureGloamingCapabilities<CompositeContext>(provider)
        );
        return services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
        );
    }

    private static Task SaveAsync(DbContext context, bool synchronous)
    {
        if (!synchronous)
            return context.SaveChangesAsync(Token);

        context.SaveChanges();
        return Task.CompletedTask;
    }

    private sealed class Module;

    private sealed record Fault(string Value);

    private sealed record Submitted(Guid Id);

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

    private sealed class CompositeContext(DbContextOptions<CompositeContext> options)
        : DbContext(options)
    {
        internal string RequiredOwner { get; } = "alpha";

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.ConfigureEventReactions();
            var work = modelBuilder.Entity<Work>();
            work.HasBaseType((Type?)null);
            work.Ignore(row => row.Events);
            work.HasKey(row => new { row.Owner, row.Id });
            work.Property(row => row.Id).ValueGeneratedNever();
            work.HasTenantOwnership(row => row.Owner, () => RequiredOwner, "owner");
            var receipt = modelBuilder.Entity<Receipt>();
            receipt.HasKey(row => new { row.Owner, row.OrderId });
            receipt.HasTenantOwnership(row => row.Owner, () => RequiredOwner, "owner");
            receipt
                .HasOne<Work>()
                .WithMany()
                .HasForeignKey(row => new { row.Owner, Id = row.OrderId });
            modelBuilder
                .ConfigurePostgresOutbox("composition", "outbox")
                .HasTenantOwnership(row => row.TenantKey!, () => RequiredOwner, "owner");
        }
    }

    private sealed class Work
    {
        public PendingEvents Events { get; } = new();
        public Guid Id { get; private set; }
        public string Owner { get; private set; } = "alpha";
        public bool Submitted { get; private set; }

        internal static Work Draft() => new() { Id = Guid.NewGuid() };

        internal void Submit()
        {
            Submitted = true;
            Events.Raise(new Submitted(Id));
        }
    }

    private sealed class Receipt
    {
        public required string Owner { get; set; }
        public Guid OrderId { get; set; }
    }

    private sealed class HandleSubmitted(
        CompositeContext database,
        IOutbox<CompositeContext> outbox,
        Fault fault
    ) : IEventReaction<Submitted>, ISynchronousEventReaction<Submitted>
    {
        public void Handle(Submitted fact)
        {
            database.Add(
                new Receipt
                {
                    Owner = fault.Value == "tenant" ? "beta" : "alpha",
                    OrderId = fact.Id,
                }
            );
            outbox.Enqueue(
                new OutgoingMessage(
                    Guid.NewGuid(),
                    "orders",
                    "orders.accepted",
                    1,
                    JsonSerializer.SerializeToElement(new { fact.Id }),
                    "alpha"
                )
            );
            if (fault.Value == "outbox")
                database
                    .Entry(database.Set<OutboxMessageRecord>().Local.Single())
                    .Property(row => row.RouteKey)
                    .CurrentValue = "tampered";
        }

        public Task HandleAsync(Submitted fact, CancellationToken cancellationToken)
        {
            Handle(fact);
            return Task.CompletedTask;
        }
    }
}
