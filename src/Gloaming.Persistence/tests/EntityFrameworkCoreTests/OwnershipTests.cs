using Gloaming.Persistence.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Gloaming.EntityFrameworkCoreTests;

public sealed class OwnershipTests
{
    private static readonly Guid Alpha = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Beta = Guid.Parse("22222222-2222-2222-2222-222222222222");

    [Fact]
    public void OwnershipRegistrationDeclaresItsEffectsWithoutResolvingTheTenant()
    {
        using var context = new GuidConsumerContext(Options(), null);
        IEntityType account = context.Model.FindEntityType(typeof(AccountReference))!;
        IProperty owner = account.FindProperty(nameof(AccountReference.WorkspaceKey))!;
        Assert.Equal(0, context.KeyReads);
        Assert.False(owner.IsNullable);
        Assert.True(owner.IsConcurrencyToken);
        Assert.Equal(ValueGenerated.Never, owner.ValueGenerated);
        Assert.Equal(PropertySaveBehavior.Save, owner.GetBeforeSaveBehavior());
        Assert.Equal(
            ["Archived", "IsolationBoundary"],
            account.GetDeclaredQueryFilters().Select(filter => filter.Key).Order().ToArray()
        );
    }

    [Theory]
    [InlineData(EntityState.Added)]
    [InlineData(EntityState.Modified)]
    [InlineData(EntityState.Deleted)]
    public void ForeignCurrentAndOriginalOwnershipIsRejected(EntityState state)
    {
        using var context = new GuidConsumerContext(Options(), Alpha);
        AccountReference row = Row(Beta);
        context.Entry(row).State = state;
        var failure = Assert.Throws<TenantOwnershipException>(() =>
            context.ValidateTenantChanges(() => Alpha)
        );
        Assert.Equal(TenantOwnershipFailure.ForeignOwner, failure.Reason);
        Assert.Equal(nameof(AccountReference.WorkspaceKey), failure.PropertyName);
        Assert.Equal(typeof(AccountReference).FullName, failure.EntityTypeName);
        Assert.Equal(Beta, row.WorkspaceKey);
    }

    [Theory]
    [InlineData(EntityState.Modified)]
    [InlineData(EntityState.Deleted)]
    public void ChangingOwnershipIsRejectedEvenWithAutomaticDetectionDisabled(EntityState state)
    {
        using var context = new GuidConsumerContext(Options(), Alpha);
        AccountReference row = Row(Alpha);
        context.Attach(row);
        context.ChangeTracker.AutoDetectChangesEnabled = false;
        row.WorkspaceKey = Beta;
        if (state == EntityState.Deleted)
            context.Remove(row);
        var failure = Assert.Throws<TenantOwnershipException>(() =>
            context.ValidateTenantChanges(() => Alpha)
        );
        Assert.Equal(TenantOwnershipFailure.OwnershipChanged, failure.Reason);
        Assert.False(context.ChangeTracker.AutoDetectChangesEnabled);
    }

    [Theory]
    [InlineData(EntityState.Modified)]
    [InlineData(EntityState.Deleted)]
    public void ForgedForeignOriginalOwnerIsRejected(EntityState state)
    {
        using var context = new GuidConsumerContext(Options(), Alpha);
        AccountReference row = Row(Alpha);
        context.Entry(row).State = state;
        context.Entry(row).Property(item => item.WorkspaceKey).OriginalValue = Beta;
        var failure = Assert.Throws<TenantOwnershipException>(() =>
            context.ValidateTenantChanges(() => Alpha)
        );
        Assert.Equal(TenantOwnershipFailure.OwnershipChanged, failure.Reason);
    }

    [Fact]
    public void GlobalOnlyOrUnchangedWritesDoNotResolveTenancy()
    {
        using var context = new GuidConsumerContext(Options(), null);
        context.Attach(Row(Alpha));
        context.Lookups.Add(new GlobalLookup { Id = Guid.NewGuid(), Label = "global" });
        context.ValidateTenantChanges<Guid>(() =>
            throw new InvalidOperationException("Must not resolve.")
        );
        Assert.Equal(0, context.KeyReads);
    }

    [Fact]
    public void OperationKeyIsResolvedOnceForMultipleProtectedWrites()
    {
        using var context = new GuidConsumerContext(Options(), Alpha);
        context.Accounts.AddRange(Row(Alpha), Row(Alpha));
        int resolutions = 0;
        context.ValidateTenantChanges(() =>
        {
            resolutions++;
            return Alpha;
        });
        Assert.Equal(1, resolutions);
    }

    [Fact]
    public void WrongStorageKeyTypeIsInvalidConfiguration()
    {
        using var context = new GuidConsumerContext(Options(), Alpha);
        context.Accounts.Add(Row(Alpha));
        var failure = Assert.Throws<TenantOwnershipException>(() =>
            context.ValidateTenantChanges(() => "alpha")
        );
        Assert.Equal(TenantOwnershipFailure.InvalidConfiguration, failure.Reason);
    }

    [Fact]
    public void RequirementReasonValuesStartAtOneAndReserveZero()
    {
        Assert.Equal(
            [1, 2, 3, 4],
            Enum.GetValues<TenantOwnershipFailure>().Select(reason => (int)reason)
        );
        Assert.False(Enum.IsDefined((TenantOwnershipFailure)0));
    }

