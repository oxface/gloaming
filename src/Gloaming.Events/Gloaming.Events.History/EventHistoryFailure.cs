namespace Gloaming.Events.History;

public enum EventHistoryFailure
{
    UnexpectedVersion = 1,
    RangeMismatch = 2,
    RecordedTimeRegression = 3,
}
