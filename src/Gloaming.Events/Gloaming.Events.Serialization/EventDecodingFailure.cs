namespace Gloaming.Events.Serialization;

/// <summary>Distinguishes unsupported event identity from an invalid supported payload.</summary>
public enum EventDecodingFailure
{
    /// <summary>The durable alias/version has no supported registration or upgrade path.</summary>
    UnknownEvent = 1,

    /// <summary>The selected registration or upgrade cannot produce a valid event from the payload.</summary>
    InvalidPayload = 2,
}
