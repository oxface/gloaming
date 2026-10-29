using System.Text.Json;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Agents.AI.Workflows.Checkpointing;
using Microsoft.EntityFrameworkCore;

namespace Gloaming.Agents.Workflows.EntityFrameworkCore;

/// <summary>Native immutable JSON checkpoints in a caller-owned EF transaction and serialized conversation.</summary>
public sealed class EfCheckpointStore<TContext> : ICheckpointStore<JsonElement>
    where TContext : DbContext
{
    private readonly TContext database;
    private readonly string partitionKey;
    private readonly string conversationKey;
    private readonly CancellationToken cancellationToken;

    /// <summary>Binds one operation's context, trusted scope and cancellation to MAF's tokenless store interface.</summary>
    public EfCheckpointStore(
        TContext database,
        string partitionKey,
        string conversationKey,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentException.ThrowIfNullOrWhiteSpace(partitionKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(conversationKey);
        if (partitionKey.Length > 200 || conversationKey.Length > 200)
            throw new ArgumentException("Storage keys must not exceed 200 characters.");

        this.database = database;
        this.partitionKey = partitionKey;
        this.conversationKey = conversationKey;
        this.cancellationToken = cancellationToken;
    }

    private IQueryable<AgentCheckpoint> Query =>
        database
            .Set<AgentCheckpoint>()
            .AsNoTracking()
            .Where(x => x.PartitionKey == partitionKey && x.ConversationKey == conversationKey);

    /// <inheritdoc />
    public async ValueTask<IEnumerable<CheckpointInfo>> RetrieveIndexAsync(
        string sessionId,
        CheckpointInfo? withParent = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        if (withParent is not null && withParent.SessionId != sessionId)
            throw new ArgumentException("Parent belongs to another session.", nameof(withParent));

        var ids = await Query
            .Where(x =>
                x.SessionId == sessionId
                && (withParent == null || x.ParentId == withParent.CheckpointId)
            )
            .OrderBy(x => x.Sequence)
            .Select(x => x.CheckpointId)
            .ToArrayAsync(cancellationToken);
        return ids.Select(id => new CheckpointInfo(sessionId, id)).ToArray();
    }

    /// <inheritdoc />
    public async ValueTask<CheckpointInfo> CreateCheckpointAsync(
        string sessionId,
        JsonElement value,
        CheckpointInfo? parent = null
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        if (value.ValueKind == JsonValueKind.Undefined)
            throw new ArgumentException("A defined checkpoint payload is required.", nameof(value));

        if (database.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "Checkpoint writes require the caller's transaction."
            );

        if (parent is not null)
            await RetrieveCheckpointAsync(sessionId, parent);

        var sequence =
            await Query
                .Where(x => x.SessionId == sessionId)
                .Select(x => (long?)x.Sequence)
                .MaxAsync(cancellationToken)
            ?? 0;
        var id = Guid.NewGuid().ToString("N");
        database.Add(
            new AgentCheckpoint
            {
                PartitionKey = partitionKey,
                ConversationKey = conversationKey,
                SessionId = sessionId,
                CheckpointId = id,
                ParentId = parent?.CheckpointId,
                Sequence = checked(sequence + 1),
                Payload = value.GetRawText(),
            }
        );
        await database.SaveChangesAsync(cancellationToken);
        return new(sessionId, id);
    }

    /// <inheritdoc />
    public async ValueTask<JsonElement> RetrieveCheckpointAsync(
        string sessionId,
        CheckpointInfo key
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);
        ArgumentNullException.ThrowIfNull(key);
        if (key.SessionId != sessionId)
            throw new KeyNotFoundException("The checkpoint is unavailable in this session.");

        var payload =
            await Query
                .Where(x => x.SessionId == sessionId && x.CheckpointId == key.CheckpointId)
                .Select(x => x.Payload)
                .SingleOrDefaultAsync(cancellationToken)
            ?? throw new KeyNotFoundException("The checkpoint is unavailable in this scope.");
        using var document = JsonDocument.Parse(payload);
        return document.RootElement.Clone();
    }
}
