namespace Gloaming.Messaging.EntityFrameworkCore;

/// <summary>Polling delays for the optional sequential worker.</summary>
/// <param name="IdleDelay">Positive delay after no claimable work.</param>
/// <param name="FailureDelay">Positive delay after a failed attempt; durable retry eligibility is separate.</param>
public sealed record InboxWorkerOptions(TimeSpan IdleDelay, TimeSpan FailureDelay);
