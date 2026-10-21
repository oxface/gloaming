namespace Gloaming.Messaging.EntityFrameworkCore;

/// <summary>A failed local operation, with proposed durable counts including this failure.</summary>
/// <param name="SubscriptionKey">Receiver-selected processing lane.</param>
/// <param name="Message">Unchanged retained envelope; its metadata grants no authority.</param>
/// <param name="Exception">Original processing error, not a durable diagnostic string.</param>
/// <param name="FailureCount">Lifetime confirmed failures including this one.</param>
/// <param name="FailuresSinceRedrive">Confirmed failures since the latest applied operator retry.</param>
public sealed record InboxFailure(
    string SubscriptionKey,
    IncomingMessage Message,
    Exception Exception,
    long FailureCount,
    long FailuresSinceRedrive
);
