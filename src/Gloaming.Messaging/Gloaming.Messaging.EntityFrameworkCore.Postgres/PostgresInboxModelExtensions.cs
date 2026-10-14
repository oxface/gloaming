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
            .HasFilter("processed_at IS NULL");
        row.ToTable(
            table,
            schema,
            mapping => mapping.HasCheckConstraint("ck_inbox_schema", "schema_version > 0")
        );
        return row;
    }
}
