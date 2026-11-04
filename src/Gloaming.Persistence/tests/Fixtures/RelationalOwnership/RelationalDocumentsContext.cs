using Gloaming.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace RelationalOwnershipConsumer;

// Independent native consumer: the tenant stays on the entity; values have no entity identity.
public class RelationalDocumentsContext(DbContextOptions options, string tenant)
    : DbContext(options)
{
    public string RequiredTenant => tenant;
    public DbSet<RelationalDocumentRow> Documents => Set<RelationalDocumentRow>();
    protected virtual bool RegisterOwnershipFirst => false;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var row = modelBuilder
            .Entity<RelationalDocumentRow>()
            .ToTable("documents", "inline_consumer");
        row.HasKey(x => x.Id);
        row.Property(x => x.Id).ValueGeneratedNever();
        row.Property(x => x.Tenant).HasColumnName("tenant_key");
        row.HasQueryFilter("Archived", x => !x.Archived);
        if (RegisterOwnershipFirst)
            row.HasTenantOwnership(x => x.Tenant, () => RequiredTenant, "TenantScope");

        row.ComplexProperty(
            x => x.Text,
            text =>
            {
                text.IsRequired();
                text.Property(x => x.Name).HasColumnName("Name").HasMaxLength(200).IsRequired();
                text.Property(x => x.TechnologyContext)
                    .HasColumnName("TechnologyContext")
                    .IsRequired(false);
            }
        );
        row.ComplexProperty(
            x => x.Review,
            review =>
            {
                review.IsRequired();
                review.Property(x => x.Status).HasColumnName("ReviewStatus").IsRequired();
                review.ComplexProperty(
                    x => x.Notes,
                    notes =>
                    {
                        notes.IsRequired();
                        notes.Property(x => x.Summary).HasColumnName("ReviewSummary").IsRequired();
                        notes
                            .Property(x => x.Comment)
                            .HasColumnName("ReviewComment")
                            .IsRequired(false);
                    }
                );
                review.ComplexProperty(
                    x => x.SelectedType,
                    selection =>
                    {
                        selection.IsRequired(false);
                        selection.Property(x => x.Slug).HasColumnName("ReviewSlug").IsRequired();
                        selection.Property(x => x.Revision).HasColumnName("ReviewRevision");
                    }
                );
            }
        );
        row.ComplexProperty(
            x => x.Selection,
            selection =>
            {
                selection.IsRequired(false);
                selection.Property(x => x.Slug).HasColumnName("SelectionSlug").IsRequired();
                selection.Property(x => x.Revision).HasColumnName("SelectionRevision");
            }
        );
        row.ComplexProperty(x => x.Snapshot, snapshot => snapshot.ToJson("SnapshotJson"));
        row.ComplexCollection(x => x.Defaults).ToJson("DefaultsJson");
        if (!RegisterOwnershipFirst)
            row.HasTenantOwnership(x => x.Tenant, () => RequiredTenant, "TenantScope");
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        this.ValidateTenantChanges(() => RequiredTenant);
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default
    )
    {
        this.ValidateTenantChanges(() => RequiredTenant);
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}

public sealed class EarlyOwnershipContext(DbContextOptions options, string tenant)
    : RelationalDocumentsContext(options, tenant)
{
    protected override bool RegisterOwnershipFirst => true;
}

public sealed class RelationalDocumentRow
{
    public Guid Id { get; set; }
    public string Tenant { get; set; } = "";
    public string Marker { get; set; } = "unchanged scalar";
    public bool Archived { get; set; }
    public TextValue Text { get; set; } = new("Original", null);
    public ReviewValue Review { get; set; } = new();
    public SelectionValue? Selection { get; set; }
    public SnapshotValue Snapshot { get; set; } = new("Original JSON");
    public List<ScaleValue> Defaults { get; set; } = [];
}

public sealed record TextValue(string Name, string? TechnologyContext);

public sealed class ReviewValue
{
    public string Status { get; set; } = "draft";
    public NotesValue Notes { get; set; } = new();
    public SelectionValue? SelectedType { get; set; }
}

public sealed class NotesValue
{
    public string Summary { get; set; } = "Original summary";
    public string? Comment { get; set; }
}

public sealed record SelectionValue(string Slug, int Revision);

public sealed record SnapshotValue(string Label);

public sealed record ScaleValue(string Key, string Label);
