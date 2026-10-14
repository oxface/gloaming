using System.Text.Json;
using Gloaming.Events.History;
using Gloaming.Events.Serialization;
using ModulithFoundry.Samples.Wholesale.EventCodecDemo.Inventory;
using ModulithFoundry.Samples.Wholesale.EventCodecDemo.Purchasing;

namespace ModulithFoundry.Samples.Wholesale.EventCodecDemo.Tests;

public sealed class HistoryCompatibilityTests
{
    private static string Fixtures => Path.Combine(AppContext.BaseDirectory, "Fixtures");

    [Fact]
    public void StockHistoryReconstructsCurrentVersionAndRecordedTimeStates()
    {
        var rows = StockPositionHistoryExample.Load(Fixtures);
        var codec = StockPositionExample.CreateCodec();
        var current = StockPositionHistoryExample.ReadAtVersion(codec, rows, 3, 3)!;
        var earlier = StockPositionHistoryExample.ReadAtVersion(codec, rows, 3, 2)!;
        var cutoff = StockPositionHistoryExample.ReadAsOf(
            codec,
            rows,
            3,
            StockPositionHistoryExample.FirstReceiptAt
        )!;
        Assert.Equal(13m, current.OnHand);
        Assert.Equal("DELIVERY-2", current.LatestDeliveryReference);
        Assert.Equal(Guid.Parse("11111111-1111-1111-1111-111111111111"), current.StockItemId);
        Assert.Equal("EA", current.BaseUnitCode);
        Assert.Equal(10.125m, earlier.OnHand);
        Assert.Null(earlier.LatestDeliveryReference);
        Assert.Equal(earlier, cutoff);
        Assert.Equal(0m, StockPositionHistoryExample.ReadAtVersion(codec, rows, 3, 1)!.OnHand);
        Assert.Null(
            StockPositionHistoryExample.ReadAsOf(
                codec,
                rows,
                3,
                StockPositionHistoryExample.OpenedAt.AddTicks(-1)
            )
        );
    }

    [Fact]
    public void PurchaseHistoryRetainsLineReplacementAndInclusiveEqualTimeSelection()
    {
        var rows = PurchaseOrderHistoryExample.Load(Fixtures);
        var codec = PurchaseOrderExample.CreateCodec();
        var current = PurchaseOrderHistoryExample.ReadAtVersion(codec, rows, 3, 3)!;
        var earlier = PurchaseOrderHistoryExample.ReadAtVersion(codec, rows, 3, 2)!;
        var cutoff = PurchaseOrderHistoryExample.ReadAsOf(
            codec,
            rows,
            3,
            PurchaseOrderHistoryExample.LinesRecordedAt.ToOffset(TimeSpan.FromHours(2))
        )!;
        Assert.Equal("OLD-1", current.Code);
        Assert.Equal("SUP-1", current.SupplierReference);
        Assert.Equal("EUR", current.Currency);
        Assert.Equal(62.5m, current.Total);
        Assert.Equal(5m, Assert.Single(current.Lines).Quantity);
        Assert.Equal(31.25m, earlier.Total);
        Assert.Equal(2.5m, Assert.Single(earlier.Lines).Quantity);
        Assert.Equal(62.5m, cutoff.Total);
        Assert.Equal(5m, Assert.Single(cutoff.Lines).Quantity);
        Assert.Empty(PurchaseOrderHistoryExample.ReadAtVersion(codec, rows, 3, 1)!.Lines);
        Assert.Null(
            PurchaseOrderHistoryExample.ReadAsOf(
                codec,
                rows,
                3,
                PurchaseOrderHistoryExample.DraftedAt.AddTicks(-1)
            )
        );
    }

