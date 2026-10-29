namespace Gloaming.Agents.EntityFrameworkCore;

/// <summary>An immutable retained record; the consumer owns its schema, admission and transaction.</summary>
public sealed class AgentHistorySnapshot
{
    /// <summary>The retained PartitionKey value.</summary>
    public string PartitionKey { get; init; } = "";

    /// <summary>The retained ConversationKey value.</summary>
    public string ConversationKey { get; init; } = "";

    /// <summary>The retained AgentKey value.</summary>
    public string AgentKey { get; init; } = "";

    /// <summary>The retained SessionKey value.</summary>
    public string SessionKey { get; init; } = "";

    /// <summary>The retained Id value.</summary>
    public string Id { get; init; } = "";

    /// <summary>The retained ParentId value.</summary>
    public string? ParentId { get; init; }

    /// <summary>The retained Messages value.</summary>
    public string Messages { get; init; } = "";
}
