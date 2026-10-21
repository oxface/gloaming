namespace Gloaming.Messaging.EntityFrameworkCore;

/// <summary>Consumer-selected eligibility after an observed publisher exception.</summary>
public abstract record OutboxFailureDecision
{
    private protected OutboxFailureDecision() { }

    /// <summary>Retry after a nonnegative database-clock delay with a safe nonblank code of at most 128 characters.</summary>
    public sealed record RetryAfter(TimeSpan Delay, string Code) : OutboxFailureDecision;

    /// <summary>Stop automatic claims until authorized retry. This cannot retract external acceptance.</summary>
    /// <param name="Code">Safe nonblank consumer reason of at most 128 characters; never exception text or payload.</param>
    public sealed record DeadLetter(string Code) : OutboxFailureDecision;
}
