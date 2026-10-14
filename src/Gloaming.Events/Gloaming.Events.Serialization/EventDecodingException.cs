using System.Text.Json;

namespace Gloaming.Events.Serialization;

/// <summary>A codec failure; the consumer attaches stream context and chooses failure policy.</summary>
public sealed class EventDecodingException : Exception
{
    internal EventDecodingException(
        string eventName,
        int schemaVersion,
        EventDecodingFailure failure,
        JsonException? innerException = null
    )
        : base("The event could not be decoded.", innerException)
    {
        EventName = eventName;
        SchemaVersion = schemaVersion;
        Failure = failure;
    }

    public string EventName { get; }
    public int SchemaVersion { get; }
    public EventDecodingFailure Failure { get; }
}
