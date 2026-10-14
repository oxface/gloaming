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

    public EventHistoryFailure Failure { get; }
    public long? ExpectedVersion { get; }
    public long? ObservedVersion { get; }
}
