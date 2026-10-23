using Gloaming.Events.Serialization;

namespace Gloaming.Samples.Wholesale.EventCodecDemo;

// Consumer-owned row shape; payloads are deliberately absent from the range validator.
internal sealed record RecordedEvent(
    long StreamVersion,
    DateTimeOffset RecordedAt,
    SerializedEvent Event
);
