namespace Gloaming.Messaging.EntityFrameworkCore;

/// <summary>Database-clock lease and retry timing for one module's dispatcher.</summary>
/// <param name="LeaseDuration">Positive claim lifetime. Expiry permits another publisher; this is not a transport timeout.</param>
/// <param name="RetryDelay">Nonnegative delay before a failed publication becomes eligible again.</param>
public sealed record OutboxDispatchOptions(TimeSpan LeaseDuration, TimeSpan RetryDelay)
{
    /// <summary>Optional exact route keys eligible for claims; null selects all routes. An empty collection or blank/null entries are invalid.</summary>
    /// <remarks>The dispatcher copies these keys at construction. Changed selection requires a fresh dispatcher.
    /// Selection filters delivery claims; overlapping selections still compete under the existing lease protocol.
    /// It does not authenticate an owner, tenant or publisher.</remarks>
    public IReadOnlyCollection<string>? RouteKeys { get; init; }
}
