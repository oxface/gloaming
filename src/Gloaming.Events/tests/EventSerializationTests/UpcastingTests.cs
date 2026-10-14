using System.Text.Json;
using Gloaming.Events.Serialization;

namespace Gloaming.EventSerializationTests;

public sealed class UpcastingTests
{
    private interface IEvent;

    private sealed record Receipt(decimal Quantity, string? DeliveryReference = null) : IEvent;

    private sealed record OldReceipt(decimal Amount) : IEvent;

    private sealed record RequiredReceipt(decimal Quantity, string DeliveryReference) : IEvent;

    private const string Name = "stock.received";

    private static JsonSerializerOptions Options() =>
        new(JsonSerializerDefaults.Web)
        {
            RespectNullableAnnotations = true,
            RespectRequiredConstructorParameters = true,
        };

    private static JsonEventCodec<IEvent> Codec(params JsonEventUpcaster[] steps) =>
        new(Options(), [EventRegistration<IEvent>.For<Receipt>(Name, 3)], steps);

    private sealed class RenameAmount(int from = 1, int to = 2) : JsonEventUpcaster(Name, from, to)
    {
        public override JsonElement Upcast(JsonElement payload) =>
            JsonSerializer.SerializeToElement(
                new { quantity = payload.GetProperty("amount").GetDecimal() }
            );
    }

    [Fact]
    public void TerminalDecodeAndCurrentWritesUseTheFrozenNativeOptions()
    {
        var options = Options();
        options.PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower;
        var codec = new JsonEventCodec<IEvent>(
            options,
            [EventRegistration<IEvent>.For<Receipt>(Name, 3)],
            [JsonEventUpcaster.PassThrough(Name, 1, 3)]
        );
        options.PropertyNamingPolicy = null;
        using var payload = JsonDocument.Parse("""{"quantity":7.5,"delivery_reference":"DEL-1"}""");
        var expected = new Receipt(7.5m, "DEL-1");
        Assert.Equal(expected, codec.Deserialize(Name, 1, payload.RootElement));
        Assert.Equal(
            "DEL-1",
            codec.Serialize(expected).Payload.GetProperty("delivery_reference").GetString()
        );
    }

    [Fact]
    public void ChainAndIntermediateSourcesReachCurrentTypeAndWritesKeepCurrentIdentity()
    {
        var codec = Codec(JsonEventUpcaster.PassThrough(Name, 2, 3), new RenameAmount());
        using var old = JsonDocument.Parse("""{"amount":10.125}""");
        using var intermediate = JsonDocument.Parse("""{"quantity":10.125}""");
        var expected = new Receipt(10.125m);
        Assert.Equal(expected, codec.Deserialize(Name, 1, old.RootElement));
        Assert.Equal(expected, codec.Deserialize(Name, 2, intermediate.RootElement));
        Assert.Equal(expected, codec.Deserialize(Name, 3, intermediate.RootElement));
        var encoded = codec.Serialize(expected);
        Assert.Equal(3, encoded.SchemaVersion);
        Assert.Equal(Name, encoded.EventName);
        Assert.Equal(10.125m, encoded.Payload.GetProperty("quantity").GetDecimal());
        Assert.Equal("{\"amount\":10.125}", old.RootElement.GetRawText());
    }

    [Fact]
    public void ExplicitJumpDoesNotInventIntermediateVersions()
    {
        var codec = Codec(new RenameAmount(1, 3));
        using var payload = JsonDocument.Parse("""{"amount":2.5}""");
        Assert.Equal(new Receipt(2.5m), codec.Deserialize(Name, 1, payload.RootElement));
        Assert.Equal(
            EventDecodingFailure.UnknownEvent,
            Assert
                .Throws<EventDecodingException>(() =>
                    codec.Deserialize(Name, 2, payload.RootElement)
                )
                .Failure
        );
    }

    [Fact]
    public void PassThroughUsesOptionalDefaultsWithoutAddingJsonProperties()
    {
        var codec = Codec(JsonEventUpcaster.PassThrough(Name, 1, 3));
        using var payload = JsonDocument.Parse("""{"quantity":2.5}""");
        var receipt = Assert.IsType<Receipt>(codec.Deserialize(Name, 1, payload.RootElement));
        Assert.Null(receipt.DeliveryReference);
        Assert.False(payload.RootElement.TryGetProperty("deliveryReference", out _));
        Assert.Equal(3, codec.Serialize(receipt).SchemaVersion);
    }

