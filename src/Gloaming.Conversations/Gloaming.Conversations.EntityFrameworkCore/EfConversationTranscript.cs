using System.Text.Json;
using Microsoft.EntityFrameworkCore;

namespace Gloaming.Conversations.EntityFrameworkCore;

/// <summary>Ordered visible entries independent of agent working context. Requires serialized conversation writes.</summary>
public sealed class EfConversationTranscript<TContext>
    where TContext : DbContext
{
    private readonly TContext database;
    private readonly string partitionKey;
    private readonly string conversationKey;

    /// <summary>Binds the store to one admitted consumer scope and native context.</summary>
    public EfConversationTranscript(TContext database, string partitionKey, string conversationKey)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(partitionKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationKey);
        if (partitionKey.Length > 200 || conversationKey.Length > 200)
            throw new ArgumentException("Storage keys must not exceed 200 characters.");

        this.database = database;
        this.partitionKey = partitionKey;
        this.conversationKey = conversationKey;
    }

    private IQueryable<ConversationEntry> Query =>
        database
            .Set<ConversationEntry>()
            .AsNoTracking()
            .Where(x => x.PartitionKey == partitionKey && x.ConversationKey == conversationKey);

    /// <summary>Reads a bounded oldest-first page after an exclusive sequence cursor.</summary>
    public async Task<IReadOnlyList<ConversationEntry>> ReadAsync(
        long afterSequence = 0,
        int maxCount = 100,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentOutOfRangeException.ThrowIfNegative(afterSequence);
        if (maxCount is < 1 or > 1000)
            throw new ArgumentOutOfRangeException(nameof(maxCount));

        return await Query
            .Where(x => x.Sequence > afterSequence)
            .OrderBy(x => x.Sequence)
            .Take(maxCount)
            .ToArrayAsync(cancellationToken);
    }

    /// <summary>Appends consumer-serialized payload with explicit identity/version. Flushes all tracked changes without committing.</summary>
    public async Task<ConversationEntry> AppendAsync(
        Guid id,
        string type,
        int schemaVersion,
        JsonElement payload,
        DateTimeOffset occurredAt,
        CancellationToken cancellationToken = default
    )
    {
        if (id == Guid.Empty)
            throw new ArgumentException("An entry ID is required.", nameof(id));

        ArgumentException.ThrowIfNullOrWhiteSpace(type);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(schemaVersion);
        if (type.Length > 100 || payload.ValueKind == JsonValueKind.Undefined)
            throw new ArgumentException("A supported type and defined JSON payload are required.");

        if (database.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "Transcript writes require the caller's transaction."
            );

        var sequence = await Query.Select(x => (long?)x.Sequence).MaxAsync(cancellationToken) ?? 0;
        var entry = new ConversationEntry
        {
            PartitionKey = partitionKey,
            ConversationKey = conversationKey,
            Id = id,
            Sequence = checked(sequence + 1),
            Type = type,
            SchemaVersion = schemaVersion,
            Payload = payload.GetRawText(),
            OccurredAt = occurredAt,
        };
        database.Add(entry);
        await database.SaveChangesAsync(cancellationToken);
        return entry;
    }
}
