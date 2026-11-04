using Gloaming.Persistence.EntityFrameworkCore;
using JsonOwnershipConsumer;
using Microsoft.EntityFrameworkCore;

namespace Gloaming.EntityFrameworkCoreTests;

public sealed class UnsupportedMappingTests
{
    [Fact]
    public void InheritedOwnershipIsRejected() => AssertRejected(new InheritedContext(Options()));

    [Fact]
    public void SplitEntityOwnershipIsRejected() => AssertRejected(new SplitContext(Options()));

    [Fact]
    public void InlineComplexValueKeepsItsOrdinaryOwnerTableAndOwnershipToken()
    {
        using var context = new ComplexContext(Options());
        var entity = Assert.Single(context.Model.GetEntityTypes());
        Assert.Single(entity.GetComplexProperties());
        Assert.True(entity.FindProperty(nameof(RootRow.Owner))!.IsConcurrencyToken);
        context.Add(new RootRow { Id = Guid.NewGuid() });
        context.ValidateTenantChanges(() => Guid.Empty);
    }

    [Fact]
    public void JsonComplexValueKeepsItsOrdinaryOwnerTableAndOwnershipToken()
    {
        using var context = new JsonComplexContext(Options());
        var entity = context.Model.FindEntityType(typeof(RootRow))!;
        Assert.Single(entity.GetComplexProperties());
        Assert.Equal("json_roots", entity.GetTableName());
        Assert.True(entity.FindProperty(nameof(RootRow.Owner))!.IsConcurrencyToken);
    }

    [Fact]
    public void JsonAndInlineComplexValuesShareTheirOrdinaryOwnerTable()
    {
        using var context = new MixedComplexContext(Options());
        var entity = Assert.Single(context.Model.GetEntityTypes());
        Assert.Equal(2, entity.GetComplexProperties().Count());
        context.Add(new MixedRow { Id = Guid.NewGuid() });
        context.ValidateTenantChanges(() => Guid.Empty);
    }

    [Fact]
    public void OwnedJsonEntityMappingRemainsUnsupported() =>
        AssertRejected(new OwnedJsonContext(Options()));

    [Fact]
    public void JsonModelContainsOnlyOneEntityAndPreservesOwnershipFilters()
    {
        using var context = new JsonDocumentsContext(Options(), "alpha");
        var entity = Assert.Single(context.Model.GetEntityTypes());
        Assert.Equal(3, entity.GetComplexProperties().Count());
        Assert.All(
            entity.GetComplexProperties(),
            value => Assert.True(value.ComplexType.IsMappedToJson())
        );
        Assert.Equal(
            ["Archived", "TenantScope"],
            entity.GetDeclaredQueryFilters().Select(x => x.Key).Order().ToArray()
        );
    }

    [Fact]
    public void NativeEfStillRejectsReadOnlyComplexCollectionDeclarations()
    {
        using var context = new ReadOnlyCollectionContext(Options());
        var error = Assert.Throws<InvalidOperationException>(() => context.Model);
        Assert.IsNotType<TenantOwnershipException>(error);
        Assert.Contains("does not implement 'IList", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void RootJsonCollectionOnlyChangeTriggersTheOwnershipGuard()
    {
        using var context = new JsonDocumentsContext(Options(), "alpha");
        var row = new JsonDocumentRow
        {
            Id = Guid.NewGuid(),
            Tenant = "beta",
            Defaults = [new("basic", "Basic")],
        };
        context.Attach(row);
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        row.Defaults[0] = row.Defaults[0] with { Label = "Foreign edit" };
        row.Defaults.Add(new("advanced", "Advanced"));
        var error = Assert.Throws<TenantOwnershipException>(() =>
            context.ValidateTenantChanges(() => "alpha")
        );
        Assert.Equal(TenantOwnershipFailure.ForeignOwner, error.Reason);
    }

    [Fact]
    public void InlineComplexValuesCanBeConfiguredAfterOwnershipRegistration()
    {
        using var context = new LateComplexContext(Options());
        context.Add(new RootRow { Id = Guid.NewGuid() });
        context.ValidateTenantChanges(() => Guid.Empty);
    }

    [Fact]
    public void NativeEfStillRequiresBindableJsonConstructors()
    {
        using var context = new ConstructorContext(Options());
        var error = Assert.Throws<InvalidOperationException>(() => context.Model);
        Assert.IsNotType<TenantOwnershipException>(error);
        Assert.Contains("No suitable constructor", error.Message, StringComparison.Ordinal);
    }

    private static void AssertRejected(DbContext context)
    {
        using (context)
        {
            var failure = Assert.Throws<TenantOwnershipException>(() => context.Model);
            Assert.Equal(TenantOwnershipFailure.InvalidConfiguration, failure.Reason);
        }
    }

    private static DbContextOptions Options() =>
        new DbContextOptionsBuilder()
            .UseNpgsql("Host=localhost;Database=not_opened;Username=not_used;Password=not_used")
            .Options;

    private abstract class ShapeContext(DbContextOptions options) : DbContext(options)
    {
        private readonly Guid _owner = Guid.Empty;
        protected Guid RequiredOwner => _owner;
    }

    private sealed class InheritedContext(DbContextOptions options) : ShapeContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<DerivedRow>().HasBaseType<BaseRow>();
            modelBuilder.Entity<BaseRow>().HasKey(row => row.Id);
            modelBuilder
                .Entity<BaseRow>()
                .HasTenantOwnership(row => row.Owner, () => RequiredOwner, "Scope");
        }
    }

