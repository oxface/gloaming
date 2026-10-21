using Gloaming.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gloaming.Messaging.EntityFrameworkCore;

/// <summary>Explicit relational mapping and tracked-save validation for the provided outbox record.</summary>
public static class OutboxModelExtensions
{
    /// <summary>Registers one provided outbox record in this context at the supplied schema and table.</summary>
    /// <remarks>Provider-specific payload and database-time configuration is still required.</remarks>
    public static EntityTypeBuilder<OutboxMessageRecord> ConfigureOutbox(
        this ModelBuilder model,
        string schema,
        string table
    )
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        ArgumentException.ThrowIfNullOrWhiteSpace(table);

        model.RequireGloamingCapability("outbox");
        var row = model.Entity<OutboxMessageRecord>();
        row.ToTable(table, schema);
        row.HasAnnotation("Gloaming:Outbox", true);
        row.HasKey(item => item.MessageId);
        row.Property(item => item.MessageId).HasColumnName("message_id").ValueGeneratedNever();
        row.Property(item => item.RouteKey).HasColumnName("destination");
        row.Property(item => item.MessageName).HasColumnName("message_name");
        row.Property(item => item.SchemaVersion).HasColumnName("schema_version");
        row.Property(item => item.Payload).HasColumnName("payload");
        row.Property(item => item.TenantKey).HasColumnName("owner_key");
        row.Property(item => item.CorrelationId).HasColumnName("correlation_id");
        row.Property(item => item.CausationId).HasColumnName("causation_id");
        row.Property(item => item.TraceParent).HasColumnName("trace_parent");
        row.Property(item => item.TraceState).HasColumnName("trace_state");
        row.Property(item => item.QueuedAt).HasColumnName("queued_at").ValueGeneratedOnAdd();
        row.Property(item => item.AvailableAt).HasColumnName("available_at").ValueGeneratedOnAdd();
        row.Property(item => item.DispatchedAt).HasColumnName("dispatched_at");
        row.Property(item => item.LeaseToken).HasColumnName("lease_token");
        row.Property(item => item.LeaseUntil).HasColumnName("lease_until");
        row.Property(item => item.Attempts).HasColumnName("attempts");
        row.Property(item => item.Revision).HasColumnName("revision").HasDefaultValue(0L);
        row.Property(item => item.FailureCount).HasColumnName("failure_count").HasDefaultValue(0L);
        row.Property(item => item.FailuresSinceRedrive)
            .HasColumnName("failures_since_redrive")
            .HasDefaultValue(0L);
        row.Property(item => item.LastFailedAt).HasColumnName("last_failed_at");
        row.Property(item => item.LastFailureCode)
            .HasColumnName("last_failure_code")
            .HasMaxLength(128);
        row.Property(item => item.DeadLetteredAt).HasColumnName("dead_lettered_at");
        row.Property(item => item.RedriveCount).HasColumnName("redrive_count").HasDefaultValue(0L);
        row.Property(item => item.LastRedrivenAt).HasColumnName("last_redriven_at");
        row.HasIndex(item => new
        {
            item.AvailableAt,
            item.LeaseUntil,
            item.MessageId,
        });
        return row;
    }

    /// <summary>Call from both native SaveChanges overrides. Does not guard raw SQL or bulk writes.</summary>
    /// <remarks>
    /// Rejects edits/deletes and added records whose envelope or enqueue transaction changed.
    /// Native transaction atomicity works independently; these guards protect the supported tracked-write contract.
    /// </remarks>
    public static void ValidateOutboxChanges(this DbContext database)
    {
        ArgumentNullException.ThrowIfNull(database);

        database.ChangeTracker.DetectChanges();
        foreach (var entry in database.ChangeTracker.Entries<OutboxMessageRecord>())
        {
            if (entry.State is EntityState.Unchanged or EntityState.Detached)
                continue;
            if (entry.State != EntityState.Added)
                throw new InvalidOperationException(
                    "Tracked outbox envelopes and lifecycle cannot be edited or deleted."
                );
            var row = entry.Entity;
            var registration = OutboxEnqueueRegistry.Find(database, row);
            if (
                registration is null
                || !ReferenceEquals(registration.Transaction, database.Database.CurrentTransaction)
            )
                throw new InvalidOperationException(
                    "Save outgoing work in the native transaction that enqueued it."
                );
            var message = registration.Message;
            if (
                row.MessageId != message.MessageId
                || row.RouteKey != message.RouteKey
                || row.MessageName != message.MessageName
                || row.SchemaVersion != message.SchemaVersion
                || row.TenantKey != message.TenantKey
                || row.CorrelationId != message.CorrelationId
                || row.CausationId != message.CausationId
                || row.TraceParent != message.TraceParent
                || row.TraceState != message.TraceState
                || row.Payload.ValueKind == System.Text.Json.JsonValueKind.Undefined
                || row.Payload.GetRawText() != message.Payload.GetRawText()
                || row.QueuedAt != default
                || row.AvailableAt != default
                || row.DispatchedAt is not null
                || row.LeaseToken is not null
                || row.LeaseUntil is not null
                || row.Attempts != 0
                || row.Revision != 0
                || row.FailureCount != 0
                || row.FailuresSinceRedrive != 0
                || row.LastFailedAt is not null
                || row.LastFailureCode is not null
                || row.DeadLetteredAt is not null
                || row.RedriveCount != 0
                || row.LastRedrivenAt is not null
            )
                throw new InvalidOperationException(
                    "Do not change the enqueued envelope or assign its database-managed lifecycle."
                );
        }
    }
}
