namespace Gloaming.Messaging.EntityFrameworkCore;

/// <summary>Detached aggregate observation of one exact route's visible retained outbox work.</summary>
/// <param name="ObservedAt">Single database statement time used for all classifications.</param>
/// <param name="Pending">Undispatched work excluding dead letters; the sum of Eligible, Delayed and Leased.</param>
/// <param name="Eligible">Pending work whose availability has arrived and has no unexpired lease.</param>
/// <param name="Delayed">Pending work without an unexpired lease whose availability is still in the future.</param>
/// <param name="Leased">Pending work with an unexpired lease, regardless of availability.</param>
/// <param name="DeadLetters">Undispatched work stopped by consumer failure policy.</param>
/// <param name="OldestEligibleAge">Age since original queue insertion of the oldest eligible row, or null when none is eligible.</param>
public sealed record OutboxBacklogSnapshot(
    DateTimeOffset ObservedAt,
    long Pending,
    long Eligible,
    long Delayed,
    long Leased,
    long DeadLetters,
    TimeSpan? OldestEligibleAge
);
