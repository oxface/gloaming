using Microsoft.EntityFrameworkCore;

namespace Gloaming.Messaging.EntityFrameworkCore;

/// <summary>Separately invoked, identity-preserving retry of quarantined work.</summary>
/// <remarks>Authorize in consumer code first. Requires a fresh context; owns its native transaction.</remarks>
public interface IInboxRecovery<TDbContext>
    where TDbContext : DbContext
{
    /// <summary>Make one unprocessed quarantined delivery eligible only at the observed revision.</summary>
    /// <remarks>Retains total failures and the complete envelope. Faults/cancellation throw; do not infer rollback from a lost commit response.</remarks>
    Task<InboxRetryResult> RetryAsync(
        string subscriptionKey,
        string producerKey,
        Guid messageId,
        long expectedRevision,
        CancellationToken cancellationToken = default
    );
}

/// <summary>Committed operator retry outcome.</summary>
public enum InboxRetryResult
{
    /// <summary>Quarantine cleared and eligibility committed for the unchanged delivery.</summary>
    Retried = 1,

    /// <summary>Missing/invisible, completed, nonquarantined or stale state; no transition applied.</summary>
    NotApplied = 2,
}
