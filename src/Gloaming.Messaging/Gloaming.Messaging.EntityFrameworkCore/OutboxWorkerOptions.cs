namespace Gloaming.Messaging.EntityFrameworkCore;

/// <summary>Polling delays for the optional sequential dispatch worker.</summary>
/// <param name="IdleDelay">Positive delay after no claimable work is found.</param>
/// <param name="FailureDelay">Positive delay after dispatch throws. Distinct from the record's retry delay.</param>
public sealed record OutboxWorkerOptions(TimeSpan IdleDelay, TimeSpan FailureDelay);
