using Gloaming.Samples.Wholesale.IntegrationTests.Scenarios.Ddd;
using Gloaming.Samples.Wholesale.Sales;
using Gloaming.Samples.Wholesale.Sales.Contracts;

namespace Gloaming.Samples.Wholesale.IntegrationTests.Domain;

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

    private static CustomerProfileAggregate CreateProfile() =>
        CustomerProfileAggregate.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "CUSTOMER-1",
            CustomerProfileDetails.Create("Customer", "Street")
        );
}
