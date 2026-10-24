using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Gloaming.Samples.Messaging.Exports;

public sealed class ExportDbContext(DbContextOptions<ExportDbContext> options) : DbContext(options)
{
    public static void Configure(DbContextOptionsBuilder options, string connection) =>
        options.UseNpgsql(
            connection,
            postgres => postgres.MigrationsHistoryTable("__EFMigrationsHistory", "exports")
        );

    public static ExportDbContext Create(string connection)
    {
        var options = new DbContextOptionsBuilder<ExportDbContext>();
        Configure(options, connection);
        return new(options.Options);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ConfigurePostgresOutbox("exports", "outgoing_work");
        modelBuilder
            .Entity<OutboxMessageRecord>()
            .HasIndex(row => new
            {
                row.RouteKey,
                row.DispatchedAt,
                row.MessageId,
            })
            .HasDatabaseName("ix_exports_outbox_completed_retention")
            .HasFilter("dispatched_at IS NOT NULL");

        var request = modelBuilder.Entity<ExportRequest>();
        request.ToTable("requests", "exports");
        request.HasKey(row => row.Id);
        request.Property(row => row.Id).ValueGeneratedNever();
        request.Property(row => row.Version).IsConcurrencyToken();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        this.ValidateOutboxChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default
    )
    {
        this.ValidateOutboxChanges();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}

public sealed class ExportDesignTimeFactory : IDesignTimeDbContextFactory<ExportDbContext>
{
    public ExportDbContext CreateDbContext(string[] args) =>
        ExportDbContext.Create("Host=localhost;Database=not-opened;Username=not-used");
}
