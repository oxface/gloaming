namespace Gloaming.Agents.Workflows.EntityFrameworkCore;

/// <summary>An immutable retained record; the consumer owns its schema, admission and transaction.</summary>
public sealed class AgentCheckpoint
{
    /// <summary>The retained PartitionKey value.</summary>
    public string PartitionKey { get; init; } = "";

    /// <summary>The retained ConversationKey value.</summary>
    public string ConversationKey { get; init; } = "";

    /// <summary>The retained SessionId value.</summary>
    public string SessionId { get; init; } = "";

    /// <summary>The retained CheckpointId value.</summary>
    public string CheckpointId { get; init; } = "";

    /// <summary>The retained ParentId value.</summary>
    public string? ParentId { get; init; }

    /// <summary>The retained Sequence value.</summary>
    public long Sequence { get; init; }

    /// <summary>The retained Payload value.</summary>
    public string Payload { get; init; } = "";
}
