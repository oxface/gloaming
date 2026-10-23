using System.Text.Json;
using Gloaming.Events.Serialization;
using ModulithFoundry.Samples.Wholesale.EventCodecDemo.Inventory;
using ModulithFoundry.Samples.Wholesale.EventCodecDemo.Purchasing;

namespace ModulithFoundry.Samples.Wholesale.EventCodecDemo.Tests;

public sealed class CompatibilityTests
{
    private static string Fixture(string family, string name) =>
        Path.Combine(AppContext.BaseDirectory, "Fixtures", family, name);

    [Fact]
    public async Task NativeCodecExecutableReadsBothLiteralSchemasAndWritesTheCurrentShape()
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(
            TestContext.Current.CancellationToken
        );
        deadline.CancelAfter(TimeSpan.FromSeconds(60));
        await using var child = new ModulithFoundry.Tests.Infrastructure.ChildHost(
            typeof(CodecExample).Assembly,
            new()
        );
        Assert.True(await child.WaitForExitAsync(deadline.Token) == 0, child.Output);
        Assert.Contains(
            "receipt: v1=10.125, v2=10.125, write-schema=2",
            child.Output,
            StringComparison.Ordinal
        );
    }

    [Fact]
    public void RetainedStockLiteralsDecodeRenamedReceiptAndProduceExpectedQuantity()
    {
        var codec = StockPositionExample.CreateCodec();
        var events = FixtureEvents
            .Read(
                codec,
                Fixture("Inventory", "opened.v1.json"),
                Fixture("Inventory", "received.v1.json")
            )
            .ToArray();
        var received = Assert.IsType<StockPositionReceived>(events[1]);
        Assert.Null(received.DeliveryReference);
        var state = StockPositionExample.Read(events);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), state.StockItemId);
        Assert.Equal(Guid.Parse("22222222-2222-2222-2222-222222222222"), state.StockingLocationId);
        Assert.Equal("EA", state.BaseUnitCode);
        Assert.Equal(10.125m, state.OnHand);
        Assert.Null(state.LatestDeliveryReference);
        var encoded = codec.Serialize(received);
        Assert.Equal("inventory.stock-position.received", encoded.EventName);
        Assert.Equal(1, encoded.SchemaVersion);
        Assert.Equal(10.125m, encoded.Payload.GetProperty("quantity").GetDecimal());
    }

    [Fact]
    public void PurchasingLiteralsProduceExpectedDraftLineAndTotal()
    {
        var events = FixtureEvents.Read(
            PurchaseOrderExample.CreateCodec(),
            Fixture("Purchasing", "drafted.v1.json"),
            Fixture("Purchasing", "line-set.v1.json")
        );
        var order = PurchaseOrderExample.Read(events);
        Assert.Equal("OLD-1", order.Code);
        Assert.Equal("SUP-1", order.SupplierReference);
        Assert.Equal("EUR", order.Currency);
        var line = Assert.Single(order.Lines);
        Assert.Equal("ITEM-1", line.ItemCode);
        Assert.Equal(2.5m, line.Quantity);
        Assert.Equal(12.5m, line.UnitPrice);
        Assert.Equal(31.25m, order.Total);
    }

    [Fact]
    public void FixtureRecipesRunBothFamiliesWithExplicitRegistration()
    {
        using var output = new StringWriter();
        DemoJourneys.Run(Path.Combine(AppContext.BaseDirectory, "Fixtures"), output);
        Assert.Equal(
            "inventory: item=11111111-1111-1111-1111-111111111111, location=22222222-2222-2222-2222-222222222222, unit=EA, on-hand=10.125"
                + Environment.NewLine
                + "purchasing: code=OLD-1, supplier=SUP-1, currency=EUR, total=31.25"
                + Environment.NewLine
                + "inventory history: head=3, current=13.000, at-version-2=10.125, at-cutoff=10.125, before-open=none"
                + Environment.NewLine
                + "purchasing history: head=3, current-total=62.50, at-version-2=31.25, at-cutoff=62.50, before-draft=none"
                + Environment.NewLine,
            output.ToString()
        );
    }

    [Theory]
    [InlineData("inventory.stock-position.received", "{}")]
    [InlineData(
        "inventory.stock-position.opened",
        "{\"stockItemId\":\"11111111-1111-1111-1111-111111111111\",\"stockingLocationId\":\"22222222-2222-2222-2222-222222222222\",\"baseUnitCode\":null}"
    )]
    public void InventoryPayloadRequirementsSurviveSharedCodecDispatch(string name, string json)
    {
        using var payload = JsonDocument.Parse(json);
        var failure = Assert.Throws<EventDecodingException>(() =>
            StockPositionExample.CreateCodec().Deserialize(name, 1, payload.RootElement)
        );
        Assert.Equal(EventDecodingFailure.InvalidPayload, failure.Failure);
        Assert.IsType<JsonException>(failure.InnerException);
    }

    [Fact]
    public void PurchasingRequiredConstructorFieldCannotBecomeADefaultValue()
    {
        using var payload = JsonDocument.Parse("""{"code":"OLD-1","currency":"EUR"}""");
        var failure = Assert.Throws<EventDecodingException>(() =>
            PurchaseOrderExample
                .CreateCodec()
                .Deserialize("purchasing.purchase-order.drafted", 1, payload.RootElement)
        );
        Assert.Equal(EventDecodingFailure.InvalidPayload, failure.Failure);
        Assert.IsType<JsonException>(failure.InnerException);
    }

    [Fact]
    public void OneFamilyDoesNotDiscoverAnotherFamiliesEvents()
    {
        using var payload = JsonDocument.Parse(
            """{"code":"OLD-1","supplierReference":"SUP-1","currency":"EUR"}"""
        );
        var failure = Assert.Throws<EventDecodingException>(() =>
            StockPositionExample
                .CreateCodec()
                .Deserialize("purchasing.purchase-order.drafted", 1, payload.RootElement)
        );
        Assert.Equal(EventDecodingFailure.UnknownEvent, failure.Failure);
    }
}
