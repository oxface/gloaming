using System.Text.Json;

namespace Gloaming.Messaging.Tests;

public sealed class MessageTests
{
    [Fact]
    public void IncomingPayloadOutlivesTransportDocumentAndRetainsIndependentIdentityMetadata()
    {
        IncomingMessage incoming;
        Guid id = Guid.NewGuid();
        using (var document = JsonDocument.Parse("{\"quantity\":3}"))
            incoming = new(
                id,
                "inventory",
                "issued",
                1,
                document.RootElement,
                "alpha",
                "conversation",
                "cause"
            );
        Assert.Equal(id, incoming.MessageId);
        Assert.Equal("inventory", incoming.ProducerKey);
        Assert.Equal("alpha", incoming.TenantKey);
        Assert.Equal("conversation", incoming.CorrelationId);
        Assert.Equal("cause", incoming.CausationId);
        Assert.Equal(3, incoming.Payload.GetProperty("quantity").GetInt32());
        var outgoing = OutgoingMessage.FromPayload(
            Guid.NewGuid(),
            "reply",
            "handled",
            1,
            new WireItem("ok"),
            new JsonSerializerOptions(),
            incoming.TenantKey,
            incoming.CorrelationId,
            incoming.MessageId.ToString()
        );
        Assert.Equal("conversation", outgoing.CorrelationId);
        Assert.Equal(id.ToString(), outgoing.CausationId);
    }

    [Fact]
    public void IncomingEnvelopeAndOptionalCorrelationMetadataRejectInvalidValues()
    {
        var payload = JsonSerializer.SerializeToElement(new { value = 1 });
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new IncomingMessage(Guid.Empty, "p", "m", 1, payload)
        );
        Assert.Throws<ArgumentException>(() =>
            new IncomingMessage(Guid.NewGuid(), " ", "m", 1, payload)
        );
        Assert.Throws<ArgumentException>(() =>
            new IncomingMessage(Guid.NewGuid(), "p", "m", 1, default)
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new IncomingMessage(Guid.NewGuid(), "p", "m", 0, payload)
        );
        Assert.Throws<ArgumentException>(() =>
            new IncomingMessage(Guid.NewGuid(), "p", "m", 1, payload, correlationId: " ")
        );
        Assert.Throws<ArgumentException>(() =>
            new OutgoingMessage(Guid.NewGuid(), "r", "m", 1, payload, causationId: " ")
        );
    }

    [Fact]
    public void TypedConstructionUsesConsumerJsonPolicyAndCapturesThePayloadBeforeLaterChanges()
    {
        var payload = new List<WireItem> { new("first") };
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web);
        Guid id = Guid.NewGuid();
        var message = OutgoingMessage.FromPayload(
            id,
            "commands.export",
            "exports.render",
            2,
            payload,
            options,
            "Tenant A"
        );
        payload.Add(new("later"));

        Assert.Equal(id, message.MessageId);
        Assert.Equal("commands.export", message.RouteKey);
        Assert.Equal("exports.render", message.MessageName);
        Assert.Equal("Tenant A", message.TenantKey);
        var item = Assert.Single(message.Payload.EnumerateArray());
        Assert.Equal("first", item.GetProperty("displayName").GetString());
        Assert.False(item.TryGetProperty("DisplayName", out _));
    }

    [Fact]
    public void TypedConstructionRequiresExplicitJsonPolicyAndANonNullSerializedPayload()
    {
        Assert.Throws<ArgumentNullException>(() =>
            OutgoingMessage.FromPayload(Guid.NewGuid(), "d", "m", 1, new WireItem("x"), null!)
        );
        Assert.Throws<ArgumentException>(() =>
            OutgoingMessage.FromPayload<WireItem?>(Guid.NewGuid(), "d", "m", 1, null, new())
        );
    }

    [Fact]
    public void RetainedPayloadOutlivesItsSourceAndPreservesConsumerMetadata()
    {
        Guid id = Guid.NewGuid();
        OutgoingMessage message;
        using (var document = JsonDocument.Parse("{\"optional\":null,\"quantity\":2.5}"))
            message = new(
                id,
                "commands.export",
                "exports.render",
                3,
                document.RootElement,
                "Tenant A"
            );
        Assert.Equal(id, message.MessageId);
        Assert.Equal("commands.export", message.RouteKey);
        Assert.Equal("exports.render", message.MessageName);
        Assert.Equal(3, message.SchemaVersion);
        Assert.Equal("Tenant A", message.TenantKey);
        Assert.Equal(2.5m, message.Payload.GetProperty("quantity").GetDecimal());
        Assert.Equal(JsonValueKind.Null, message.Payload.GetProperty("optional").ValueKind);
    }

    [Fact]
    public void UndefinedOrNullPayloadCannotBecomeDurableWork()
    {
        Assert.Throws<ArgumentException>(() =>
            new OutgoingMessage(Guid.NewGuid(), "d", "m", 1, default)
        );
        Assert.Throws<ArgumentException>(() =>
            new OutgoingMessage(
                Guid.NewGuid(),
                "d",
                "m",
                1,
                JsonSerializer.SerializeToElement<object?>(null)
            )
        );
    }

    [Fact]
    public void InvalidIdentityOrSchemaFailsAtTheEnvelopeBoundary()
    {
        var payload = JsonSerializer.SerializeToElement(new { value = 1 });
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OutgoingMessage(Guid.Empty, "d", "m", 1, payload)
        );
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new OutgoingMessage(Guid.NewGuid(), "d", "m", 0, payload)
        );
        Assert.Throws<ArgumentException>(() =>
            new OutgoingMessage(Guid.NewGuid(), " ", "m", 1, payload)
        );
        Assert.Throws<ArgumentException>(() =>
            new OutgoingMessage(Guid.NewGuid(), "d", "m", 1, payload, " ")
        );
    }

    [Fact]
    public void DiagnosticMetadataIsExplicitOptionalAndIndependentOfBusinessValidation()
    {
        using var ambient = new System.Diagnostics.Activity("ambient").Start();
        var omitted = OutgoingMessage.FromPayload(
            Guid.NewGuid(),
            "r",
            "m",
            1,
            new { value = 1 },
            new()
        );
        Assert.Null(omitted.TraceParent);
        Assert.Null(omitted.TraceState);

        var supplied = OutgoingMessage.FromPayload(
            Guid.NewGuid(),
            "r",
            "m",
            1,
            new { value = 1 },
            new(),
            traceParent: "invalid-parent",
            traceState: "opaque-state"
        );
        var incoming = new IncomingMessage(
            supplied.MessageId,
            "p",
            "m",
            1,
            supplied.Payload,
            traceParent: supplied.TraceParent,
            traceState: supplied.TraceState
        );
        Assert.Equal("invalid-parent", incoming.TraceParent);
        Assert.Equal("opaque-state", incoming.TraceState);
    }

    private sealed record WireItem(string DisplayName);
}
