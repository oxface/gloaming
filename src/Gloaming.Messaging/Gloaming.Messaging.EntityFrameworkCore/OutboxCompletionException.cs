namespace Gloaming.Messaging.EntityFrameworkCore;

/// <summary>The publisher returned normally, but outbox completion was not confirmed.</summary>
/// <remarks>A missing commit response may leave the message completed. Inspect fresh storage.</remarks>
public sealed class OutboxCompletionException : Exception
{
    /// <summary>Preserves message identity and the original completion error without retaining the envelope.</summary>
    public OutboxCompletionException(Guid messageId, Exception completionFailure)
        : base("Outbox completion was not confirmed after publication.", completionFailure)
    {
        if (messageId == Guid.Empty)
            throw new ArgumentException("A message identity is required.", nameof(messageId));
        ArgumentNullException.ThrowIfNull(completionFailure);

        MessageId = messageId;
    }

    /// <summary>The retained message whose completion was not confirmed.</summary>
    public Guid MessageId { get; }
}
