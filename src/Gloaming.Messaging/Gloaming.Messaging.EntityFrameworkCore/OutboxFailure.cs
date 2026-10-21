namespace Gloaming.Messaging.EntityFrameworkCore;

/// <summary>A publisher exception with proposed durable counts including this observation.</summary>
/// <param name="Message">The unchanged retained envelope; metadata grants no authority.</param>
/// <param name="Exception">The original publisher exception; acceptance may already have occurred.</param>
/// <param name="FailureCount">Lifetime committed publisher-exception observations, including this one.</param>
/// <param name="FailuresSinceRedrive">Such observations since the latest applied operator retry.</param>
public sealed record OutboxFailure(
    OutgoingMessage Message,
    Exception Exception,
    long FailureCount,
    long FailuresSinceRedrive
);
