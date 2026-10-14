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
            .HasFilter("dispatched_at IS NULL");
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
            }
        );
        return row;
    }
}
