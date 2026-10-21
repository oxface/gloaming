namespace Gloaming.Messaging.EntityFrameworkCore;

/// <summary>Bounded live selection without a total count or multi-page snapshot.</summary>
/// <param name="PageSize">One through 100 items, default 50.</param>
/// <param name="FailureCode">Optional exact safe code; blank or over-128-character values reject.</param>
/// <param name="After">Opaque continuation for this mapped table, route and code filter; grants no authority.</param>
public sealed record OutboxDeadLetterQuery(
    int PageSize = 50,
    string? FailureCode = null,
    string? After = null
);
