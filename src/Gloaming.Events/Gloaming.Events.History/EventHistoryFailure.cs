namespace Gloaming.Events.History;

/// <summary>Identifies a version, range or recorded-time violation in materialized history.</summary>
public enum EventHistoryFailure
{
    /// <summary>An entry does not occupy the next contiguous expected stream position.</summary>
    UnexpectedVersion = 1,

    /// <summary>The materialized history does not cover the requested version bounds.</summary>
    RangeMismatch = 2,

    /// <summary>Recorded times move backward in the supplied version order.</summary>
    RecordedTimeRegression = 3,
}