    [Fact]
    public void RepeatedHydrationDoesNotAccumulateStateOrChangeSourcePayloads()
    {
        var stockRows = StockPositionHistoryExample.Load(Fixtures);
        var stockCodec = StockPositionExample.CreateCodec();
        var orderRows = PurchaseOrderHistoryExample.Load(Fixtures);
        var orderCodec = PurchaseOrderExample.CreateCodec();
        for (int repetition = 0; repetition < 2; repetition++)
        {
            Assert.Equal(
                13m,
                StockPositionHistoryExample.ReadAtVersion(stockCodec, stockRows, 3, 3)!.OnHand
            );
            Assert.Equal(
                62.5m,
                PurchaseOrderHistoryExample.ReadAtVersion(orderCodec, orderRows, 3, 3)!.Total
            );
        }
        Assert.Equal(2.875m, stockRows[2].Event.Payload.GetProperty("quantity").GetDecimal());
        Assert.Equal(5m, orderRows[2].Event.Payload.GetProperty("quantity").GetDecimal());
    }

    [Theory]
    [InlineData("unknown.event", 1, "{}", EventDecodingFailure.UnknownEvent)]
    [InlineData("inventory.stock-position.received", 1, "{}", EventDecodingFailure.InvalidPayload)]
    public void RangeReadsKeepTheExistingCodecFailureBoundary(
        string name,
        int schema,
        string json,
        EventDecodingFailure expected
    )
    {
        var rows = StockPositionHistoryExample.Load(Fixtures);
        using var document = JsonDocument.Parse(json);
        rows[2] = rows[2] with { Event = new(name, schema, document.RootElement) };
        var codec = StockPositionExample.CreateCodec();
        var failure = Assert.Throws<EventDecodingException>(() =>
            StockPositionHistoryExample.ReadAtVersion(codec, rows, 3, 3)
        );
        Assert.Equal(expected, failure.Failure);
        Assert.Equal(name, failure.EventName);
        Assert.Equal(schema, failure.SchemaVersion);
        Assert.Equal(10.125m, StockPositionHistoryExample.ReadAtVersion(codec, rows, 3, 2)!.OnHand);
        Assert.Equal(10.125m, StockPositionHistoryExample.ReadAtVersion(codec, rows, 2, 2)!.OnHand);
    }

    [Fact]
    public void ConsumerDomainSequenceFailureIsNotRelabeledAsHistoryOrCodecCorruption()
    {
        var source = StockPositionHistoryExample.Load(Fixtures);
        RecordedEvent[] rows = [source[0] with { Event = source[1].Event }];
        Assert.Throws<InvalidOperationException>(() =>
            StockPositionHistoryExample.ReadAtVersion(
                StockPositionExample.CreateCodec(),
                rows,
                1,
                1
            )
        );
    }

    [Fact]
    public void EarlierReadsDoNotCertifyMetadataOutsideTheSelectedRange()
    {
        var rows = StockPositionHistoryExample.Load(Fixtures);
        rows[1] = rows[1] with { RecordedAt = StockPositionHistoryExample.OpenedAt.AddSeconds(20) };
        rows[2] = rows[2] with { RecordedAt = StockPositionHistoryExample.OpenedAt.AddSeconds(15) };
        var codec = StockPositionExample.CreateCodec();
        Assert.Equal(10.125m, StockPositionHistoryExample.ReadAtVersion(codec, rows, 3, 2)!.OnHand);
        Assert.Equal(
            0m,
            StockPositionHistoryExample
                .ReadAsOf(codec, rows, 3, StockPositionHistoryExample.OpenedAt.AddSeconds(5))!
                .OnHand
        );
        var failure = Assert.Throws<EventHistoryException>(() =>
            StockPositionHistoryExample.ReadAtVersion(codec, rows, 3, 3)
        );
        Assert.Equal(EventHistoryFailure.RecordedTimeRegression, failure.Failure);
        Assert.Equal(3, failure.ObservedVersion);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4)]
    public void ConsumerVersionSelectionCannotSilentlyClampToTheCapturedHead(long version)
    {
        var rows = StockPositionHistoryExample.Load(Fixtures);
        var codec = StockPositionExample.CreateCodec();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            StockPositionHistoryExample.ReadAtVersion(codec, rows, 3, version)
        );
    }
}
