using Gloaming.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Gloaming.EntityFrameworkCoreTests;

public sealed class UnsupportedMappingTests
{
    [Fact]
    public void InheritedOwnershipIsRejected() => AssertRejected(new InheritedContext(Options()));

    [Fact]
    public void SplitEntityOwnershipIsRejected() => AssertRejected(new SplitContext(Options()));

    [Fact]
    public void ComplexEntityMappingIsRejected() => AssertRejected(new ComplexContext(Options()));

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
