using Gloaming.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gloaming.Messaging.EntityFrameworkCore;

/// <summary>Explicit relational inbox mapping and tracked-save guard.</summary>
public static class InboxModelExtensions
{
    /// <summary>Maps one module-owned inbox; provider-specific JSON/time configuration is still required.</summary>
    public static EntityTypeBuilder<InboxMessageRecord> ConfigureInbox(
        this ModelBuilder model,
        string schema,
        string table
    )
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        ArgumentException.ThrowIfNullOrWhiteSpace(table);

        model.RequireGloamingCapability("inbox");
        var row = model.Entity<InboxMessageRecord>();
        row.ToTable(table, schema);
        row.HasAnnotation("Gloaming:Inbox", true);
        row.HasKey(item => new
        {
            item.SubscriptionKey,
            item.ProducerKey,
            item.MessageId,
        });
        row.Property(item => item.SubscriptionKey).HasColumnName("subscription_key");
        row.Property(item => item.ProducerKey).HasColumnName("producer_key");
        row.Property(item => item.MessageId).HasColumnName("message_id").ValueGeneratedNever();
        row.Property(item => item.MessageName).HasColumnName("message_name");
        row.Property(item => item.SchemaVersion).HasColumnName("schema_version");
        row.Property(item => item.Payload).HasColumnName("payload");
        row.Property(item => item.TenantKey).HasColumnName("tenant_key");
        row.Property(item => item.CorrelationId).HasColumnName("correlation_id");
        row.Property(item => item.CausationId).HasColumnName("causation_id");
        row.Property(item => item.TraceParent).HasColumnName("trace_parent");
        row.Property(item => item.TraceState).HasColumnName("trace_state");
        row.Property(item => item.ReceivedAt).HasColumnName("received_at").ValueGeneratedOnAdd();
        row.Property(item => item.AvailableAt).HasColumnName("available_at").ValueGeneratedOnAdd();
        row.Property(item => item.ProcessedAt).HasColumnName("processed_at");
        row.Property(item => item.Revision).HasColumnName("revision").HasDefaultValue(0L);
        row.Property(item => item.FailureCount).HasColumnName("failure_count").HasDefaultValue(0L);
        row.Property(item => item.FailuresSinceRedrive)
            .HasColumnName("failures_since_redrive")
            .HasDefaultValue(0L);
        row.Property(item => item.LastFailedAt).HasColumnName("last_failed_at");
        row.Property(item => item.LastFailureCode)
            .HasColumnName("last_failure_code")
            .HasMaxLength(128);
        row.Property(item => item.DeadLetteredAt).HasColumnName("quarantined_at");
        row.Property(item => item.RedriveCount).HasColumnName("redrive_count").HasDefaultValue(0L);
        row.Property(item => item.LastRedrivenAt).HasColumnName("last_redriven_at");
        row.HasIndex(item => new
        {
            item.SubscriptionKey,
            item.AvailableAt,
            item.ReceivedAt,
        });
        return row;
    }

    /// <summary>Call from both SaveChanges overrides. Rejects tracked lifecycle writes, not privileged SQL/bulk bypasses.</summary>
    public static void ValidateInboxChanges(this DbContext database)
    {
        ArgumentNullException.ThrowIfNull(database);
        database.ChangeTracker.DetectChanges();
        if (
            database
                .ChangeTracker.Entries<InboxMessageRecord>()
                .Any(entry =>
                    entry.State is EntityState.Added or EntityState.Modified or EntityState.Deleted
                )
        )
            throw new InvalidOperationException(
                "Inbox envelope/lifecycle writes belong to native intake and processing."
            );
    }
}
