using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gloaming.Agents.Workflows.EntityFrameworkCore;

/// <summary>Explicit module-owned mapping and tracked-save validation.</summary>
public static class AgentCheckpointsModelExtensions
{
    /// <summary>Maps this capability independently into the consumer's schema.</summary>
    public static EntityTypeBuilder<AgentCheckpoint> ConfigureAgentCheckpoints(
        this ModelBuilder model,
        string schema,
        string table = "agent_checkpoints"
    )
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        ArgumentException.ThrowIfNullOrWhiteSpace(table);

        var row = model.Entity<AgentCheckpoint>();
        row.ToTable(table, schema);
        row.HasKey(x => new
        {
            x.PartitionKey,
            x.ConversationKey,
            x.CheckpointId,
        });
        row.Property(x => x.PartitionKey).HasMaxLength(200);
        row.Property(x => x.ConversationKey).HasMaxLength(200);
        row.HasIndex(x => new
            {
                x.PartitionKey,
                x.ConversationKey,
                x.SessionId,
                x.Sequence,
            })
            .IsUnique();
        row.Property(x => x.Payload).IsRequired();
        return row;
    }

    /// <summary>Rejects retained-record mutation and foreign-partition inserts in every native save path.</summary>
    public static void ValidateAgentCheckpointsChanges(this DbContext database, string partitionKey)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(partitionKey);

        database.ChangeTracker.DetectChanges();
        foreach (var entry in database.ChangeTracker.Entries<AgentCheckpoint>())
        {
            if (entry.State is EntityState.Modified or EntityState.Deleted)
                throw new InvalidOperationException("Retained records are immutable.");

            if (
                entry.State == EntityState.Added
                && !string.Equals(entry.Entity.PartitionKey, partitionKey, StringComparison.Ordinal)
            )
                throw new InvalidOperationException("The record belongs to another partition.");
        }
    }
}
