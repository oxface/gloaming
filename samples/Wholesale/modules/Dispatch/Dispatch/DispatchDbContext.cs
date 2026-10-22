using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;

namespace ModulithFoundry.Samples.Wholesale.Dispatch;

// All cross-tenant worker access is explicit; serving queries always constrain organization.
public sealed class DispatchDbContext(DbContextOptions<DispatchDbContext> options)
    : DbContext(options)
{
    internal DbSet<PopulationRow> Populations => Set<PopulationRow>();
    internal DbSet<DirectoryRow> Customers => Set<DirectoryRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("dispatch");
        modelBuilder.ConfigurePostgresInbox("dispatch", "inbox_messages");
        var population = modelBuilder.Entity<PopulationRow>();
        population.ToTable("directory_populations");
        population.HasKey(row => row.Organization);
        population.Property(row => row.Organization).HasMaxLength(256);
        var customer = modelBuilder.Entity<DirectoryRow>();
        customer.ToTable("customer_directory");
        customer.HasKey(row => new { row.Organization, row.CustomerId });
        customer.Property(row => row.Organization).HasMaxLength(256);
        customer.Property(row => row.Code).HasMaxLength(128);
        customer.Property(row => row.DisplayName).HasMaxLength(256);
        customer.Property(row => row.AddressLine).HasMaxLength(512);
    }
}

internal sealed class PopulationRow
{
    public required string Organization { get; set; }
    public Guid PopulationId { get; set; }
    public Guid? After { get; set; }
    public bool Ready { get; set; }
    public long Copied { get; set; }
}

internal sealed class DirectoryRow
{
    public required string Organization { get; set; }
    public Guid CustomerId { get; set; }
    public long Revision { get; set; }
    public bool Listed { get; set; }
    public string? Code { get; set; }
    public string? DisplayName { get; set; }
    public string? AddressLine { get; set; }
}
