using Gloaming.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Gloaming.EntityFrameworkCoreTests;

// An independently compiled consumer: no identity, tenancy or sample references.
public class GuidConsumerContext(DbContextOptions options, Guid? workspace) : DbContext(options)
{
    public DbSet<AccountReference> Accounts => Set<AccountReference>();
    public DbSet<GlobalLookup> Lookups => Set<GlobalLookup>();
    public int KeyReads { get; private set; }
    private Guid RequiredWorkspace
    {
        get
        {
            KeyReads++;
            return workspace
                ?? throw new InvalidOperationException("Select a workspace explicitly.");
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var account = modelBuilder.Entity<AccountReference>();
        account.ToTable("accounts", "custom_workspace");
        account.HasKey(row => row.Id);
        account.Property(row => row.Id).ValueGeneratedNever();
        account.Property(row => row.Label).IsRequired();
        account.HasQueryFilter("Archived", row => !row.Archived);
        account.HasTenantOwnership(
            row => row.WorkspaceKey,
            () => RequiredWorkspace,
            "IsolationBoundary"
        );
        var lookup = modelBuilder.Entity<GlobalLookup>();
        lookup.ToTable("lookups", "custom_workspace");
        lookup.HasKey(row => row.Id);
        lookup.Property(row => row.Id).ValueGeneratedNever();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        this.ValidateTenantChanges(() => RequiredWorkspace);
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default
    )
    {
        this.ValidateTenantChanges(() => RequiredWorkspace);
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}

public sealed class AccountReference
{
    public Guid Id { get; set; }
    public Guid WorkspaceKey { get; set; }
    public required string Label { get; set; }
    public bool Archived { get; set; }
}

public sealed class GlobalLookup
{
    public Guid Id { get; set; }
    public required string Label { get; set; }
}
