namespace Gloaming.Messaging;

/// <summary>Returns only after the consumer's documented transport acceptance; retries retain MessageId.</summary>
public interface IMessagePublisher
{
    /// <summary>Routes and publishes the retained envelope, completing only after transport acceptance.</summary>
    /// <remarks>
    /// The implementation owns destination mapping, transport configuration and acceptance semantics.
    /// Throw on failed or unconfirmed acceptance. Acceptance does not establish downstream business completion.
    /// The dispatcher may call again with the same message after an ambiguous outcome; honor cancellation.
    /// </remarks>
    Task PublishAsync(OutgoingMessage message, CancellationToken cancellationToken);
}
