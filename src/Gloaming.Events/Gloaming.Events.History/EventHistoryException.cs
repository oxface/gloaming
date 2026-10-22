namespace Gloaming.Events.History;

/// <summary>A history integrity failure; the consumer attaches its stream identity.</summary>
public sealed class EventHistoryException : Exception
{
    internal EventHistoryException(
        EventHistoryFailure failure,
        long? expectedVersion,
        long? observedVersion
    )
        : base("The event history range failed integrity validation.")
    {
        Failure = failure;
        ExpectedVersion = expectedVersion;
        ObservedVersion = observedVersion;
    }

    /// <summary>The integrity rule that the supplied history violated.</summary>
    public EventHistoryFailure Failure { get; }

    /// <summary>The expected stream position when the failure concerns a version boundary.</summary>
    public long? ExpectedVersion { get; }

    /// <summary>The supplied stream position associated with the integrity failure.</summary>
    public long? ObservedVersion { get; }
}
