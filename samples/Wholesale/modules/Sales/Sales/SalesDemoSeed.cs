namespace Gloaming.Samples.Wholesale.Sales;

// Stage demonstration rows; the caller establishes tenancy and owns save/commit.
public static class SalesDemoSeed
{
    // Finite product setup, separate from the fixture seed used by domain/failure proofs.
    public static void StageOrderAllowance(SalesDbContext database, decimal ceiling = 1000)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(ceiling);

        var organization = database.RequiredOrganizationKey;
        if (organization is not ("wholesale-alpha" or "wholesale-beta"))
            throw new ArgumentException(
                "The demonstration supports Alpha and Beta only.",
                nameof(database)
            );

        database.Add(
            new OrderAllowances.OrderAllowanceRow
            {
                OrganizationKey = organization,
                CustomerId = organization == "wholesale-alpha" ? AlphaCustomerId : BetaCustomerId,
                Currency = "EUR",
                Ceiling = ceiling,
                Reserved = 0,
                Version = 1,
            }
        );
    }

    public static Guid AlphaCustomerId { get; } =
        Guid.Parse("10000000-0000-0000-0000-000000000001");
    public static Guid BetaCustomerId { get; } = Guid.Parse("10000000-0000-0000-0000-000000000002");
    public static Guid AlphaAddressId { get; } = Guid.Parse("20000000-0000-0000-0000-000000000001");
    public static Guid BetaAddressId { get; } = Guid.Parse("20000000-0000-0000-0000-000000000002");

    public static void Stage(SalesDbContext database)
    {
        ArgumentNullException.ThrowIfNull(database);

        string organization = database.RequiredOrganizationKey;
        bool alpha = organization == "wholesale-alpha";
        if (!alpha && organization != "wholesale-beta")
            throw new ArgumentException(
                "The demonstration supports Alpha and Beta only.",
                nameof(database)
            );

        Guid customerId = alpha ? AlphaCustomerId : BetaCustomerId;
        var profile = CustomerProfileAggregate.Create(
            customerId,
            alpha ? AlphaAddressId : BetaAddressId,
            "DEMO-CUSTOMER",
            CustomerProfileDetails.Create(
                alpha ? "Alpha Customer" : "Beta Customer",
                alpha ? "Alpha Street" : "Beta Street"
            )
        );
        database.Add(
            new CustomerRow
            {
                Id = profile.CustomerId,
                OrganizationKey = organization,
                Code = profile.Code,
                DisplayName = profile.Details.DisplayName,
                Version = profile.Version,
            }
        );
        database.Add(
            new AddressRow
            {
                Id = profile.AddressId,
                CustomerId = profile.CustomerId,
                OrganizationKey = organization,
                AddressLine = profile.Details.AddressLine,
            }
        );
    }
}
