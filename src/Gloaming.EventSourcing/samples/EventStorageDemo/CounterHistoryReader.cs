using Gloaming.EventSourcing.EntityFrameworkCore;

namespace ModulithFoundry.Samples.EventStorageDemo;

// No codec or tenant service is required: the shared reader owns only native prefix loading.
internal sealed class CounterHistoryReader(StorageDbContext database)
    : EventHistoryReader<CounterEvent, EventStreamRecord, StoredEventRecord>(
        database,
        "proof.counter"
    )
{
    protected override CounterEvent DecodeEvent(StoredEventRecord record)
    {
        if (record.SchemaVersion != 1)
            throw new InvalidDataException("The counter schema is unsupported.");

        return record.EventName switch
        {
            "proof.counter-started" when record.StreamVersion == 1 => new CounterStarted(
                record.Payload.GetProperty("value").GetInt32()
            ),
            "proof.counter-increased" when record.StreamVersion > 1 => new CounterIncreased(
                record.Payload.GetProperty("amount").GetInt32()
            ),
            _ => throw new InvalidDataException("The counter fact sequence is invalid."),
        };
    }
}
