using Gloaming.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Gloaming.EntityFrameworkCoreTests;

public sealed class MappingBoundaryTests
{
    [Theory]
    [InlineData(GraphShape.OwnedReference)]
    [InlineData(GraphShape.OwnedCollection)]
    [InlineData(GraphShape.Inheritance)]
    [InlineData(GraphShape.Tpt)]
    [InlineData(GraphShape.Tpc)]
    public void NativeEfAcceptsTheExcludedGraph(GraphShape shape)
    {
        using var context = new GraphContext(Options(), shape, protect: false);
        Assert.Equal(2, context.Model.GetEntityTypes().Count());
    }

    [Theory]
    [InlineData(GraphShape.OwnedReference, false)]
    [InlineData(GraphShape.OwnedReference, true)]
    [InlineData(GraphShape.OwnedCollection, false)]
    [InlineData(GraphShape.OwnedCollection, true)]
    [InlineData(GraphShape.Inheritance, false)]
    [InlineData(GraphShape.Inheritance, true)]
    [InlineData(GraphShape.Tpt, false)]
    [InlineData(GraphShape.Tpt, true)]
    [InlineData(GraphShape.Tpc, false)]
    [InlineData(GraphShape.Tpc, true)]
    public void ExcludedGraphCannotEscapeThroughAChangedDependentOrDerivedEntry(
        GraphShape shape,
        bool ownershipFirst
    )
    {
        using var context = new GraphContext(Options(), shape, protect: true, ownershipFirst);
        var failure = Assert.Throws<TenantOwnershipException>(() =>
        {
            GraphRow row = shape is GraphShape.Inheritance or GraphShape.Tpt or GraphShape.Tpc
                ? new DerivedRow()
                : new GraphRow();
            row.Tenant = "beta";
            if (shape == GraphShape.OwnedReference)
                row.Detail = new();
            else if (shape == GraphShape.OwnedCollection)
                row.Children = [new()];

            context.Attach(row);
            context.ChangeTracker.AutoDetectChangesEnabled = false;
            if (shape == GraphShape.OwnedReference)
                row.Detail!.Label = "foreign child edit";
            else if (shape == GraphShape.OwnedCollection)
                row.Children[0].Label = "foreign child edit";
            else
                row.Label = "foreign derived edit";

            context.ValidateTenantChanges(() => "alpha");
        });
        Assert.Equal(TenantOwnershipFailure.InvalidConfiguration, failure.Reason);
    }

    private static DbContextOptions Options() =>
        new DbContextOptionsBuilder()
            .UseNpgsql("Host=localhost;Database=not_opened;Username=not_used;Password=not_used")
            .EnableServiceProviderCaching(false)
            .Options;

    public enum GraphShape
    {
        OwnedReference = 1,
        OwnedCollection = 2,
        Inheritance = 3,
        Tpt = 4,
        Tpc = 5,
    }

    private sealed class GraphContext(
        DbContextOptions options,
        GraphShape shape,
        bool protect,
        bool ownershipFirst = false
    ) : DbContext(options)
    {
        private readonly string _tenant = "alpha";
        private string RequiredTenant => _tenant;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var row = modelBuilder.Entity<GraphRow>().ToTable("graph_roots");
            row.HasKey(x => x.Id);
            row.Ignore(x => x.Detail);
            row.Ignore(x => x.Children);
            if (protect && ownershipFirst)
                row.HasTenantOwnership(x => x.Tenant, () => RequiredTenant, "Scope");

            switch (shape)
            {
                case GraphShape.OwnedReference:
                    row.OwnsOne(x => x.Detail, detail => detail.ToTable("graph_details"));
                    break;
                case GraphShape.OwnedCollection:
                    row.OwnsMany(
                        x => x.Children,
                        children =>
                        {
                            children.ToTable("graph_children");
                            children.HasKey(x => x.Id);
                            children.Property(x => x.Id).ValueGeneratedNever();
                        }
                    );
                    break;
                case GraphShape.Inheritance:
                    modelBuilder.Entity<DerivedRow>().HasBaseType<GraphRow>();
                    break;
                case GraphShape.Tpt:
                    row.UseTptMappingStrategy();
                    modelBuilder
                        .Entity<DerivedRow>()
                        .HasBaseType<GraphRow>()
                        .ToTable("graph_derived");
                    break;
                case GraphShape.Tpc:
                    row.UseTpcMappingStrategy();
                    modelBuilder
                        .Entity<DerivedRow>()
                        .HasBaseType<GraphRow>()
                        .ToTable("graph_derived");
                    break;
                default:
                    throw new InvalidOperationException("Unknown graph shape.");
            }

            if (protect && !ownershipFirst)
                row.HasTenantOwnership(x => x.Tenant, () => RequiredTenant, "Scope");
        }
    }

    private class GraphRow
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Tenant { get; set; } = "";
        public string Label { get; set; } = "Original";
        public DetailRow? Detail { get; set; }
        public List<ChildRow> Children { get; set; } = [];
    }

    private sealed class DerivedRow : GraphRow
    {
        public string Extra { get; set; } = "Original";
    }

    private sealed class DetailRow
    {
        public string Label { get; set; } = "Original";
    }

    private sealed class ChildRow
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public string Label { get; set; } = "Original";
    }
}
