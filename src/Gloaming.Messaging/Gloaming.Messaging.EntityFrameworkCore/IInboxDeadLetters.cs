using Microsoft.EntityFrameworkCore;

namespace Gloaming.Messaging.EntityFrameworkCore;

/// <summary>Bounded metadata inspection and identity-preserving retry of retained inbox dead letters.</summary>
/// <remarks>Authorize each invocation in consumer code. Requires a fresh context; owns each native transaction.</remarks>
public interface IInboxDeadLetters<TDbContext>
    where TDbContext : DbContext
{
    /// <summary>Read a bounded live page in one exact subscription/producer lane, optionally filtered by failure code.</summary>
    /// <remarks>Orders by dead-letter time then database UUID order. Cursors are positions, not authorization or snapshots.</remarks>
    Task<InboxDeadLetterPage> ListAsync(
        string subscriptionKey,
        string producerKey,
        InboxDeadLetterQuery query,
        CancellationToken cancellationToken = default
    );

    /// <summary>Read current dead-letter metadata, or null for missing, invisible or no longer dead-lettered work.</summary>
    Task<InboxDeadLetter?> GetAsync(
        string subscriptionKey,
        string producerKey,
        Guid messageId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Make one unprocessed dead letter eligible only in the observed receipt generation and revision.</summary>
    /// <remarks>Retains total failures and the complete envelope. Faults/cancellation throw; do not infer rollback from a lost commit response.</remarks>
    Task<InboxRetryResult> RetryAsync(
        string subscriptionKey,
        string producerKey,
        Guid messageId,
        Guid expectedReceiptId,
        long expectedRevision,
        CancellationToken cancellationToken = default
    );
}

/// <summary>Committed operator retry outcome.</summary>
public enum InboxRetryResult
{
    /// <summary>Dead-letter state cleared and eligibility committed for the unchanged delivery.</summary>
    Retried = 1,

    /// <summary>Missing/invisible, completed, not dead-lettered or stale state; no transition applied.</summary>
    NotApplied = 2,
}
