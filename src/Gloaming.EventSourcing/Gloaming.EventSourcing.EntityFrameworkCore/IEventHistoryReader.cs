namespace Gloaming.EventSourcing.EntityFrameworkCore;

/// <summary>Reads a decoded ordered prefix bounded by an already observed stream version.</summary>
public interface IEventHistoryReader<TEvent, TStreamRecord>
    where TEvent : class
    where TStreamRecord : class, IEventStreamRecord
{
    /// <summary>Loads and validates the admitted stream prefix through its captured version, without tracking, evolving or saving aggregate state.</summary>
    Task<IReadOnlyList<ReplayedEvent<TEvent>>> ReadAsync(
        TStreamRecord observedStream,
        long? throughVersion = null,
        CancellationToken cancellationToken = default
    );
}