    [Fact]
    public void DisablingTheNativeOwnershipTokenIsRejectedBeforeWrites()
    {
        using var context = new DisabledTokenContext(Options(), Alpha);
        AssertInvalidWrite(context);
    }

    [Fact]
    public void ReplacingTheNamedOwnershipFilterIsRejectedBeforeWrites()
    {
        using var context = new ReplacedFilterContext(Options(), Alpha);
        AssertInvalidWrite(context);
    }

    [Fact]
    public void ViewMappingIsRejectedBeforeWrites()
    {
        using var context = new ViewContext(Options(), Alpha);
        AssertInvalidWrite(context);
    }

    [Fact]
    public void CapturingAnOperationKeyAsAConstantIsRejectedDuringRegistration()
    {
        using var context = new CapturedKeyContext(Options(), Alpha);
        var failure = Assert.Throws<TenantOwnershipException>(() => context.Model);
        Assert.Equal(TenantOwnershipFailure.InvalidConfiguration, failure.Reason);
    }

    [Fact]
    public void DuplicateOwnershipRegistrationIsRejectedDuringRegistration()
    {
        using var context = new DuplicateOwnershipContext(Options(), Alpha);
        var failure = Assert.Throws<TenantOwnershipException>(() => context.Model);
        Assert.Equal(TenantOwnershipFailure.InvalidConfiguration, failure.Reason);
    }

    [Fact]
    public void ConverterOnOwnershipIsRejectedBeforeWrites()
    {
        using var context = new ConvertedOwnerContext(Options(), Alpha);
        AssertInvalidWrite(context);
    }

    [Fact]
    public void SharedTableMappingIsRejectedDuringRegistration()
    {
        using var context = new SharedTableContext(Options(), Alpha);
        var failure = Assert.Throws<TenantOwnershipException>(() => context.Model);
        Assert.Equal(TenantOwnershipFailure.InvalidConfiguration, failure.Reason);
    }

    [Fact]
    public void IgnoringSuppliedOwnershipDuringInsertIsRejectedBeforeWrites()
    {
        using var context = new IgnoredOwnerContext(Options(), Alpha);
        AssertInvalidWrite(context);
    }

    private static void AssertInvalidWrite(GuidConsumerContext context)
    {
        context.Accounts.Add(Row(Alpha));
        var failure = Assert.Throws<TenantOwnershipException>(() =>
            context.ValidateTenantChanges(() => Alpha)
        );
        Assert.Equal(TenantOwnershipFailure.InvalidConfiguration, failure.Reason);
    }

    private static AccountReference Row(Guid workspace) =>
        new()
        {
            Id = Guid.NewGuid(),
            WorkspaceKey = workspace,
            Label = "original",
        };

    private static DbContextOptions Options() =>
        new DbContextOptionsBuilder()
            .UseNpgsql("Host=localhost;Database=not_opened;Username=not_used;Password=not_used")
            .Options;

    private sealed class DisabledTokenContext(DbContextOptions options, Guid workspace)
        : GuidConsumerContext(options, workspace)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder
                .Entity<AccountReference>()
                .Property(row => row.WorkspaceKey)
                .IsConcurrencyToken(false);
        }
    }

    private sealed class ReplacedFilterContext(DbContextOptions options, Guid workspace)
        : GuidConsumerContext(options, workspace)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder
                .Entity<AccountReference>()
                .HasQueryFilter("IsolationBoundary", row => true);
        }
    }

    private sealed class ViewContext(DbContextOptions options, Guid workspace)
        : GuidConsumerContext(options, workspace)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder.Entity<AccountReference>().ToView("accounts_view", "custom_workspace");
        }
    }

    private sealed class CapturedKeyContext(DbContextOptions options, Guid workspace)
        : GuidConsumerContext(options, workspace)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            Guid captured = workspace;
            modelBuilder
                .Entity<AccountReference>()
                .HasTenantOwnership(row => row.WorkspaceKey, () => captured, "IsolationBoundary");
        }
    }

    private sealed class DuplicateOwnershipContext(DbContextOptions options, Guid workspace)
        : GuidConsumerContext(options, workspace)
    {
        private Guid Selected => workspace;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder
                .Entity<AccountReference>()
                .HasTenantOwnership(row => row.WorkspaceKey, () => Selected, "SecondScope");
        }
    }

    private sealed class ConvertedOwnerContext(DbContextOptions options, Guid workspace)
        : GuidConsumerContext(options, workspace)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder
                .Entity<AccountReference>()
                .Property(row => row.WorkspaceKey)
                .HasConversion<string>();
        }
    }

    private sealed class SharedTableContext(DbContextOptions options, Guid workspace)
        : GuidConsumerContext(options, workspace)
    {
        private Guid Selected => workspace;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<GlobalLookup>().ToTable("shared");
            var account = modelBuilder.Entity<AccountReference>().ToTable("shared");
            account.HasTenantOwnership(
                row => row.WorkspaceKey,
                () => Selected,
                "IsolationBoundary"
            );
        }
    }

    private sealed class IgnoredOwnerContext(DbContextOptions options, Guid workspace)
        : GuidConsumerContext(options, workspace)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder
                .Entity<AccountReference>()
                .Property(row => row.WorkspaceKey)
                .Metadata.SetBeforeSaveBehavior(PropertySaveBehavior.Ignore);
        }
    }
}
