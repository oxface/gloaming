namespace Gloaming.Messaging.EntityFrameworkCore;

/// <summary>Database-clock retry eligibility after a failed local attempt.</summary>
/// <param name="RetryDelay">Positive delay; other eligible work may proceed while this delivery waits.</param>
public sealed record InboxProcessingOptions(TimeSpan RetryDelay);
