using ModulithFoundry.Samples.Wholesale.IntegrationTests.Scenarios.Ddd;
using ModulithFoundry.Samples.Wholesale.Purchasing.Contracts;
using ModulithFoundry.Samples.Wholesale.Purchasing.PurchaseOrders;
using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;

namespace ModulithFoundry.Samples.Wholesale.IntegrationTests.Domain;

public sealed class DomainDecisionTests
{
    [Fact]
    public void ProfileValuesCompareByContentsWithoutNormalizingAcceptedText()
    {
        var details = CustomerProfileDetails.Create(" Customer ", " Street ");
        Assert.Equal(details, CustomerProfileDetails.Create(" Customer ", " Street "));
        Assert.NotEqual(details, CustomerProfileDetails.Create("Customer", "Street"));
        Assert.Equal(" Customer ", details.DisplayName);
        Assert.Equal(" Street ", details.AddressLine);
        Assert.Equal(
            256,
            CustomerProfileDetails
                .Create(new string('n', 256), new string('a', 512))
                .DisplayName.Length
        );
    }

    [Theory]
    [InlineData(true, " ")]
    [InlineData(false, " ")]
    [InlineData(true, null)]
    [InlineData(false, null)]
    public void InvalidProfileFieldCannotPartiallyReplaceAcceptedState(bool name, string? invalid)
    {
        var aggregate = CreateProfile();
        var before = aggregate.ToProfile();
        Assert.ThrowsAny<ArgumentException>(() =>
            aggregate.Change(
                CustomerProfileDetails.Create(
                    name ? invalid! : "Replacement name",
                    name ? "Replacement address" : invalid!
                )
            )
        );
        Assert.Equal(before, aggregate.ToProfile());
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ProfileFieldLengthLimitsRejectBeforeAcceptingAnyChange(bool name)
    {
        var aggregate = CreateProfile();
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            aggregate.Change(
                CustomerProfileDetails.Create(
                    name ? new string('n', 257) : "Replacement name",
                    name ? "Replacement address" : new string('a', 513)
                )
            )
        );
        Assert.Equal("Customer", aggregate.Details.DisplayName);
        Assert.Equal("Street", aggregate.Details.AddressLine);
        Assert.Equal(1, aggregate.Version);
    }

    [Fact]
    public void AcceptedStateStoredCandidatePreservesIdentityAndLeavesObservedStateUnchanged()
    {
        var aggregate = CreateProfile();
        var changed = aggregate.Change(CustomerProfileDetails.Create("New name", "New street"));
        Assert.Equal(
            new CustomerProfile(
                aggregate.CustomerId,
                aggregate.AddressId,
                "CUSTOMER-1",
                "New name",
                "New street",
                2
            ),
            changed.ToProfile()
        );
        Assert.Equal("Customer", aggregate.Details.DisplayName);
        Assert.Equal("Street", aggregate.Details.AddressLine);
        Assert.Equal(1, aggregate.Version);
    }

    [Fact]
    public void StateStoredVersionOverflowPreservesTheObservedAggregate()
    {
        var profile = CreateProfile().ToProfile() with { Version = long.MaxValue };
        var aggregate = CustomerProfileAggregate.FromState(profile);
        Assert.Throws<OverflowException>(() =>
            aggregate.Change(CustomerProfileDetails.Create("New name", "New street"))
        );
        Assert.Equal(profile, aggregate.ToProfile());
    }

    [Theory]
    [InlineData("customer")]
    [InlineData("address")]
    [InlineData("code")]
    public void CreationRejectsIncompleteProfileIdentity(string field)
    {
        Assert.ThrowsAny<ArgumentException>(() =>
            CustomerProfileAggregate.Create(
                field == "customer" ? Guid.Empty : Guid.NewGuid(),
                field == "address" ? Guid.Empty : Guid.NewGuid(),
                field == "code" ? " " : "CUSTOMER-1",
                CustomerProfileDetails.Create("Customer", "Street")
            )
        );
    }

    [Theory]
    [InlineData("quantity")]
    [InlineData("price")]
    [InlineData("overflow")]
    public void RejectedOrderBatchPreservesAcceptedStateVersionAndPendingFacts(string fault)
    {
        var aggregate = PurchaseOrderAggregate.Create(
            new DraftPurchaseOrder(Guid.NewGuid(), "ORDER-1", "SUPPLIER-1", "EUR")
        );
        aggregate.SetLines([new PurchaseOrderLine("ITEM-1", 2, 5)]);
        var accepted = aggregate.State;
        var pending = aggregate.PendingEvents.ToArray();
        var batch = new[]
        {
            new PurchaseOrderLine("ITEM-1", 3, 5),
            new PurchaseOrderLine(
                "ITEM-2",
                fault == "quantity" ? 0
                    : fault == "overflow" ? decimal.MaxValue
                    : 1,
                fault == "price" ? -1 : 2
            ),
        };
        if (fault == "overflow")
            Assert.Throws<OverflowException>(() => aggregate.SetLines(batch));
        else
            Assert.Throws<ArgumentOutOfRangeException>(() => aggregate.SetLines(batch));

        Assert.Same(accepted, aggregate.State);
        Assert.Equal(2, aggregate.Version);
        Assert.Equal(pending, aggregate.PendingEvents);
        Assert.Equal(10, aggregate.State!.Total);
        Assert.Equal(new PurchaseOrderLine("ITEM-1", 2, 5), Assert.Single(aggregate.State.Lines));
    }

    [Fact]
    public void OrderRulesValidateTheFinalBatchRatherThanTemporaryTotals()
    {
        var aggregate = PurchaseOrderAggregate.Create(
            new DraftPurchaseOrder(Guid.NewGuid(), "ORDER-1", "SUPPLIER-1", "EUR")
        );
        aggregate.SetLines([
            new PurchaseOrderLine("ITEM-1", decimal.MaxValue, 1),
            new PurchaseOrderLine("ITEM-2", 1, 1),
            new PurchaseOrderLine("ITEM-1", 2, 1),
        ]);
        Assert.Equal(3, aggregate.State!.Total);
        Assert.Equal(4, aggregate.Version);
        Assert.Equal(4, aggregate.PendingEvents.Count);
    }

    private static CustomerProfileAggregate CreateProfile() =>
        CustomerProfileAggregate.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "CUSTOMER-1",
            CustomerProfileDetails.Create("Customer", "Street")
        );
}
