using Gloaming.Events.Serialization;

namespace ModulithFoundry.Samples.Wholesale.EventCodecDemo;

// Consumer-owned row shape; payloads are deliberately absent from the range validator.
internal sealed record RecordedEvent(
    long StreamVersion,
    DateTimeOffset RecordedAt,
    SerializedEvent Event
);
