namespace Gloaming.Messaging.EntityFrameworkCore;

/// <summary>A detached live page of outgoing dead-letter metadata.</summary>
/// <param name="Items">At most the requested size, ordered by database dead-letter time then UUID.</param>
/// <param name="NextCursor">Position after the last item, or null when no further item was observed.</param>
public sealed record OutboxDeadLetterPage(
    IReadOnlyList<OutboxDeadLetter> Items,
    string? NextCursor
);
