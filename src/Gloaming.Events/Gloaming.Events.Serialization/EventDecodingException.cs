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

    /// <summary>The durable event alias supplied for decoding.</summary>
    public string EventName { get; }

    /// <summary>The durable schema version supplied for decoding.</summary>
    public int SchemaVersion { get; }

    /// <summary>Whether no supported identity/path exists or the payload cannot be decoded.</summary>
    public EventDecodingFailure Failure { get; }
}
