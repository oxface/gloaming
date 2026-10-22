namespace Gloaming.Events.History;

/// <summary>Explicit integrity checks for a consumer-selected ordered history range.</summary>
public static class EventHistory
{
    /// <summary>Validates the already ordered exclusive/inclusive version range and nondecreasing UTC recorded times; it does not load or sort rows.</summary>
    public static void ValidateRange(
        IEnumerable<HistoryPosition> positions,
        long afterVersion,
        long throughVersion
    )
    {
        ArgumentNullException.ThrowIfNull(positions);
        ArgumentOutOfRangeException.ThrowIfNegative(afterVersion);
        ArgumentOutOfRangeException.ThrowIfLessThan(throughVersion, afterVersion);
        long observedVersion = afterVersion;
        DateTimeOffset? previousRecordedAt = null;
        foreach (HistoryPosition current in positions)
        {
            if (observedVersion == throughVersion)
                throw new EventHistoryException(
                    EventHistoryFailure.RangeMismatch,
                    throughVersion,
                    current.StreamVersion
                );
            long expectedVersion = observedVersion + 1;
            if (current.StreamVersion != expectedVersion)
                throw new EventHistoryException(
                    EventHistoryFailure.UnexpectedVersion,
                    expectedVersion,
                    current.StreamVersion
                );
            if (previousRecordedAt is { } previous && current.RecordedAt < previous)
                throw new EventHistoryException(
                    EventHistoryFailure.RecordedTimeRegression,
                    null,
                    current.StreamVersion
                );
            observedVersion = current.StreamVersion;
            previousRecordedAt = current.RecordedAt;
        }
        if (observedVersion != throughVersion)
            throw new EventHistoryException(
                EventHistoryFailure.RangeMismatch,
                throughVersion,
                observedVersion
            );
    }
}
