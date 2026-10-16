using Gloaming.Persistence.EntityFrameworkCore;
using Gloaming.Persistence.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Gloaming.Persistence.Postgres.Tests;

public sealed class DeclarationTests
{
    [Fact]
    public void RlsRequiresOwnershipAndRejectsDuplicateSelectionWithoutChangingTheEfFilter()
    {
        using var probe = new Probe();
        var model = new ModelBuilder();
        var entity = model.Entity<Note>();
        entity.ToTable("notes");
        entity.Property(row => row.Partition);
        Assert.Throws<InvalidOperationException>(() => entity.WithRls(ProofDatabase.Setting));
        probe.Own(entity);
        entity.WithRls(ProofDatabase.Setting);
        Assert.Equal(nameof(Note.Partition), entity.Metadata.FindTenantOwnershipProperty()!.Name);
        Assert.Single(entity.Metadata.GetDeclaredQueryFilters());
        Assert.Throws<InvalidOperationException>(() => entity.WithRls(ProofDatabase.Setting));
    }

    [Fact]
    public void NonTextOwnershipNeedsASeparatelyProvenPolicyMapping()
    {
        using var probe = new Probe();
        var model = new ModelBuilder();
        var entity = model.Entity<Note>();
        entity.ToTable("notes");
        entity.Property(row => row.Id);
        probe.OwnGuid(entity);
        Assert.Throws<InvalidOperationException>(() => entity.WithRls(ProofDatabase.Setting));
    }

    [Theory]
    [InlineData("")]
    [InlineData("role")]
    [InlineData("app.Bad")]
    [InlineData("app.x'; SELECT 1")]
    public void BothDeclarationAndMigrationRejectUnsupportedSettingNames(string setting)
    {
        using var probe = new Probe();
        var model = new ModelBuilder();
        var entity = model.Entity<Note>();
        entity.ToTable("notes");
        entity.Property(row => row.Partition);
        probe.Own(entity);
        Assert.Throws<ArgumentException>(() => entity.WithRls(setting));
        Assert.Throws<ArgumentException>(() =>
            new MigrationBuilder("Npgsql.EntityFrameworkCore.PostgreSQL").EnableTenantRowSecurity(
                "notes",
                "partition",
                setting
            )
        );
    }

    [Fact]
    public async Task ConflictingTableSettingsFailBeforeOpeningOrResolvingTenancy()
    {
        var services = new ServiceCollection();
        services.AddPostgresTenantRowSecurity<ConflictingDatabase>();
        using var provider = services.BuildServiceProvider();
        using var connection = new NpgsqlConnection("Host=127.0.0.1;Port=1;Database=unused");
        var options = new DbContextOptionsBuilder<ConflictingDatabase>().UseNpgsql(connection);
        options.UseRls<ConflictingDatabase>(provider);
        await using var database = new ConflictingDatabase(options.Options);
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            database.Set<Note>().CountAsync(TestContext.Current.CancellationToken)
        );
        Assert.Contains("one authoritative RLS setting", failure.Message, StringComparison.Ordinal);
        Assert.Equal(System.Data.ConnectionState.Closed, connection.State);
    }

    [Fact]
    public void UnselectedModelDoesNotResolveOrRequireTenancy()
    {
        var services = new ServiceCollection();
        services.AddPostgresTenantRowSecurity<Probe>();
        using var provider = services.BuildServiceProvider();
        var options = new DbContextOptionsBuilder<Probe>().UseNpgsql(
            "Host=localhost;Database=unused"
        );
        options.UseRls<Probe>(provider);
        using var context = new Probe(options.Options);
        Assert.Empty(context.Model.GetEntityTypes());
    }

    private sealed class Probe : DbContext
    {
        internal Probe() { }

        internal Probe(DbContextOptions<Probe> options)
            : base(options) { }

        internal string Partition { get; } = "selected";
        internal Guid Key { get; } = Guid.NewGuid();

        internal void Own(EntityTypeBuilder<Note> entity) =>
            entity.HasTenantOwnership(row => row.Partition, () => Partition, "PartitionScope");

        internal void OwnGuid(EntityTypeBuilder<Note> entity) =>
            entity.HasTenantOwnership(row => row.Id, () => Key, "Scope");
    }

    private sealed class ConflictingDatabase(DbContextOptions<ConflictingDatabase> options)
        : DbContext(options)
    {
        private string Partition { get; } = "fixed";

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var first = modelBuilder.Entity<Note>();
            first.ToTable("first");
            first.HasKey(row => row.Id);
            first.Property(row => row.Partition);
            first
                .HasTenantOwnership(row => row.Partition, () => Partition, "Scope")
                .WithRls("first.tenant");
            var second = modelBuilder.Entity<OtherNote>();
            second.ToTable("second");
            second.HasKey(row => row.Id);
            second.Property(row => row.Partition);
            second
                .HasTenantOwnership(row => row.Partition, () => Partition, "Scope")
                .WithRls("second.tenant");
        }
    }

    private sealed class OtherNote
    {
        public Guid Id { get; set; }
        public string Partition { get; set; } = "";
    }
}