    private sealed class SplitContext(DbContextOptions options) : ShapeContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var row = modelBuilder.Entity<AccountReference>().ToTable("primary");
            row.SplitToTable("detail", table => table.Property(item => item.Label));
            row.HasTenantOwnership(item => item.WorkspaceKey, () => RequiredOwner, "Scope");
        }
    }

    private sealed class ComplexContext(DbContextOptions options) : ShapeContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var row = modelBuilder.Entity<RootRow>();
            row.ComplexProperty(item => item.Detail);
            row.HasTenantOwnership(item => item.Owner, () => RequiredOwner, "Scope");
        }
    }

    private sealed class JsonComplexContext(DbContextOptions options) : ShapeContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var row = modelBuilder.Entity<RootRow>().ToTable("json_roots");
            row.ComplexProperty(item => item.Detail, detail => detail.ToJson("ContentJson"));
            row.HasTenantOwnership(item => item.Owner, () => RequiredOwner, "Scope");
        }
    }

    private sealed class OwnedJsonContext(DbContextOptions options) : ShapeContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var row = modelBuilder.Entity<RootRow>().ToTable("owned_json");
            row.OwnsOne(item => item.Detail, detail => detail.ToJson("ContentJson"));
            row.HasTenantOwnership(item => item.Owner, () => RequiredOwner, "Scope");
        }
    }

    private sealed class MixedComplexContext(DbContextOptions options) : ShapeContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var row = modelBuilder.Entity<MixedRow>().ToTable("mixed");
            row.ComplexProperty(item => item.Json, detail => detail.ToJson("ContentJson"));
            row.ComplexProperty(item => item.Inline);
            row.HasTenantOwnership(item => item.Owner, () => RequiredOwner, "Scope");
        }
    }

    private sealed class ReadOnlyCollectionContext(DbContextOptions options) : ShapeContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var row = modelBuilder.Entity<ReadOnlyCollectionRow>().ToTable("readonly");
            row.ComplexProperty(
                item => item.Json,
                detail =>
                {
                    detail.ToJson("ContentJson");
                    detail.ComplexCollection(item => item.Levels);
                }
            );
            row.HasTenantOwnership(item => item.Owner, () => RequiredOwner, "Scope");
        }
    }

    private sealed class LateComplexContext(DbContextOptions options) : ShapeContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var row = modelBuilder.Entity<RootRow>().ToTable("late");
            row.HasTenantOwnership(item => item.Owner, () => RequiredOwner, "Scope");
            row.ComplexProperty(item => item.Detail);
        }
    }

    private sealed class ConstructorContext(DbContextOptions options) : ShapeContext(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var row = modelBuilder.Entity<ConstructorRow>().ToTable("constructor");
            row.ComplexProperty(
                item => item.Json,
                detail =>
                {
                    detail.ToJson("ContentJson");
                    detail.ComplexCollection(item => item.Levels);
                }
            );
            row.HasTenantOwnership(item => item.Owner, () => RequiredOwner, "Scope");
        }
    }

    private sealed class ConstructorRow
    {
        public Guid Id { get; set; }
        public Guid Owner { get; set; }
        public UnbindableValue Json { get; set; } = new([]);
    }

    private sealed record UnbindableValue(List<LevelValue> Levels);

    private sealed class MixedRow
    {
        public Guid Id { get; set; }
        public Guid Owner { get; set; }
        public DetailValue Json { get; set; } = new();
        public DetailValue Inline { get; set; } = new();
    }

    private sealed class ReadOnlyCollectionRow
    {
        public Guid Id { get; set; }
        public Guid Owner { get; set; }
        public ReadOnlyValue Json { get; set; } = new();
    }

    private sealed class ReadOnlyValue
    {
        public IReadOnlyList<LevelValue> Levels { get; init; } = [];
    }

    private class BaseRow
    {
        public Guid Id { get; set; }
        public Guid Owner { get; set; }
    }

    private sealed class DerivedRow : BaseRow
    {
        public int Extra { get; set; }
    }

    private sealed class RootRow
    {
        public Guid Id { get; set; }
        public Guid Owner { get; set; }
        public DetailValue Detail { get; set; } = new();
    }

    private sealed class DetailValue
    {
        public Guid Owner { get; set; }
    }
}
