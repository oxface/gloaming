using Microsoft.EntityFrameworkCore;

namespace Gloaming.Messaging.EntityFrameworkCore;

/// <summary>Dispatches retained outgoing work from one owning module's context.</summary>
/// <typeparam name="TDbContext">The typed context identifying this module's outbox.</typeparam>
public interface IOutboxDispatcher<TDbContext>
    where TDbContext : DbContext
{
    /// <summary>Claims at most one committed message, publishes outside its claim transaction, then completes.</summary>
    /// <remarks>
    /// Use an independent context without pending business changes or an existing transaction.
    /// Publication and storage failures propagate; acceptance may already have occurred when storage fails.
    /// Unconfirmed completion after normal publication return throws OutboxCompletionException,
    /// including completion-stage cancellation. Its InnerException preserves the original error.
    /// Publisher exceptions whose failure accounting is unconfirmed throw OutboxFailureAccountingException.
    /// </remarks>
    Task<OutboxDispatchResult> DispatchNextAsync(CancellationToken cancellationToken = default);
}

/// <summary>Outcome of one dispatch attempt; failures are exceptions rather than enum values.</summary>
public enum OutboxDispatchResult
{
    /// <summary>No currently claimable row was found; delayed, leased or locked work may still exist.</summary>
    NoWork = 1,

    /// <summary>The publisher accepted the message and the outbox completion committed.</summary>
    Published = 2,

    /// <summary>The publisher accepted the message, but the lease expired or changed before completion could be recorded.</summary>
    ClaimLost = 3,
}
