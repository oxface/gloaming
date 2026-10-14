using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage;

namespace Gloaming.Auditing.EntityFrameworkCore;

/// <summary>Editable native relational mapping and explicit validation of tracked audit writes.</summary>
public static class AuditModelExtensions
{
    /// <summary>Maps one provided record per context; consumer supplies provider JSON mapping and ownership policy.</summary>
    public static EntityTypeBuilder<AuditRecord> ConfigureAudit(
        this ModelBuilder model,
        string schema,
        string table
    )
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        ArgumentException.ThrowIfNullOrWhiteSpace(table);

        var row = model.Entity<AuditRecord>();
        row.ToTable(table, schema);
        row.HasAnnotation("Gloaming:Audit", true);
        row.HasKey(item => item.Id);
        row.Property(item => item.Id).ValueGeneratedNever();
        row.Property(item => item.Id).HasColumnName("id");
        row.Property(item => item.OccurredAt).HasColumnName("occurred_at");
        row.Property(item => item.ActorKind).HasColumnName("actor_kind");
        row.Property(item => item.ActorKey).HasColumnName("actor_key");
        row.Property(item => item.InitiatorKind).HasColumnName("initiator_kind");
        row.Property(item => item.InitiatorKey).HasColumnName("initiator_key");
        row.Property(item => item.Source).HasColumnName("source").IsRequired();
        row.Property(item => item.Action).HasColumnName("action").IsRequired();
        row.Property(item => item.SubjectType).HasColumnName("subject_type").IsRequired();
        row.Property(item => item.SubjectKey).HasColumnName("subject_key");
        row.Property(item => item.Outcome).HasColumnName("outcome").IsRequired();
        row.Property(item => item.SchemaVersion).HasColumnName("schema_version");
        row.Property(item => item.Details).HasColumnName("details");
        row.Property(item => item.ReasonCode).HasColumnName("reason_code");
        row.Property(item => item.TenantKey).HasColumnName("tenant_key");
        row.HasIndex(item => new
            {
                item.TenantKey,
                item.SubjectType,
                item.SubjectKey,
                item.OccurredAt,
                item.Id,
            })
            .HasDatabaseName("ix_audit_subject_timeline");
        return row;
    }

    /// <summary>Call from both native save overrides. Does not guard raw SQL, bulk writes or omitted staging.</summary>
    public static void ValidateAuditChanges(this DbContext database)
    {
        ArgumentNullException.ThrowIfNull(database);

        database.ChangeTracker.DetectChanges();
        foreach (var tracked in database.ChangeTracker.Entries<AuditRecord>())
        {
            if (tracked.State is EntityState.Unchanged or EntityState.Detached)
                continue;

            if (tracked.State != EntityState.Added)
                throw new InvalidOperationException(
                    "Tracked audit records cannot be edited or deleted."
                );

            var registration = AuditStageRegistry.Find(database, tracked.Entity);
            var transaction = database.Database.CurrentTransaction;
            if (
                registration is null
                || transaction is null
                || !ReferenceEquals(registration.Transaction, transaction)
                || transaction.GetDbTransaction().Connection is null
            )
                throw new InvalidOperationException(
                    "Save audits in the active native transaction that staged them."
                );

            if (!tracked.Entity.Matches(registration.Entry))
                throw new InvalidOperationException("Do not change a staged audit envelope.");
        }
    }
}
