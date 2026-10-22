namespace Gloaming.EventSourcing.EntityFrameworkCore;

/// <summary>Consumer-selected finite retry policy. Defaults to one attempt without delay.</summary>
public sealed record AggregateRegenerationOptions
{
    /// <summary>Positive finite attempt budget for recognized native regeneration conflicts.</summary>
    public int MaxAttempts { get; init; } = 1;

    /// <summary>Nonnegative delay before a recognized conflict is retried in a fresh attempt.</summary>
    public TimeSpan RetryDelay { get; init; } = TimeSpan.Zero;
}