    [Theory]
    [InlineData("{}")]
    [InlineData("{\"quantity\":1}")]
    [InlineData("{\"quantity\":1,\"deliveryReference\":null}")]
    public void PassThroughDoesNotBypassNativeRequiredFieldsOrNullability(string json)
    {
        var codec = new JsonEventCodec<IEvent>(
            Options(),
            [EventRegistration<IEvent>.For<RequiredReceipt>(Name, 2)],
            [JsonEventUpcaster.PassThrough(Name, 1, 2)]
        );
        using var payload = JsonDocument.Parse(json);
        var failure = Assert.Throws<EventDecodingException>(() =>
            codec.Deserialize(Name, 1, payload.RootElement)
        );
        Assert.Equal(EventDecodingFailure.InvalidPayload, failure.Failure);
        Assert.Equal(1, failure.SchemaVersion);
        Assert.IsType<JsonException>(failure.InnerException);
    }

    [Fact]
    public void ExactRegistrationsForDifferentClrSchemasRemainIndependent()
    {
        var codec = new JsonEventCodec<IEvent>(
            Options(),
            [
                EventRegistration<IEvent>.For<OldReceipt>(Name, 1),
                EventRegistration<IEvent>.For<Receipt>(Name, 3),
            ]
        );
        using var old = JsonDocument.Parse("""{"amount":2.5}""");
        Assert.Equal(new OldReceipt(2.5m), codec.Deserialize(Name, 1, old.RootElement));
        Assert.Equal(1, codec.Serialize(new OldReceipt(2.5m)).SchemaVersion);
        Assert.Equal(3, codec.Serialize(new Receipt(2.5m)).SchemaVersion);
    }

    [Theory]
    [InlineData("stock.received", 0)]
    [InlineData("stock.received", 2)]
    [InlineData("stock.received", 4)]
    [InlineData("Stock.Received", 1)]
    [InlineData("unknown", 1)]
    public void UndeclaredIdentityFailsBeforePayloadInspection(string name, int version)
    {
        var failure = Assert.Throws<EventDecodingException>(() =>
            Codec(new RenameAmount(1, 3)).Deserialize(name, version, default)
        );
        Assert.Equal(EventDecodingFailure.UnknownEvent, failure.Failure);
        Assert.Equal(name, failure.EventName);
        Assert.Equal(version, failure.SchemaVersion);
    }

    [Theory]
    [InlineData("", 1, 2)]
    [InlineData(" ", 1, 2)]
    [InlineData("stock.received", 0, 2)]
    [InlineData("stock.received", 2, 2)]
    [InlineData("stock.received", 2, 1)]
    public void MetadataMustDescribeOneForwardStep(string name, int from, int to) =>
        Assert.ThrowsAny<ArgumentException>(() => JsonEventUpcaster.PassThrough(name, from, to));

    [Fact]
    public void NullNamesAndStepsAreCallerErrors()
    {
        Assert.Throws<ArgumentNullException>(() => JsonEventUpcaster.PassThrough(null!, 1, 2));
        Assert.Throws<ArgumentNullException>(() => Codec([null!]));
    }

    [Fact]
    public void DuplicateSourcesCannotBranchOrDependOnOrder()
    {
        var first = JsonEventUpcaster.PassThrough(Name, 1, 2);
        var second = JsonEventUpcaster.PassThrough(Name, 1, 3);
        var terminal = JsonEventUpcaster.PassThrough(Name, 2, 3);
        Assert.Equal(
            "upcasters",
            Assert.Throws<ArgumentException>(() => Codec(first, second, terminal)).ParamName
        );
        Assert.Throws<ArgumentException>(() => Codec(second, terminal, first));
    }

    [Fact]
    public void ExactRegistrationCannotBeShadowedByAnUpcaster() =>
        Assert.Throws<ArgumentException>(() => Codec(JsonEventUpcaster.PassThrough(Name, 3, 4)));

    [Theory]
    [InlineData("stock.received", 1, 2)]
    [InlineData("stock.received", 1, 4)]
    [InlineData("other.event", 1, 3)]
    public void EveryDeclaredPathMustReachItsOwnRegisteredTerminal(string name, int from, int to) =>
        Assert.Throws<ArgumentException>(() =>
            Codec(JsonEventUpcaster.PassThrough(name, from, to))
        );

