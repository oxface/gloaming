using System.Text.Json;
using System.Text.Json.Serialization;
using Gloaming.Events.Serialization;

namespace Gloaming.EventSerializationTests;

public sealed class CodecTests
{
    private interface IEvent;

    private sealed record RenamedReceipt(decimal AmountValue, string? DeliveryReference = null)
        : IEvent;

    private sealed record AnotherReceipt(decimal AmountValue) : IEvent;

    private abstract record AbstractEvent : IEvent;

    private record BaseEvent(decimal AmountValue) : IEvent;

    private sealed record DerivedEvent(decimal AmountValue, string Source) : BaseEvent(AmountValue);

    private static JsonSerializerOptions Options() =>
        new(JsonSerializerDefaults.Web)
        {
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
        };

    private static JsonEventCodec<IEvent> Codec(JsonSerializerOptions? options = null) =>
        new(
            options ?? Options(),
            [EventRegistration<IEvent>.For<RenamedReceipt>("stock.received", 1)]
        );

    [Fact]
    public void ConflictingDurableIdentityCannotDependOnRegistrationOrder()
    {
        Assert.Throws<ArgumentException>(() =>
            new JsonEventCodec<IEvent>(
                Options(),
                [
                    EventRegistration<IEvent>.For<RenamedReceipt>("stock.received", 1),
                    EventRegistration<IEvent>.For<AnotherReceipt>("stock.received", 1),
                ]
            )
        );
    }

    [Fact]
    public void OneClrTypeCannotHaveAmbiguousWriteIdentities()
    {
        Assert.Throws<ArgumentException>(() =>
            new JsonEventCodec<IEvent>(
                Options(),
                [
                    EventRegistration<IEvent>.For<RenamedReceipt>("stock.received", 1),
                    EventRegistration<IEvent>.For<RenamedReceipt>("other.received", 1),
                ]
            )
        );
    }

    [Fact]
    public void InterfaceTypedWriteEmitsExplicitIdentityAndConcretePayload()
    {
        IEvent receipt = new RenamedReceipt(10.125m, "DELIVERY-1");
        SerializedEvent stored = Codec().Serialize(receipt);
        Assert.Equal("stock.received", stored.EventName);
        Assert.Equal(1, stored.SchemaVersion);
        Assert.Equal(10.125m, stored.Payload.GetProperty("amountValue").GetDecimal());
        Assert.Equal("DELIVERY-1", stored.Payload.GetProperty("deliveryReference").GetString());
        Assert.False(stored.Payload.TryGetProperty("$type", out _));
    }

    [Fact]
    public void LiteralIdentityDecodesRenamedClrEventWithAnOptionalDefault()
    {
        using var payload = JsonDocument.Parse("""{"amountValue":10.125}""");
        var receipt = Assert.IsType<RenamedReceipt>(
            Codec().Deserialize("stock.received", 1, payload.RootElement)
        );
        Assert.Equal(10.125m, receipt.AmountValue);
        Assert.Null(receipt.DeliveryReference);
    }

    [Theory]
    [InlineData("stock.missing", 1)]
    [InlineData("stock.received", 99)]
    [InlineData("Stock.Received", 1)]
    [InlineData("Old.Namespace.Receipt", 1)]
    public void UnknownIdentityDoesNotFallBackToClrNamesOrAnotherVersion(string name, int version)
    {
        var failure = Assert.Throws<EventDecodingException>(() =>
            Codec().Deserialize(name, version, default)
        );
        Assert.Equal(EventDecodingFailure.UnknownEvent, failure.Failure);
        Assert.Equal(name, failure.EventName);
        Assert.Equal(version, failure.SchemaVersion);
        Assert.Null(failure.InnerException);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("null")]
    [InlineData("{\"amountValue\":\"private-payload-data\"}")]
    public void InvalidPayloadIsClassifiedWithoutCopyingItsContentsToTheMessage(string json)
    {
        using var payload = JsonDocument.Parse(json);
        var failure = Assert.Throws<EventDecodingException>(() =>
            Codec().Deserialize("stock.received", 1, payload.RootElement)
        );
        Assert.Equal(EventDecodingFailure.InvalidPayload, failure.Failure);
        Assert.Equal("stock.received", failure.EventName);
        Assert.Equal(1, failure.SchemaVersion);
        Assert.DoesNotContain("private-payload-data", failure.Message);
        if (json != "null")
            Assert.IsType<JsonException>(failure.InnerException);
    }

