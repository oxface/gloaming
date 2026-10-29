using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gloaming.Agents.EntityFrameworkCore;

/// <summary>Explicit module-owned mapping and tracked-save validation.</summary>
public static class AgentHistoryModelExtensions
{
    /// <summary>Maps this capability independently into the consumer's schema.</summary>
    public static EntityTypeBuilder<AgentHistorySnapshot> ConfigureAgentHistory(
        this ModelBuilder model,
        string schema,
        string table = "agent_history"
    )
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        ArgumentException.ThrowIfNullOrWhiteSpace(table);

        var row = model.Entity<AgentHistorySnapshot>();
        row.ToTable(table, schema);
        row.HasKey(x => new
        {
            x.PartitionKey,
            x.ConversationKey,
            x.Id,
        });
        row.Property(x => x.PartitionKey).HasMaxLength(200);
        row.Property(x => x.ConversationKey).HasMaxLength(200);
        row.Property(x => x.AgentKey).HasMaxLength(200);
        row.Property(x => x.SessionKey).HasMaxLength(200);
        row.Property(x => x.Messages).IsRequired();
        return row;
    }

    /// <summary>Rejects retained-record mutation and foreign-partition inserts in every native save path.</summary>
    public static void ValidateAgentHistoryChanges(this DbContext database, string partitionKey)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(partitionKey);

        database.ChangeTracker.DetectChanges();
        foreach (var entry in database.ChangeTracker.Entries<AgentHistorySnapshot>())
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
