using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gloaming.Messaging.EntityFrameworkCore.Postgres;

/// <summary>Supported PostgreSQL JSONB, database-clock and pending-index inbox mapping.</summary>
public static class PostgresInboxModelExtensions
{
    /// <summary>Maps a module-owned inbox; the consumer applies its native migration separately.</summary>
    public static EntityTypeBuilder<InboxMessageRecord> ConfigurePostgresInbox(
        this ModelBuilder model,
        string schema,
        string table
    )
    {
        var row = model.ConfigureInbox(schema, table);
        row.HasAnnotation("Gloaming:InboxProvider", "Postgres");
        row.Property(item => item.Payload).HasColumnType("jsonb");
        row.Property(item => item.ReceivedAt).HasDefaultValueSql("clock_timestamp()");
        row.Property(item => item.AvailableAt).HasDefaultValueSql("clock_timestamp()");
        row.HasIndex(item => new
            {
                item.SubscriptionKey,
                item.AvailableAt,
                item.ReceivedAt,
            })
            .HasFilter("processed_at IS NULL AND quarantined_at IS NULL");
        row.ToTable(
            table,
            schema,
            mapping =>
            {
                mapping.HasCheckConstraint("ck_inbox_schema", "schema_version > 0");
                mapping.HasCheckConstraint(
                    "ck_inbox_recovery_counts",
                    "revision >= 0 AND failure_count >= 0 AND failures_since_redrive >= 0 AND failures_since_redrive <= failure_count AND redrive_count >= 0"
                );
                mapping.HasCheckConstraint(
                    "ck_inbox_recovery_state",
                    "processed_at IS NULL OR quarantined_at IS NULL"
                );
                mapping.HasCheckConstraint(
                    "ck_inbox_failure_metadata",
                    "(failure_count = 0 AND last_failed_at IS NULL AND last_failure_code IS NULL AND quarantined_at IS NULL) OR (failure_count > 0 AND last_failed_at IS NOT NULL AND last_failure_code IS NOT NULL AND length(btrim(last_failure_code)) > 0)"
                );
                mapping.HasCheckConstraint(
                    "ck_inbox_redrive_metadata",
                    "(redrive_count = 0 AND last_redriven_at IS NULL) OR (redrive_count > 0 AND last_redriven_at IS NOT NULL)"
                );
            }
        );
        return row;
    }
}
