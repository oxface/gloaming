namespace Gloaming.Messaging.EntityFrameworkCore;

/// <summary>Processing failed and its durable failure accounting was not confirmed.</summary>
/// <remarks>An accounting commit may have succeeded despite a lost response. Inspect fresh storage.</remarks>
public sealed class InboxFailureAccountingException : AggregateException
{
    /// <summary>Retains delivery identity and both original errors without including its envelope.</summary>
    public InboxFailureAccountingException(
        string subscriptionKey,
        string producerKey,
        Guid messageId,
        Exception processingFailure,
        Exception accountingFailure
    )
        : base(
            "Inbox processing failed and failure accounting was not confirmed.",
            processingFailure,
            accountingFailure
        )
    {
        SubscriptionKey = subscriptionKey;
        ProducerKey = producerKey;
        MessageId = messageId;
        ProcessingFailure = processingFailure;
        AccountingFailure = accountingFailure;
    }

    /// <summary>The receiver's stable processing registration.</summary>
    public string SubscriptionKey { get; }

    /// <summary>The receiver-assigned producer namespace.</summary>
    public string ProducerKey { get; }

    /// <summary>The retained delivery identity.</summary>
    public Guid MessageId { get; }

    /// <summary>The original handler, save or completion error.</summary>
    public Exception ProcessingFailure { get; }

    /// <summary>The rollback, policy, metadata-write or accounting-commit error.</summary>
    public Exception AccountingFailure { get; }
}
