using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Gloaming.Samples.Messaging.Rendering;

public sealed class RenderDbContext(DbContextOptions<RenderDbContext> options) : DbContext(options)
{
    public static void Configure(DbContextOptionsBuilder options, string connection) =>
        options.UseNpgsql(
            connection,
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "rendering")
        );

    public static RenderDbContext Create(string connection)
    {
        var options = new DbContextOptionsBuilder<RenderDbContext>();
        Configure(options, connection);
        return new(options.Options);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder
            .ConfigurePostgresInbox("rendering", "incoming_work")
            .HasIndex(row => new
            {
                row.SubscriptionKey,
                row.ProducerKey,
                row.ProcessedAt,
                row.MessageId,
            })
            .HasFilter("processed_at IS NOT NULL AND quarantined_at IS NULL");
        var job = modelBuilder.Entity<RenderJob>();
        job.ToTable(
            "jobs",
            "rendering",
            table => table.HasCheckConstraint("ck_job_pages", "\"Pages\" > 0")
        );
        job.HasKey(row => row.ExportRequestId);
        job.Property(row => row.ExportRequestId).ValueGeneratedNever();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        this.ValidateInboxChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default
    )
    {
        this.ValidateInboxChanges();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}

public sealed class RenderDesignTimeFactory : IDesignTimeDbContextFactory<RenderDbContext>
{
    public RenderDbContext CreateDbContext(string[] args) =>
        RenderDbContext.Create("Host=localhost;Database=not-opened;Username=not-used");
}
