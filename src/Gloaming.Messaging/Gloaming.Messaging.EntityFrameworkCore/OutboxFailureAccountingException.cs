namespace Gloaming.Messaging.EntityFrameworkCore;

/// <summary>Publication threw and its durable failure accounting was not confirmed.</summary>
/// <remarks>Transport acceptance or an accounting commit may already have occurred. Inspect fresh storage.</remarks>
public sealed class OutboxFailureAccountingException : AggregateException
{
    /// <summary>Preserves message identity and both errors without retaining the envelope.</summary>
    public OutboxFailureAccountingException(
        Guid messageId,
        Exception publicationFailure,
        Exception accountingFailure
    )
        : base(
            "Outbox publication failed and failure accounting was not confirmed.",
            publicationFailure,
            accountingFailure
        )
    {
        MessageId = messageId;
        PublicationFailure = publicationFailure;
        AccountingFailure = accountingFailure;
    }

    /// <summary>The retained outgoing identity.</summary>
    public Guid MessageId { get; }

    /// <summary>The original publisher error, which does not prove transport rejection.</summary>
    public Exception PublicationFailure { get; }

    /// <summary>The accounting lock, policy, SQL or commit error.</summary>
    public Exception AccountingFailure { get; }
}
