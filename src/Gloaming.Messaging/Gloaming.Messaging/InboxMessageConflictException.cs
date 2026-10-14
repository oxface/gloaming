namespace Gloaming.Messaging;

/// <summary>A retained delivery identity was reused with incompatible envelope content.</summary>
public sealed class InboxMessageConflictException : Exception
{
    /// <summary>Identifies the conflicting delivery without exposing payload or tenant data.</summary>
    public InboxMessageConflictException(string subscriptionKey, string producerKey, Guid messageId)
        : base("An inbox delivery identity was reused with incompatible content.")
    {
        SubscriptionKey = subscriptionKey;
        ProducerKey = producerKey;
        MessageId = messageId;
    }

    /// <summary>The receiver's stable processing registration.</summary>
    public string SubscriptionKey { get; }

    /// <summary>The receiver-assigned producer namespace.</summary>
    public string ProducerKey { get; }

    /// <summary>The conflicting delivery identity.</summary>
    public Guid MessageId { get; }
}