    [Fact]
    public void RegistrationOrderAndLaterCollectionChangesCannotRebindPaths()
    {
        List<JsonEventUpcaster> steps =
        [
            new RenameAmount(),
            JsonEventUpcaster.PassThrough(Name, 2, 3),
        ];
        var codec = new JsonEventCodec<IEvent>(
            Options(),
            [EventRegistration<IEvent>.For<Receipt>(Name, 3)],
            steps
        );
        var reversed = Codec(steps.AsEnumerable().Reverse().ToArray());
        steps.Clear();
        using var payload = JsonDocument.Parse("""{"amount":4.125}""");
        Assert.Equal(new Receipt(4.125m), codec.Deserialize(Name, 1, payload.RootElement));
        Assert.Equal(new Receipt(4.125m), reversed.Deserialize(Name, 1, payload.RootElement));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NullOrUndefinedIntermediateFailsUnderOriginalIdentity(bool nullPayload)
    {
        var codec = Codec(new EmptyResult(nullPayload));
        using var payload = JsonDocument.Parse("{}");
        var failure = Assert.Throws<EventDecodingException>(() =>
            codec.Deserialize(Name, 1, payload.RootElement)
        );
        Assert.Equal(EventDecodingFailure.InvalidPayload, failure.Failure);
        Assert.Equal(Name, failure.EventName);
        Assert.Equal(1, failure.SchemaVersion);
    }

    private sealed class EmptyResult(bool nullPayload) : JsonEventUpcaster(Name, 1, 3)
    {
        public override JsonElement Upcast(JsonElement payload) =>
            nullPayload ? JsonSerializer.SerializeToElement<object?>(null) : default;
    }

    [Theory]
    [InlineData("null")]
    [InlineData("{\"quantity\":\"private\"}")]
    public void InvalidSourceOrTerminalRetainsPersistedIdentity(string json)
    {
        using var payload = JsonDocument.Parse(json);
        var failure = Assert.Throws<EventDecodingException>(() =>
            Codec(JsonEventUpcaster.PassThrough(Name, 1, 3))
                .Deserialize(Name, 1, payload.RootElement)
        );
        Assert.Equal(EventDecodingFailure.InvalidPayload, failure.Failure);
        Assert.Equal(1, failure.SchemaVersion);
        Assert.DoesNotContain("private", failure.Message);
    }

    [Fact]
    public void JsonTransformFailureIsClassifiedButProgrammerFailurePropagates()
    {
        using var payload = JsonDocument.Parse("{}");
        var malformed = new JsonException("Invalid old shape.");
        var failure = Assert.Throws<EventDecodingException>(() =>
            Codec(new FailingStep(malformed)).Deserialize(Name, 1, payload.RootElement)
        );
        Assert.Same(malformed, failure.InnerException);
        Assert.Equal(1, failure.SchemaVersion);
        var bug = new InvalidOperationException("Consumer failure.");
        Assert.Same(
            bug,
            Assert.Throws<InvalidOperationException>(() =>
                Codec(new FailingStep(bug)).Deserialize(Name, 1, payload.RootElement)
            )
        );
    }

    private sealed class FailingStep(Exception failure) : JsonEventUpcaster(Name, 1, 3)
    {
        public override JsonElement Upcast(JsonElement payload) => throw failure;
    }

    [Fact]
    public void InputAndIntermediateElementsOwnTheirDocumentLifetimes()
    {
        using var source = JsonDocument.Parse("""{"quantity":7.5}""");
        using var intermediate = JsonDocument.Parse("""{"quantity":7.5}""");
        var codec = Codec(
            new BorrowedResult(source, intermediate),
            new DisposePrevious(intermediate)
        );
        var receipt = codec.Deserialize(Name, 1, source.RootElement);
        Assert.Equal(new Receipt(7.5m), receipt);
    }

    private sealed class BorrowedResult(JsonDocument source, JsonDocument result)
        : JsonEventUpcaster(Name, 1, 2)
    {
        public override JsonElement Upcast(JsonElement payload)
        {
            source.Dispose();
            Assert.Equal(7.5m, payload.GetProperty("quantity").GetDecimal());
            return result.RootElement;
        }
    }

    private sealed class DisposePrevious(JsonDocument previous) : JsonEventUpcaster(Name, 2, 3)
    {
        public override JsonElement Upcast(JsonElement payload)
        {
            previous.Dispose();
            Assert.Equal(7.5m, payload.GetProperty("quantity").GetDecimal());
            return payload;
        }
    }

    [Fact]
    public void AlreadyDisposedConsumerResultIsNotDisguisedAsBadStoredJson()
    {
        using var payload = JsonDocument.Parse("{}");
        Assert.Throws<ObjectDisposedException>(() =>
            Codec(new DisposedResult()).Deserialize(Name, 1, payload.RootElement)
        );
    }

    private sealed class DisposedResult() : JsonEventUpcaster(Name, 1, 3)
    {
        public override JsonElement Upcast(JsonElement payload)
        {
            using var document = JsonDocument.Parse("{}");
            return document.RootElement;
        }
    }

    [Fact]
    public async Task EstablishedStatelessPathsSupportConcurrentDecoding()
    {
        var codec = Codec(new RenameAmount(), JsonEventUpcaster.PassThrough(Name, 2, 3));
        var results = await Task.WhenAll(
            Enumerable
                .Range(0, 64)
                .Select(index =>
                    Task.Run(() =>
                    {
                        using var payload = JsonDocument.Parse("""{"amount":10.125}""");
                        return codec.Deserialize(Name, 1, payload.RootElement);
                    })
                )
        );
        Assert.All(results, result => Assert.Equal(new Receipt(10.125m), result));
    }
}