    [Fact]
    public void MissingJsonElementIsInvalidPayloadRatherThanUnknownIdentity()
    {
        var failure = Assert.Throws<EventDecodingException>(() =>
            Codec().Deserialize("stock.received", 1, default)
        );
        Assert.Equal(EventDecodingFailure.InvalidPayload, failure.Failure);
    }

    [Fact]
    public void EstablishedCodecRetainsConsumerOptionsAndRegistrationSnapshot()
    {
        var options = Options();
        options.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
        List<EventRegistration<IEvent>> registrations =
        [
            EventRegistration<IEvent>.For<RenamedReceipt>("stock.received", 1),
        ];
        var codec = new JsonEventCodec<IEvent>(options, registrations);
        options.PropertyNamingPolicy = null;
        options.RespectRequiredConstructorParameters = false;
        registrations.Clear();
        registrations.Add(EventRegistration<IEvent>.For<AnotherReceipt>("other.received", 1));
        var stored = codec.Serialize(new RenamedReceipt(7.5m, "DELIVERY-2"));
        Assert.Equal("stock.received", stored.EventName);
        Assert.Equal(7.5m, stored.Payload.GetProperty("amount_value").GetDecimal());
        Assert.Equal("DELIVERY-2", stored.Payload.GetProperty("delivery_reference").GetString());
        using var payload = JsonDocument.Parse("""{"amount_value":2.5}""");
        Assert.Equal(
            new RenamedReceipt(2.5m),
            codec.Deserialize("stock.received", 1, payload.RootElement)
        );
        using var missing = JsonDocument.Parse("{}");
        Assert.Equal(
            EventDecodingFailure.InvalidPayload,
            Assert
                .Throws<EventDecodingException>(() =>
                    codec.Deserialize("stock.received", 1, missing.RootElement)
                )
                .Failure
        );
        Assert.Throws<InvalidOperationException>(() => codec.Serialize(new AnotherReceipt(1)));
    }

    [Fact]
    public void UnregisteredDerivedTypeDoesNotUseABaseWriteIdentity()
    {
        var codec = new JsonEventCodec<IEvent>(
            Options(),
            [EventRegistration<IEvent>.For<BaseEvent>("base.event", 1)]
        );
        Assert.Throws<InvalidOperationException>(() =>
            codec.Serialize(new DerivedEvent(1, "source"))
        );
    }

    [Fact]
    public void RegistrationCannotDispatchAnAbstractEventType()
    {
        Assert.Throws<ArgumentException>(() =>
            EventRegistration<IEvent>.For<AbstractEvent>("abstract.event", 1)
        );
    }

    [Fact]
    public void ConverterBugIsNotDisguisedAsStoredPayloadCorruption()
    {
        var expected = new InvalidOperationException("Consumer converter failure.");
        var options = Options();
        options.Converters.Add(new BrokenConverter(expected));
        var codec = Codec(options);
        using var payload = JsonDocument.Parse("""{"amountValue":1}""");
        Assert.Same(
            expected,
            Assert.Throws<InvalidOperationException>(() =>
                codec.Deserialize("stock.received", 1, payload.RootElement)
            )
        );
    }

    private sealed class BrokenConverter(InvalidOperationException failure)
        : JsonConverter<RenamedReceipt>
    {
        public override RenamedReceipt Read(
            ref Utf8JsonReader reader,
            Type typeToConvert,
            JsonSerializerOptions options
        ) => throw failure;

        public override void Write(
            Utf8JsonWriter writer,
            RenamedReceipt value,
            JsonSerializerOptions options
        ) => throw new NotSupportedException();
    }
}
