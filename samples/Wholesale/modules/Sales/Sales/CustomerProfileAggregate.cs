using Gloaming.Samples.Wholesale.Sales.Contracts;

namespace Gloaming.Samples.Wholesale.Sales;

// Sales owns these limits and the single-address profile. This value has no persistence behavior.
internal sealed record CustomerProfileDetails
{
    private CustomerProfileDetails(string displayName, string addressLine)
    {
        DisplayName = displayName;
        AddressLine = addressLine;
    }

    internal string DisplayName { get; }
    internal string AddressLine { get; }

    internal static CustomerProfileDetails Create(string displayName, string addressLine)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(displayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(addressLine);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(displayName.Length, 256);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(addressLine.Length, 512);

        return new(displayName, addressLine);
    }
}

// A complete immutable candidate needs no pending-event bookkeeping in this state-stored model.
internal sealed class CustomerProfileAggregate
{
    private CustomerProfileAggregate(
        Guid customerId,
        Guid addressId,
        string code,
        long version,
        CustomerProfileDetails details
    )
    {
        ArgumentOutOfRangeException.ThrowIfEqual(customerId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(addressId, Guid.Empty);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(code.Length, 128);
        ArgumentOutOfRangeException.ThrowIfLessThan(version, 1);
        ArgumentNullException.ThrowIfNull(details);

        CustomerId = customerId;
        AddressId = addressId;
        Code = code;
        Version = version;
        Details = details;
    }

    internal Guid CustomerId { get; }
    internal Guid AddressId { get; }
    internal string Code { get; }
    internal long Version { get; }
    internal CustomerProfileDetails Details { get; }

    internal static CustomerProfileAggregate Create(
        Guid customerId,
        Guid addressId,
        string code,
        CustomerProfileDetails details
    ) => new(customerId, addressId, code, 1, details);

    internal static CustomerProfileAggregate FromState(CustomerProfile profile)
    {
        ArgumentNullException.ThrowIfNull(profile);

        return new(
            profile.CustomerId,
            profile.AddressId,
            profile.Code,
            profile.Version,
            CustomerProfileDetails.Create(profile.DisplayName, profile.AddressLine)
        );
    }

    internal CustomerProfileAggregate Change(CustomerProfileDetails details) =>
        new(CustomerId, AddressId, Code, checked(Version + 1), details);

    internal CustomerProfile ToProfile() =>
        new(CustomerId, AddressId, Code, Details.DisplayName, Details.AddressLine, Version);
}
