using Microsoft.EntityFrameworkCore;

namespace Gloaming.Messaging.EntityFrameworkCore;

/// <summary>One local processing attempt, owning its native save/completion/commit.</summary>
/// <remarks>Use a fresh scope/context per attempt; business concurrency remains consumer-owned.</remarks>
public interface IInboxProcessor<TDbContext>
    where TDbContext : DbContext
{
    /// <summary>Locks one eligible delivery, invokes its handler and commits local effects with completion.</summary>
    Task<InboxProcessingResult> ProcessNextAsync(
        string subscriptionKey,
        CancellationToken cancellationToken = default
    );
}

/// <summary>Committed processing outcome; faults and cancellation throw.</summary>
public enum InboxProcessingResult
{
    /// <summary>No eligible unlocked row was found; this is not a globally empty backlog.</summary>
    NoWork = 1,

    /// <summary>Handler effects and inbox completion committed; business refusal can also be handled work.</summary>
    Processed = 2,
}
