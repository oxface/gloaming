using System.Text.Json.Serialization;

namespace ModulithFoundry.Samples.Wholesale.Sales.Contracts;

// Sales owns this whole-state contract. Revision is the profile's explicitly published
// directory revision, including withdrawal/relisting. Keys are never physically removed/reused.
public sealed record CustomerDeliveryState(
    [property: JsonRequired] Guid CustomerId,
    [property: JsonRequired] long Revision,
    [property: JsonRequired] bool Listed,
    string? Code,
    string? DisplayName,
    string? AddressLine
);

public sealed record CustomerDeliveryPage(
    string Organization,
    IReadOnlyList<CustomerDeliveryState> Entries,
    Guid? Next,
    bool Complete
);

public sealed record RegisterDeliveryCustomer(
    Guid CustomerId,
    Guid AddressId,
    string Code,
    string DisplayName,
    string AddressLine
);

public interface ICustomerDeliveryDirectory
{
    Task<CustomerDeliveryPage> ReadPageAsync(Guid? after, int limit, CancellationToken token);
    Task<CustomerProfile> RegisterAsync(RegisterDeliveryCustomer customer, CancellationToken token);
    Task<ProfileChangeResult> SetListedAsync(
        Guid customerId,
        long expectedRevision,
        bool listed,
        CancellationToken token
    );
}
