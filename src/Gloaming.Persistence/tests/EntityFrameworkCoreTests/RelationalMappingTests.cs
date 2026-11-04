using System.Linq.Expressions;
using Gloaming.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using RelationalOwnershipConsumer;

namespace Gloaming.EntityFrameworkCoreTests;

public sealed class RelationalMappingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NestedOnlyForeignWriteIsDetectedForEitherRegistrationOrder(bool ownershipFirst)
    {
        using RelationalDocumentsContext context = ownershipFirst
            ? new EarlyOwnershipContext(Options(), "alpha")
            : new RelationalDocumentsContext(Options(), "alpha");
        var row = new RelationalDocumentRow { Id = Guid.NewGuid(), Tenant = "beta" };
        context.Attach(row);
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        row.Review.Notes.Summary = "foreign nested change";
        var failure = Assert.Throws<TenantOwnershipException>(() =>
            context.ValidateTenantChanges(() => "alpha")
        );
        Assert.Equal(TenantOwnershipFailure.ForeignOwner, failure.Reason);
        Assert.Equal(EntityState.Modified, context.Entry(row).State);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }

    [Theory]
    [InlineData(ModelChange.RemovedFilter)]
    [InlineData(ModelChange.ReplacedFilter)]
    [InlineData(ModelChange.DisabledToken)]
    [InlineData(ModelChange.View)]
    [InlineData(ModelChange.Split)]
    [InlineData(ModelChange.SharedTable)]
    [InlineData(ModelChange.OwnedJson)]
    public void AlteredInlineModelIsRejectedBeforeProtectedWrites(ModelChange change)
    {
        // Each variant owns a model source; no mutable test parameter enters a shared model cache.
        using var context = new AlteredContext(Options(), change);
        context.Add(new RelationalDocumentRow { Id = Guid.NewGuid(), Tenant = "alpha" });
        var failure = Assert.Throws<TenantOwnershipException>(() =>
            context.ValidateTenantChanges(() => "alpha")
        );
        Assert.Equal(TenantOwnershipFailure.InvalidConfiguration, failure.Reason);
    }

    [Fact]
    public void OwnershipCannotMoveIntoAComplexMember()
    {
        using var context = new NestedOwnerContext(Options());
        var failure = Assert.Throws<TenantOwnershipException>(() => context.Model);
        Assert.Equal(TenantOwnershipFailure.InvalidConfiguration, failure.Reason);
        Assert.Contains("directly mapped CLR property", failure.Message, StringComparison.Ordinal);
    }

    private static DbContextOptions Options() =>
        new DbContextOptionsBuilder()
            .UseNpgsql("Host=localhost;Database=not_opened;Username=not_used;Password=not_used")
            .EnableServiceProviderCaching(false)
            .Options;

    public enum ModelChange
    {
        RemovedFilter = 1,
        ReplacedFilter = 2,
        DisabledToken = 3,
        View = 4,
        Split = 5,
        SharedTable = 6,
        OwnedJson = 7,
    }

    private sealed class AlteredContext(DbContextOptions options, ModelChange change)
        : RelationalDocumentsContext(options, "alpha")
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            var row = modelBuilder.Entity<RelationalDocumentRow>();
            switch (change)
            {
                case ModelChange.RemovedFilter:
                    row.HasQueryFilter(
                        "TenantScope",
                        (Expression<Func<RelationalDocumentRow, bool>>?)null
                    );
                    break;
                case ModelChange.ReplacedFilter:
                    row.HasQueryFilter("TenantScope", x => true);
                    break;
                case ModelChange.DisabledToken:
                    row.Property(x => x.Tenant).IsConcurrencyToken(false);
                    break;
                case ModelChange.View:
                    row.ToView("document_view", "inline_consumer");
                    break;
                case ModelChange.Split:
                    row.SplitToTable(
                        "document_details",
                        "inline_consumer",
                        table => table.Property(x => x.Marker)
                    );
                    break;
                case ModelChange.SharedTable:
                    var partner = modelBuilder
                        .Entity<TablePartner>()
                        .ToTable("documents", "inline_consumer");
                    partner
                        .HasOne<RelationalDocumentRow>()
                        .WithOne()
                        .HasForeignKey<TablePartner>(x => x.Id);
                    partner
                        .Property<string>("Tenant")
                        .HasColumnName("tenant_key")
                        .IsConcurrencyToken();
                    break;
                case ModelChange.OwnedJson:
                    row.Ignore(x => x.Snapshot);
                    row.OwnsOne(x => x.Snapshot, snapshot => snapshot.ToJson("SnapshotJson"));
                    break;
                default:
                    throw new InvalidOperationException("Unknown model change.");
            }
        }
    }

    private sealed class TablePartner
    {
        public Guid Id { get; set; }
    }

    private sealed class NestedOwnerContext(DbContextOptions options) : DbContext(options)
    {
        private readonly string _tenant = "alpha";
        public string RequiredTenant => _tenant;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var row = modelBuilder.Entity<RelationalDocumentRow>();
            row.Ignore(x => x.Review);
            row.Ignore(x => x.Selection);
            row.Ignore(x => x.Snapshot);
            row.Ignore(x => x.Defaults);
            row.ComplexProperty(x => x.Text);
            row.HasTenantOwnership(x => x.Text.Name, () => RequiredTenant, "TenantScope");
        }
    }
}
