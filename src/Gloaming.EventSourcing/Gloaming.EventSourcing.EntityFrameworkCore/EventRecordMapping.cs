namespace Gloaming.EventSourcing.EntityFrameworkCore;

/// <summary>Configured persistence encoding and consumer row fields for one explicit stream stream type.</summary>
public abstract class EventRecordMapping<TEvent, TStreamRecord, TStoredEventRecord>
    where TEvent : class
    where TStreamRecord : class, IEventStreamRecord
    where TStoredEventRecord : class, IStoredEventRecord
{
    public abstract string StreamType { get; }

    /// <summary>Returns a fresh detached envelope with encoding/ownership fields only; do not retain or mutate it.</summary>
    public abstract TStoredEventRecord ToRow(TEvent eventData, TStreamRecord observedStream);
}
