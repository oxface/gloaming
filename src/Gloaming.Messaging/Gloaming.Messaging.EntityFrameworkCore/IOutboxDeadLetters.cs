using Microsoft.EntityFrameworkCore;

namespace Gloaming.Messaging.EntityFrameworkCore;

/// <summary>Bounded metadata inspection and identity-preserving retry of retained outgoing dead letters.</summary>
/// <remarks>Authorize every invocation in consumer code. Requires a fresh context and owns each native transaction.</remarks>
public interface IOutboxDeadLetters<TDbContext>
    where TDbContext : DbContext
{
    /// <summary>Read a bounded live page in one exact route, optionally filtered by failure code.</summary>
    /// <remarks>Database time/UUID ordering; cursors are positions, not authority or snapshots.</remarks>
    Task<OutboxDeadLetterPage> ListAsync(
        string routeKey,
        OutboxDeadLetterQuery query,
        CancellationToken cancellationToken = default
    );

    /// <summary>Read current dead-letter metadata, or null for missing, invisible or inapplicable work.</summary>
    Task<OutboxDeadLetter?> GetAsync(
        string routeKey,
        Guid messageId,
        CancellationToken cancellationToken = default
    );

    /// <summary>Make one undispatched dead letter eligible only at the observed revision.</summary>
    /// <remarks>Retains the complete envelope and total counts. A missing commit response does not prove rollback.</remarks>
    Task<OutboxRetryResult> RetryAsync(
        string routeKey,
        Guid messageId,
        long expectedRevision,
        CancellationToken cancellationToken = default
    );
}

/// <summary>Observed committed operator retry outcome.</summary>
public enum OutboxRetryResult
{
    /// <summary>Eligibility committed for the unchanged retained message.</summary>
    Retried = 1,

    /// <summary>Missing/invisible, wrong route, completed, active/pending or stale state; no transition applied.</summary>
    NotApplied = 2,
}
