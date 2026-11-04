using System.Data;
using Gloaming.EntityFrameworkCore;
using Gloaming.Persistence.EntityFrameworkCore;
using Gloaming.Persistence.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Gloaming.Persistence.Postgres.Tests;

public sealed class MappingBoundaryTests
{
    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(false, true, false)]
    [InlineData(false, true, true)]
    [InlineData(true, false, false)]
    [InlineData(true, false, true)]
    [InlineData(true, true, false)]
    [InlineData(true, true, true)]
    public async Task RlsRejectsSeparateOwnedTablesBeforeConnectionOrTenantResolution(
        bool collection,
        bool selectRlsFirst,
        bool asynchronous
    )
    {
        var services = new ServiceCollection();
        // Deliberately omit the base guard: the RLS adapter must enforce its own model boundary.
        services.TryAddGloamingCapability<GraphContext>(
            "tenant-ownership",
            CapabilityPhase.Validate,
            (_, _) => { }
        );
        services.AddPostgresTenantRowSecurity<GraphContext>();
        using var provider = services.BuildServiceProvider();
        using var connection = new NpgsqlConnection("Host=127.0.0.1;Port=1;Database=unused");
        var options = new DbContextOptionsBuilder()
            .UseNpgsql(connection)
            .EnableServiceProviderCaching(false);
        options.UseRls<GraphContext>(provider);
        await using var context = new GraphContext(options.Options, collection, selectRlsFirst);
        var failure = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            asynchronous
                ? context.SaveChangesAsync(TestContext.Current.CancellationToken)
                : Task.FromResult(context.SaveChanges())
        );
        Assert.Contains(
            "RLS requires HasTenantOwnership",
            failure.Message,
            StringComparison.Ordinal
        );
        Assert.Equal(ConnectionState.Closed, connection.State);
    }

    private sealed class GraphContext(
        DbContextOptions options,
        bool collection,
        bool selectRlsFirst
    ) : DbContext(options)
    {
        private readonly string _tenant = "alpha";
        private string RequiredTenant => _tenant;

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            var row = modelBuilder.Entity<GraphRow>().ToTable("graph_roots");
            row.Ignore(x => x.Detail);
            row.Ignore(x => x.Children);
            row.HasTenantOwnership(x => x.Tenant, () => RequiredTenant, "Scope");
            if (selectRlsFirst)
                row.WithRls("graph.tenant");

            if (collection)
                row.OwnsMany(
                    x => x.Children,
                    children =>
                    {
                        children.ToTable("graph_children");
                        children.HasKey(x => x.Id);
                        children.Property(x => x.Id).ValueGeneratedNever();
                    }
                );
            else
                row.OwnsOne(x => x.Detail, detail => detail.ToTable("graph_details"));

            if (!selectRlsFirst)
                row.WithRls("graph.tenant");
        }
    }

    private sealed class GraphRow
    {
        public Guid Id { get; set; }
        public string Tenant { get; set; } = "";
        public DetailRow? Detail { get; set; }
        public List<ChildRow> Children { get; set; } = [];
    }

    private sealed class DetailRow
    {
        public string Label { get; set; } = "Original";
    }

    private sealed class ChildRow
    {
        public Guid Id { get; set; }
        public string Label { get; set; } = "Original";
    }
}
