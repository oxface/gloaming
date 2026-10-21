namespace Gloaming.Messaging.EntityFrameworkCore;

/// <summary>Consumer-selected eligibility after confirmed local rollback.</summary>
public abstract record InboxFailureDecision
{
    private protected InboxFailureDecision() { }

    /// <summary>Defer by a positive database-clock delay, recording a safe nonblank code of at most 128 characters.</summary>
    public sealed record RetryAfter(TimeSpan Delay, string Code) : InboxFailureDecision;

    /// <summary>Exclude retained work from automatic processing until an authorized retry.</summary>
    /// <param name="Code">Safe nonblank consumer reason of at most 128 characters; never exception text or payload.</param>
    public sealed record DeadLetter(string Code) : InboxFailureDecision;
}
