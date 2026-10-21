using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gloaming.Messaging.EntityFrameworkCore.Postgres;

/// <summary>Native PostgreSQL specialization of the provided outbox model.</summary>
public static class PostgresOutboxModelExtensions
{
    /// <summary>Registers the outbox with JSONB, database-clock defaults, constraints and a pending-row index.</summary>
    /// <remarks>Call during OnModelCreating and apply the consumer-owned native migration separately.</remarks>
    public static EntityTypeBuilder<OutboxMessageRecord> ConfigurePostgresOutbox(
        this ModelBuilder model,
        string schema,
        string table
    )
    {
        var row = model.ConfigureOutbox(schema, table);
        row.HasAnnotation("Gloaming:OutboxProvider", "Postgres");
        row.Property(item => item.Payload).HasColumnType("jsonb");
        row.Property(item => item.QueuedAt).HasDefaultValueSql("clock_timestamp()");
        row.Property(item => item.AvailableAt).HasDefaultValueSql("clock_timestamp()");
        // Completed records are retained, but should leave the candidate index immediately.
        row.HasIndex(item => new
            {
                item.AvailableAt,
                item.LeaseUntil,
                item.MessageId,
            })
            .HasFilter("dispatched_at IS NULL AND dead_lettered_at IS NULL");
        row.HasIndex(item => new
            {
                item.RouteKey,
                item.DeadLetteredAt,
                item.MessageId,
            })
            .HasFilter("dispatched_at IS NULL AND dead_lettered_at IS NOT NULL");
        row.ToTable(
            table,
            schema,
            mapping =>
            {
                mapping.HasCheckConstraint("ck_outbox_schema", "schema_version > 0");
                mapping.HasCheckConstraint("ck_outbox_attempts", "attempts >= 0");
                mapping.HasCheckConstraint(
                    "ck_outbox_lease",
                    "(lease_token IS NULL) = (lease_until IS NULL)"
                );
                mapping.HasCheckConstraint(
                    "ck_outbox_recovery_counts",
                    "revision >= 0 AND failure_count >= 0 AND failures_since_redrive >= 0 AND failures_since_redrive <= failure_count AND failure_count <= attempts AND redrive_count >= 0"
                );
                mapping.HasCheckConstraint(
                    "ck_outbox_recovery_state",
                    "(dispatched_at IS NULL OR dead_lettered_at IS NULL) AND ((dispatched_at IS NULL AND dead_lettered_at IS NULL) OR lease_token IS NULL)"
                );
                mapping.HasCheckConstraint(
                    "ck_outbox_failure_metadata",
                    "(failure_count = 0 AND last_failed_at IS NULL AND last_failure_code IS NULL AND dead_lettered_at IS NULL) OR (failure_count > 0 AND last_failed_at IS NOT NULL AND last_failure_code IS NOT NULL AND length(btrim(last_failure_code)) > 0)"
                );
                mapping.HasCheckConstraint(
                    "ck_outbox_redrive_metadata",
                    "(redrive_count = 0 AND last_redriven_at IS NULL) OR (redrive_count > 0 AND last_redriven_at IS NOT NULL)"
                );
            }
        );
        return row;
    }
}
