using System.Text.Json;

namespace Gloaming.Messaging;

/// <summary>A retained delivery identity and consumer-selected wire contract, independent of transport.</summary>
public sealed class OutgoingMessage
{
    /// <summary>Creates an immutable wire envelope and clones its JSON payload.</summary>
    /// <param name="messageId">Delivery identity retained unchanged across publication retries.</param>
    /// <param name="routeKey">Logical routing key interpreted by the consumer's publisher.</param>
    /// <param name="messageName">Stable wire-contract alias, independent of CLR type names and routing.</param>
    /// <param name="schemaVersion">Positive version of the named wire contract.</param>
    /// <param name="payload">Already serialized, non-null JSON; serialization policy belongs to the consumer.</param>
    /// <param name="tenantKey">Optional opaque tenant metadata. This neither admits a tenant nor grants authority.</param>
    /// <param name="correlationId">Optional conversation identity; does not deduplicate delivery.</param>
    /// <param name="causationId">Optional identity of the immediate cause of this message.</param>
    /// <param name="traceParent">Optional explicitly supplied W3C upstream context; invalid values do not reject business work.</param>
    /// <param name="traceState">Optional opaque W3C vendor state; not an identity or admission credential.</param>
    public OutgoingMessage(
        Guid messageId,
        string routeKey,
        string messageName,
        int schemaVersion,
        JsonElement payload,
        string? tenantKey = null,
        string? correlationId = null,
        string? causationId = null,
        string? traceParent = null,
        string? traceState = null
    )
    {
        ArgumentOutOfRangeException.ThrowIfEqual(messageId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(routeKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(schemaVersion);
        if (payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            throw new ArgumentException(
                "A message requires a non-null JSON payload.",
                nameof(payload)
            );
        if (tenantKey is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(tenantKey);
        if (correlationId is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        if (causationId is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(causationId);

        MessageId = messageId;
        RouteKey = routeKey;
        MessageName = messageName;
        SchemaVersion = schemaVersion;
        // Own the payload independently of the caller's JsonDocument lifetime.
        Payload = payload.Clone();
        TenantKey = tenantKey;
        CorrelationId = correlationId;
        CausationId = causationId;
        TraceParent = traceParent;
        TraceState = traceState;
    }

    /// <summary>Serializes a typed payload using explicit consumer options and returns a non-generic envelope.</summary>
    /// <typeparam name="TPayload">The producer's wire payload type; it does not propagate into storage or dispatch.</typeparam>
    /// <param name="messageId">Delivery identity retained unchanged across publication retries.</param>
    /// <param name="routeKey">Logical routing key interpreted by the consumer's publisher.</param>
    /// <param name="messageName">Stable wire-contract alias, independent of CLR type names.</param>
    /// <param name="schemaVersion">Positive version of the named wire contract.</param>
    /// <param name="payload">The non-null wire payload to serialize.</param>
    /// <param name="serializerOptions">Consumer-selected JSON policy, including converters and type metadata.</param>
    /// <param name="tenantKey">Optional opaque tenant metadata; grants no authority.</param>
    /// <param name="correlationId">Optional conversation identity.</param>
    /// <param name="causationId">Optional identity of the immediate cause.</param>
    /// <param name="traceParent">Optional explicitly supplied context of the selected producer activity.</param>
    /// <param name="traceState">Optional opaque vendor state accompanying that context.</param>
    public static OutgoingMessage FromPayload<TPayload>(
        Guid messageId,
        string routeKey,
        string messageName,
        int schemaVersion,
        TPayload payload,
        JsonSerializerOptions serializerOptions,
        string? tenantKey = null,
        string? correlationId = null,
        string? causationId = null,
        string? traceParent = null,
        string? traceState = null
    )
    {
        ArgumentNullException.ThrowIfNull(serializerOptions);

        return new(
            messageId,
            routeKey,
            messageName,
            schemaVersion,
            JsonSerializer.SerializeToElement(payload, serializerOptions),
            tenantKey,
            correlationId,
            causationId,
            traceParent,
            traceState
        );
    }

    /// <summary>Identifies this delivery; all retries retain the same value.</summary>
    public Guid MessageId { get; }

    /// <summary>Consumer-defined logical routing key, not necessarily a physical queue or endpoint.</summary>
    public string RouteKey { get; }

    /// <summary>Stable wire-contract alias; together with SchemaVersion identifies the payload contract.</summary>
    public string MessageName { get; }

    /// <summary>Positive version of the named wire contract.</summary>
    public int SchemaVersion { get; }

    /// <summary>Owned JSON value, independent of the source JsonDocument's lifetime.</summary>
    public JsonElement Payload { get; }

    /// <summary>Optional opaque tenant metadata. Does not admit a tenant or grant access.</summary>
    public string? TenantKey { get; }

    /// <summary>Optional conversation identity, independent of delivery deduplication.</summary>
    public string? CorrelationId { get; }

    /// <summary>Optional identity of the immediate cause, usually the preceding MessageId.</summary>
    public string? CausationId { get; }

    /// <summary>Explicitly supplied W3C upstream context; diagnostic only and independent of business identity.</summary>
    public string? TraceParent { get; }

    /// <summary>Opaque vendor state accompanying TraceParent; interpreted by native diagnostics.</summary>
    public string? TraceState { get; }
}
