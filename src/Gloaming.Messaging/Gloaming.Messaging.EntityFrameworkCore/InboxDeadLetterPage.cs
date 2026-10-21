namespace Gloaming.Messaging.EntityFrameworkCore;

/// <summary>A detached live page of inbox dead-letter metadata.</summary>
/// <param name="Items">At most the requested page size, ordered by database time then UUID.</param>
/// <param name="NextCursor">Position after the last returned item, or null when no further item was observed.</param>
public sealed record InboxDeadLetterPage(IReadOnlyList<InboxDeadLetter> Items, string? NextCursor);
