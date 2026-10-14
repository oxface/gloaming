using System.Text.Json;

namespace Gloaming.Messaging;

/// <summary>An owned incoming wire envelope; transport trust and tenant admission belong to the receiver.</summary>
public sealed class IncomingMessage
{
    /// <summary>Captures a delivery and clones its payload independently of the transport buffer/document.</summary>
    /// <param name="messageId">Stable delivery identity retained across retries.</param>
    /// <param name="producerKey">Stable producer namespace assigned by the trusted receiving adapter.</param>
    /// <param name="messageName">Stable wire-contract alias.</param>
    /// <param name="schemaVersion">Positive wire-contract version.</param>
    /// <param name="payload">Non-null JSON value, decoded later using consumer policy.</param>
    /// <param name="tenantKey">Optional opaque tenant metadata; grants no authority.</param>
    /// <param name="correlationId">Optional conversation identity; not a delivery identity.</param>
    /// <param name="causationId">Optional identity of the immediate cause.</param>
    /// <param name="traceParent">Optional explicitly supplied W3C upstream context; invalid values do not reject business work.</param>
    /// <param name="traceState">Optional opaque W3C vendor state; not an identity or admission credential.</param>
    public IncomingMessage(
        Guid messageId,
        string producerKey,
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
        ArgumentException.ThrowIfNullOrWhiteSpace(producerKey);
        ArgumentException.ThrowIfNullOrWhiteSpace(messageName);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(schemaVersion);
        if (payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            throw new ArgumentException(
                "An incoming message requires non-null JSON.",
                nameof(payload)
            );
        if (tenantKey is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(tenantKey);
        if (correlationId is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);
        if (causationId is not null)
            ArgumentException.ThrowIfNullOrWhiteSpace(causationId);

        MessageId = messageId;
        ProducerKey = producerKey;
        MessageName = messageName;
        SchemaVersion = schemaVersion;
        Payload = payload.Clone();
        TenantKey = tenantKey;
        CorrelationId = correlationId;
        CausationId = causationId;
        TraceParent = traceParent;
        TraceState = traceState;
    }

    /// <summary>Delivery identity within its producer namespace.</summary>
    public Guid MessageId { get; }

    /// <summary>Receiver-assigned producer namespace; does not authenticate the sender by itself.</summary>
    public string ProducerKey { get; }

    /// <summary>Stable payload contract alias.</summary>
    public string MessageName { get; }

    /// <summary>Positive version of the named payload.</summary>
    public int SchemaVersion { get; }

    /// <summary>Owned wire JSON; deserialization and compatibility belong to the handler.</summary>
    public JsonElement Payload { get; }

    /// <summary>Optional opaque tenant metadata; does not admit a tenant.</summary>
    public string? TenantKey { get; }

    /// <summary>Optional conversation identity, independent of delivery deduplication.</summary>
    public string? CorrelationId { get; }

    /// <summary>Optional immediate cause identity.</summary>
    public string? CausationId { get; }

    /// <summary>Explicitly supplied W3C upstream context; diagnostic only and independent of business identity.</summary>
    public string? TraceParent { get; }

    /// <summary>Opaque vendor state accompanying TraceParent; interpreted by native diagnostics.</summary>
    public string? TraceState { get; }
}
