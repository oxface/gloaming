using System.Text.Json;
using Gloaming.Events.Serialization;
using ModulithFoundry.Samples.Wholesale.EventCodecDemo.Purchasing;

namespace ModulithFoundry.Samples.Wholesale.EventCodecDemo.Tests;

public sealed class UpcastingCompatibilityTests
{
    private static string Directory =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", "Purchasing");

    [Theory]
    [InlineData("line-set.v1.json")]
    [InlineData("line-set.v2.json")]
    [InlineData("line-set.v3.json")]
    public void IndependentLiteralsConvergeToTheSameNestedFactAndCurrentWriteSchema(string file)
    {
        var codec = PurchaseOrderSchemaEvolutionExample.CreateCodec();
        var decoded = Assert.IsType<NestedPurchaseOrderLineSet>(
            FixtureEvents.Read(codec, Path.Combine(Directory, file)).Single()
        );
        Assert.Equal(new PurchaseOrderLineDetails("ITEM-1", 2.5m, 12.5m), decoded.Line);
        var stored = codec.Serialize(decoded);
        Assert.Equal(3, stored.SchemaVersion);
        Assert.Equal(2.5m, stored.Payload.GetProperty("line").GetProperty("quantity").GetDecimal());
        var order = PurchaseOrderSchemaEvolutionExample.Read(
            FixtureEvents.Read(
                codec,
                Path.Combine(Directory, "drafted.v1.json"),
                Path.Combine(Directory, file)
            )
        );
        Assert.Equal(31.25m, order.Total);
    }

    [Theory]
    [InlineData(1, "{\"itemCode\":\"ITEM-1\",\"quantity\":2.5}")]
    [InlineData(2, "{\"sku\":\"ITEM-1\",\"units\":2.5}")]
    [InlineData(3, "{\"line\":{\"itemCode\":\"ITEM-1\",\"quantity\":2.5}}")]
    [InlineData(3, "{\"line\":null}")]
    public void MissingOrNullFieldsDoNotBecomeInventedFacts(int schema, string json)
    {
        using var document = JsonDocument.Parse(json);
        var failure = Assert.Throws<EventDecodingException>(() =>
            PurchaseOrderSchemaEvolutionExample
                .CreateCodec()
                .Deserialize(
                    PurchaseOrderSchemaEvolutionExample.LineEventName,
                    schema,
                    document.RootElement
                )
        );
        Assert.Equal(EventDecodingFailure.InvalidPayload, failure.Failure);
        Assert.Equal(schema, failure.SchemaVersion);
        Assert.IsType<JsonException>(failure.InnerException);
    }

    [Fact]
    public void SchemaEvolutionRecipeRetainsIndependentTotals()
    {
        using var output = new StringWriter();
        SchemaEvolutionJourneys.Run(Path.Combine(AppContext.BaseDirectory, "Fixtures"), output);
        Assert.Equal(
            "purchasing schemas: v1-to-v3-total=31.25, write-schema=3, replaced-total=62.50"
                + Environment.NewLine,
            output.ToString()
        );
    }
}
