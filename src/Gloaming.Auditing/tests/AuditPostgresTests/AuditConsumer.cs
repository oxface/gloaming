using System.Text.Json;
using Gloaming.ActorIdentity;
using Gloaming.Auditing.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Gloaming.Auditing.Tests;

// An ordinary tenantless EF consumer with its own business table and custom audit mapping.
// No module layout, Ownership, Events, Messaging, host or runtime initializer is needed.
internal sealed class AuditDatabase(DbContextOptions<AuditDatabase> options) : DbContext(options)
{
    internal static AuditDatabase Open(string connection) =>
        new(new DbContextOptionsBuilder<AuditDatabase>().UseNpgsql(connection).Options);

    protected override void OnModelCreating(ModelBuilder model)
    {
        var audit = model.ConfigureAudit("operations", "accepted_log");
        audit.Property(row => row.Details).HasColumnType("jsonb");
        audit.Property(row => row.Id).HasColumnName("audit_id");
        var business = model.Entity<BusinessRow>();
        business.ToTable(
            "documents",
            "operations",
            table => table.HasCheckConstraint("nonempty_name", "\"Name\" <> ''")
        );
        business.HasKey(row => row.Id);
        business.Property(row => row.Id).ValueGeneratedNever();
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        this.ValidateAuditChanges();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default
    )
    {
        this.ValidateAuditChanges();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}

internal sealed class BusinessRow
{
    public Guid Id { get; set; }
    public string Name { get; set; } = null!;
}

internal sealed class BareDatabase(DbContextOptions<BareDatabase> options) : DbContext(options);

internal sealed class AlternateAuditDatabase(DbContextOptions<AlternateAuditDatabase> options)
    : DbContext(options)
{
    protected override void OnModelCreating(ModelBuilder model)
    {
        var audit = model.ConfigureAudit("operations", "accepted_log");
        audit.Property(row => row.Details).HasColumnType("jsonb");
        audit.Property(row => row.Id).HasColumnName("audit_id");
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default
    )
    {
        this.ValidateAuditChanges();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }
}

internal static class AuditConsumer
{
    internal static AuditEntry Entry(
        Guid? id = null,
        ActorContext? attribution = null,
        string? subjectKey = "document:9",
        string? reasonCode = null
    ) =>
        new(
            id ?? Guid.NewGuid(),
            new DateTimeOffset(2026, 10, 9, 10, 30, 0, TimeSpan.FromHours(3)),
            attribution ?? new ActorContext(Actor.Human(new ActorId("person:7"))),
            "documents",
            "document.changed",
            "document",
            subjectKey,
            "accepted",
            2,
            JsonSerializer.SerializeToElement(new { Version = 4 }),
            reasonCode: reasonCode
        );
}
