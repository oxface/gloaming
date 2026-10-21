namespace Gloaming.Messaging.EntityFrameworkCore;

/// <summary>Polling delays for the optional sequential dispatch worker.</summary>
/// <param name="IdleDelay">Positive delay after no claimable work is found.</param>
/// <param name="FailureDelay">Positive delay after ordinary dispatch failure. Unconfirmed failure accounting or completion faults the worker instead.</param>
public sealed record OutboxWorkerOptions(TimeSpan IdleDelay, TimeSpan FailureDelay);
