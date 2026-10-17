using Gloaming.EntityFrameworkCore;
using Gloaming.Persistence.EntityFrameworkCore;
using Gloaming.Persistence.EntityFrameworkCore.Postgres;
using Gloaming.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gloaming.Persistence.Postgres.Tests;

// Executable independent consumer: no Wholesale, Access, web or transport dependency.
internal sealed class ConsumerDatabase(
    DbContextOptions<ConsumerDatabase> options,
    ITenantContextAccessor tenancy
) : DbContext(options)
{
    internal string RequiredPartition => tenancy.Current.RequireTenant().Value;
    internal DbSet<Note> Notes => Set<Note>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var notes = modelBuilder.Entity<Note>();
        notes.ToTable("Owned Notes", "workspace");
        notes.HasKey(row => row.Id);
        notes.Property(row => row.Id).HasColumnName("id").ValueGeneratedNever();
        notes.Property(row => row.Partition).HasColumnName("Partition\"Key");
        notes.Property(row => row.Body).HasColumnName("body");
        notes.Property(row => row.Revision).HasColumnName("revision").HasDefaultValue(10);
        notes
            .HasTenantOwnership(row => row.Partition, () => RequiredPartition, "PartitionScope")
            .WithRls(ProofDatabase.Setting);
    }
}

internal sealed class Note
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Partition { get; set; } = "";
    public string Body { get; set; } = "";
    public int Revision { get; set; }
}

internal sealed class Operation : IAsyncDisposable
{
    private readonly ServiceProvider _provider;
    private readonly AsyncServiceScope _scope;
    internal ConsumerDatabase Database { get; }
    internal ITenantContextAccessor Tenancy { get; }

    internal Operation(
        string connection,
        string? partition,
        bool installRls = true,
        bool validateWrites = true,
        Action<DbContextOptionsBuilder>? configure = null
    )
    {
        var services = new ServiceCollection();
        services.AddScoped<TenantContextAccessor>();
        services.AddScoped<ITenantContextAccessor>(provider =>
            provider.GetRequiredService<TenantContextAccessor>()
        );
        services.AddPostgresTenantRowSecurity<ConsumerDatabase>();
        services.AddPostgresTenantRowSecurity<ConsumerDatabase>(); // Idempotent registration.

        if (validateWrites)
            services.AddTenantOwnershipValidation<ConsumerDatabase, string>(context =>
                context.RequiredPartition
            );
        else
            // Deliberate faulty consumer, to prove PostgreSQL rejects writes without the EF guard.
            services.TryAddGloamingCapability<ConsumerDatabase>(
                "tenant-ownership",
                CapabilityPhase.Validate,
                (_, _) => { }
            );

        services.AddDbContext<ConsumerDatabase>(
            (provider, options) =>
            {
                options.UseNpgsql(connection);
                configure?.Invoke(options);
                options.ConfigureGloamingCapabilities<ConsumerDatabase>(provider);
                if (installRls)
                {
                    options.UseRls<ConsumerDatabase>(provider);
                    options.UseRls<ConsumerDatabase>(provider); // Idempotent installation.
                }
            }
        );
        _provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true }
        );
        _scope = _provider.CreateAsyncScope();
        var accessor = _scope.ServiceProvider.GetRequiredService<TenantContextAccessor>();
        if (partition == "tenantless")
            accessor.Initialize(TenantContext.Tenantless());
        else if (partition is not null)
            accessor.Initialize(TenantContext.ForTenant(new(partition)));

        Tenancy = accessor;
        Database = _scope.ServiceProvider.GetRequiredService<ConsumerDatabase>();
    }

    public async ValueTask DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _provider.DisposeAsync();
    }
}
