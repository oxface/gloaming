using System.Text.Json;
using Gloaming.EventSourcing.EntityFrameworkCore;

namespace Gloaming.Samples.EventStorageDemo;

internal sealed class CounterEventRecordMapping
    : EventRecordMapping<CounterEvent, EventStreamRecord, StoredEventRecord>
{
    public override string StreamType => "proof.counter";

    public override StoredEventRecord ToRow(CounterEvent @event, EventStreamRecord stream) =>
        @event switch
        {
            CounterStarted started => new()
            {
                EventName = "proof.counter-started",
                SchemaVersion = 1,
                Payload = JsonSerializer.SerializeToElement(new { value = started.Value }),
            },
            CounterIncreased increased => new()
            {
                EventName = "proof.counter-increased",
                SchemaVersion = 1,
                Payload = JsonSerializer.SerializeToElement(new { amount = increased.Amount }),
            },
            _ => throw new InvalidOperationException("The counter event is not registered."),
        };
}
