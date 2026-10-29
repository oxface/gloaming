using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Gloaming.Conversations.EntityFrameworkCore;

/// <summary>Explicit module-owned mapping and tracked-save validation.</summary>
public static class ConversationTranscriptModelExtensions
{
    /// <summary>Maps this capability independently into the consumer's schema.</summary>
    public static EntityTypeBuilder<ConversationEntry> ConfigureConversationTranscript(
        this ModelBuilder model,
        string schema,
        string table = "conversation_entries"
    )
    {
        ArgumentNullException.ThrowIfNull(model);
        ArgumentException.ThrowIfNullOrWhiteSpace(schema);
        ArgumentException.ThrowIfNullOrWhiteSpace(table);

        var row = model.Entity<ConversationEntry>();
        row.ToTable(table, schema);
        row.HasKey(x => new
        {
            x.PartitionKey,
            x.ConversationKey,
            x.Id,
        });
        row.Property(x => x.PartitionKey).HasMaxLength(200);
        row.Property(x => x.ConversationKey).HasMaxLength(200);
        row.HasIndex(x => new
            {
                x.PartitionKey,
                x.ConversationKey,
                x.Sequence,
            })
            .IsUnique();
        row.Property(x => x.Type).HasMaxLength(100);
        row.Property(x => x.Id).ValueGeneratedNever();
        row.Property(x => x.Payload).IsRequired();
        return row;
    }

    /// <summary>Rejects retained-record mutation and foreign-partition inserts in every native save path.</summary>
    public static void ValidateConversationTranscriptChanges(
        this DbContext database,
        string partitionKey
    )
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(partitionKey);

        database.ChangeTracker.DetectChanges();
        foreach (var entry in database.ChangeTracker.Entries<ConversationEntry>())
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
