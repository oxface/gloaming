namespace Gloaming.Messaging.EntityFrameworkCore;

/// <summary>Default database-clock retry eligibility when no subscription failure policy is registered.</summary>
/// <param name="RetryDelay">Positive delay; other eligible work may proceed while this delivery waits.</param>
public sealed record InboxProcessingOptions(TimeSpan RetryDelay);
