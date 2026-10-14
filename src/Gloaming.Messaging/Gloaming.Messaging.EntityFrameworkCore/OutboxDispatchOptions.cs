namespace Gloaming.Messaging.EntityFrameworkCore;

/// <summary>Database-clock lease and retry timing for one module's dispatcher.</summary>
/// <param name="LeaseDuration">Positive claim lifetime. Expiry permits another publisher; this is not a transport timeout.</param>
/// <param name="RetryDelay">Nonnegative delay before a failed publication becomes eligible again.</param>
public sealed record OutboxDispatchOptions(TimeSpan LeaseDuration, TimeSpan RetryDelay);
