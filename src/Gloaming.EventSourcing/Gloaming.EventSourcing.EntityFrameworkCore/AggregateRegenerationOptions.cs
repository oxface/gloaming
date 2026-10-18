namespace Gloaming.EventSourcing.EntityFrameworkCore;

/// <summary>Consumer-selected finite retry policy. Defaults to one attempt without delay.</summary>
public sealed record AggregateRegenerationOptions
{
    public int MaxAttempts { get; init; } = 1;
    public TimeSpan RetryDelay { get; init; } = TimeSpan.Zero;
}
