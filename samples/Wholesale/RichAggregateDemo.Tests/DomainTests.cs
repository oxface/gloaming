using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;
using ModulithFoundry.Samples.Wholesale.Sales.DraftOrders;

namespace ModulithFoundry.Samples.Wholesale.RichAggregateDemo.Tests;

public sealed class DomainTests
{
    private static readonly Guid First = Guid.Parse("d1100000-0000-0000-0000-000000000011");
    private static readonly Guid Second = Guid.Parse("d1100000-0000-0000-0000-000000000012");

    [Fact]
    public void MoneyHasValueEqualityAndRejectsImplicitCurrencyNormalization()
    {
        Assert.Equal(Money.Create(12.5m, "EUR"), Money.Create(12.5m, "EUR"));
        Assert.NotEqual(Money.Create(12.5m, "EUR"), Money.Create(12.5m, "USD"));
        Assert.Throws<ArgumentException>(() => Money.Create(1, "eur"));
        Assert.Throws<ArgumentOutOfRangeException>(() => Money.Create(-1, "EUR"));
    }

    [Fact]
    public void ChildAddChangeRemoveProducesOneImmutableRootCandidate()
    {
        var observed = Create();
        Guid newId = Guid.NewGuid();
        var candidate = observed.ChangeLines([
            new DraftOrderLineChange.Change(First, 3, 10),
            new DraftOrderLineChange.Add(new(newId, "PRODUCT-2", 1, 15)),
            new DraftOrderLineChange.Remove(Second),
        ]);
        Assert.Equal(1, observed.Version);
        Assert.Equal(40, observed.Total.Amount);
        Assert.Equal(2, candidate.Version);
        Assert.Equal(45, candidate.Total.Amount);
        Assert.Equal(observed.CustomerId, candidate.CustomerId);
        Assert.Equal(observed.Limit, candidate.Limit);
        Assert.Equal(
            new DraftOrderLine(First, "PRODUCT-1", 3, 10),
            candidate.Lines.Single(line => line.Id == First).ToView()
        );
        Assert.DoesNotContain(candidate.Lines, line => line.Id == Second);
        Assert.Equal(
            new DraftOrderLine(newId, "PRODUCT-2", 1, 15),
            candidate.Lines.Single(line => line.Id == newId).ToView()
        );
    }

    [Theory]
    [InlineData("limit")]
    [InlineData("quantity")]
    [InlineData("price")]
    [InlineData("missing")]
    [InlineData("empty")]
    [InlineData("overflow")]
    [InlineData("reuse")]
    public void ARejectedLaterChangePreservesAllObservedChildrenAndVersion(string fault)
    {
        var aggregate = Create();
        var children = aggregate.Lines.ToArray();
        var changes = new List<DraftOrderLineChange>
        {
            new DraftOrderLineChange.Change(First, 3, 10),
        };
        changes.AddRange(
            fault switch
            {
                "limit" => [new DraftOrderLineChange.Change(Second, 4, 20)],
                "quantity" => [new DraftOrderLineChange.Change(Second, 0, 20)],
                "price" => [new DraftOrderLineChange.Change(Second, 1, -1)],
                "missing" => [new DraftOrderLineChange.Remove(Guid.NewGuid())],
                "empty" =>
                [
                    new DraftOrderLineChange.Remove(First),
                    new DraftOrderLineChange.Remove(Second),
                ],
                "overflow" => [new DraftOrderLineChange.Change(Second, decimal.MaxValue, 2)],
                "reuse" =>
                [
                    new DraftOrderLineChange.Remove(Second),
                    new DraftOrderLineChange.Add(new(Second, "PRODUCT-2", 1, 10)),
                ],
                _ => throw new InvalidOperationException(),
            }
        );
        var exception = Record.Exception(() => aggregate.ChangeLines(changes));
        Assert.NotNull(exception);
        if (fault == "limit")
        {
            var refusal = Assert.IsType<OrderLimitExceededException>(exception);
            Assert.Equal(100, refusal.Limit);
            Assert.Equal(110, refusal.Requested);
        }
        else if (fault == "overflow")
            Assert.IsType<OverflowException>(exception);
        else
            Assert.IsAssignableFrom<ArgumentException>(exception);

        Assert.Equal(1, aggregate.Version);
        Assert.Equal(40, aggregate.Total.Amount);
        Assert.Equal(children, aggregate.Lines);
    }

    [Fact]
    public void OnlyTheCompleteBatchMustContainChildrenAndFitTheLimit()
    {
        var aggregate = Create();
        var replacement = aggregate.ChangeLines([
            new DraftOrderLineChange.Change(First, 100, 10),
            new DraftOrderLineChange.Remove(First),
            new DraftOrderLineChange.Remove(Second),
            new DraftOrderLineChange.Add(new(Guid.NewGuid(), "PRODUCT-2", 2, 15)),
        ]);
        Assert.Equal(30, replacement.Total.Amount);
        Assert.Single(replacement.Lines);
        Assert.Equal(2, replacement.Version);
    }

    [Fact]
    public void VersionOverflowCannotAcceptAChildEdit()
    {
        var aggregate = Create();
        var max = DraftOrderAggregate.FromState(
            aggregate.Id,
            aggregate.CustomerId,
            aggregate.Code,
            long.MaxValue,
            "EUR",
            100,
            aggregate.Lines.Select(line => line.ToView())
        );
        Assert.Throws<OverflowException>(() =>
            max.ChangeLines([new DraftOrderLineChange.Change(First, 3, 10)])
        );
        Assert.Equal(long.MaxValue, max.Version);
        Assert.Equal(40, max.Total.Amount);
    }

    [Theory]
    [InlineData("empty")]
    [InlineData("duplicate")]
    [InlineData("currency")]
    [InlineData("limit")]
    public void CreationRequiresACompleteValidGraph(string fault)
    {
        DraftOrderLine[] lines = fault switch
        {
            "empty" => [],
            "duplicate" => [new(First, "PRODUCT-1", 1, 10), new(First, "PRODUCT-1", 2, 10)],
            _ => [new(First, "PRODUCT-1", 1, 10)],
        };
        Assert.ThrowsAny<ArgumentException>(() =>
            DraftOrderAggregate.Create(
                new(
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    "ORDER-1",
                    fault == "currency" ? "eur" : "EUR",
                    fault == "limit" ? 0 : 100,
                    lines
                )
            )
        );
    }

    private static DraftOrderAggregate Create() =>
        DraftOrderAggregate.Create(
            new(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "ORDER-1",
                "EUR",
                100,
                [new(First, "PRODUCT-1", 2, 10), new(Second, "PRODUCT-1", 1, 20)]
            )
        );
}
