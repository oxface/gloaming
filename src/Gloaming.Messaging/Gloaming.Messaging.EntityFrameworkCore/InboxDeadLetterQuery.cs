namespace Gloaming.Messaging.EntityFrameworkCore;

/// <summary>Bounded live metadata selection; no total count or multi-page snapshot.</summary>
/// <param name="PageSize">One through 100 items; defaults to 50.</param>
/// <param name="FailureCode">Optional exact consumer-safe code; blank or over-128-character values reject.</param>
/// <param name="After">Opaque continuation from this exact lane/filter; grants no authority.</param>
public sealed record InboxDeadLetterQuery(
    int PageSize = 50,
    string? FailureCode = null,
    string? After = null
);
