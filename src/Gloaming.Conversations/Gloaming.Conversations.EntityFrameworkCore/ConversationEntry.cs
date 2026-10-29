namespace Gloaming.Conversations.EntityFrameworkCore;

/// <summary>An immutable retained record; the consumer owns its schema, admission and transaction.</summary>
public sealed class ConversationEntry
{
    /// <summary>The retained PartitionKey value.</summary>
    public string PartitionKey { get; init; } = "";

    /// <summary>The retained ConversationKey value.</summary>
    public string ConversationKey { get; init; } = "";

    /// <summary>The retained Id value.</summary>
    public Guid Id { get; init; }

    /// <summary>The retained Sequence value.</summary>
    public long Sequence { get; init; }

    /// <summary>The retained Type value.</summary>
    public string Type { get; init; } = "";

    /// <summary>The retained SchemaVersion value.</summary>
    public int SchemaVersion { get; init; }

    /// <summary>The retained Payload value.</summary>
    public string Payload { get; init; } = "";

    /// <summary>The retained OccurredAt value.</summary>
    public DateTimeOffset OccurredAt { get; init; }
}
