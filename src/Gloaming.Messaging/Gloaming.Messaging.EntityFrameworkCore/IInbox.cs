using Microsoft.EntityFrameworkCore;

namespace Gloaming.Messaging.EntityFrameworkCore;

/// <summary>Retained intake within the receiver's explicit native transaction; never saves or commits.</summary>
public interface IInbox<TDbContext>
    where TDbContext : DbContext
{
    /// <summary>Inserts or compares retained work. Commit before acknowledging the transport.</summary>
    /// <remarks>
    /// Requires native ReadCommitted isolation. Retained incompatible content throws InboxMessageConflictException.
    /// A receipt removed by retention can queue again. Three unobservable conflicting-key observations throw
    /// DbUpdateConcurrencyException without acceptance; native SELECT/RLS visibility remains consumer-owned.
    /// </remarks>
    Task<InboxReceiveResult> ReceiveAsync(
        string subscriptionKey,
        IncomingMessage message,
        CancellationToken cancellationToken = default
    );
}

/// <summary>Provisional intake outcome; neither value proves the caller's transaction committed.</summary>
public enum InboxReceiveResult
{
    /// <summary>This transaction inserted retained work.</summary>
    Queued = 1,

    /// <summary>Equivalent retained work already exists, pending or completed.</summary>
    AlreadyReceived = 2,
}
