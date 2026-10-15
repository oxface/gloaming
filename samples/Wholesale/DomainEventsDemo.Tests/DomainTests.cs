using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Samples.Wholesale.Sales.DraftOrders;
using ModulithFoundry.Samples.Wholesale.Sales.OrderAllowances;

namespace ModulithFoundry.Samples.Wholesale.EventsDemo.Tests;

public sealed class DomainTests
{
    [Fact]
    public void OnlyAcceptedSubmissionProducesThePrivateFactAndHydrationDoesNot()
    {
        var draft = DraftOrderAggregate.Create(
            DomainEventJourney.Request(DomainEventJourney.FirstOrderId, 40)
        );
        var (submitted, fact) = draft.Submit();
        Assert.Equal(
            new DraftSubmitted(draft.Id, draft.CustomerId, 2, Money.Create(40, "EUR")),
            fact
        );
        Assert.False(draft.IsSubmitted);
        Assert.Equal(1, draft.Version);
        Assert.True(submitted.IsSubmitted);
        Assert.Throws<InvalidOperationException>(() => submitted.Submit());
        Assert.Throws<InvalidOperationException>(() =>
            submitted.ChangeLines([
                new Sales.Contracts.DraftOrderLineChange.Change(DomainEventJourney.LineId, 1, 20),
            ])
        );
        var restored = DraftOrderAggregate.FromState(
            submitted.Id,
            submitted.CustomerId,
            submitted.Code,
            submitted.Version,
            "EUR",
            100,
            submitted.ToView().Lines,
            true
        );
        Assert.True(restored.IsSubmitted);
    }

    [Fact]
    public void SeparateAllowanceDecidesAgainstItsCompleteReservedAmount()
    {
        var allowance = new OrderAllowanceAggregate(
            Guid.NewGuid(),
            Money.Create(100, "EUR"),
            Money.Create(0, "EUR"),
            1
        );
        var reserved = allowance.Reserve(Money.Create(40, "EUR"));
        Assert.Equal(0, allowance.Reserved.Amount);
        Assert.Equal(40, reserved.Reserved.Amount);
        Assert.Equal(2, reserved.Version);
        var failure = Assert.Throws<OrderAllowanceExceededException>(() =>
            reserved.Reserve(Money.Create(70, "EUR"))
        );
        Assert.Equal(110, failure.Requested);
        Assert.Equal(40, reserved.Reserved.Amount);
        Assert.Throws<ArgumentException>(() => reserved.Reserve(Money.Create(10, "USD")));
        Assert.Equal(100, reserved.Reserve(Money.Create(60, "EUR")).Reserved.Amount);
    }
}
