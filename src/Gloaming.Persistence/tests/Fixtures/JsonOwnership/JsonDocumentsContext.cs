using Gloaming.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace JsonOwnershipConsumer;

// Independent consumer shapes: init-only records with EF-compatible constructors/collections.
public class JsonDocumentsContext(DbContextOptions options, string tenant) : DbContext(options)
{
    public string RequiredTenant => tenant;
    public DbSet<JsonDocumentRow> Documents => Set<JsonDocumentRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var row = modelBuilder.Entity<JsonDocumentRow>();
        row.ToTable("documents", "json_consumer");
        row.HasKey(x => x.Id);
        row.Property(x => x.Id).ValueGeneratedNever();
        row.Property(x => x.Tenant).HasColumnName("tenant_key");
        row.HasQueryFilter("Archived", x => !x.Archived);
        row.ComplexProperty(
            x => x.Content,
            content =>
            {
                content.ToJson("ContentJson");
                content.ComplexCollection(x => x.Levels);
            }
        );
        row.ComplexProperty(
            x => x.Draft,
            draft =>
            {
                draft.ToJson("DraftJson");
                draft.ComplexProperty(x => x.SelectedType).IsRequired(false);
                draft.ComplexCollection(x => x.References);
                draft.ComplexCollection(
                    x => x.Requirements,
                    requirements => requirements.Property(x => x.Priority).HasConversion<string>()
                );
            }
        );
        row.ComplexCollection(x => x.Defaults).ToJson("DefaultsJson");
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

public sealed class JsonDocumentRow
{
    public Guid Id { get; set; }
    public string Tenant { get; set; } = "";
    public string Marker { get; set; } = "unchanged scalar";
    public bool Archived { get; set; }
    public ContentValue Content { get; set; } = new("Original", [new("basic", "Original level")]);
    public DraftValue Draft { get; set; } = new("Draft", null, [], [new("basic", Priority.High)]);

    // EF populates an existing collection on reload; seed defaults explicitly on creation.
    public List<ScaleValue> Defaults { get; set; } = [];
}

public sealed record ContentValue(string Name, List<LevelValue> Levels)
{
    // EF 10 cannot constructor-bind complex collections; native materialization uses this path.
    private ContentValue()
        : this("", []) { }
}

public sealed record LevelValue(string Key, string Description);

public sealed record SelectionValue(string Slug, int Revision);

public sealed record ReferenceValue(string Slug, int Revision);

public sealed record RequirementValue(string Key, Priority? Priority);

public sealed record DraftValue(
    string Title,
    SelectionValue? SelectedType,
    List<ReferenceValue> References,
    List<RequirementValue> Requirements
)
{
    private DraftValue()
        : this("", null, [], []) { }
}

public sealed record ScaleValue(string Key, string Label);

public enum Priority
{
    Normal = 1,
    High = 2,
}
