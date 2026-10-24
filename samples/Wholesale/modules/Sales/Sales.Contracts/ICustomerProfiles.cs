namespace Gloaming.Samples.Wholesale.Sales.Contracts;

public interface ICustomerProfiles
{
    Task<CustomerProfile?> ReadAsync(Guid customerId, CancellationToken cancellationToken);
    Task<ProfileChangeResult> ChangeAsync(
        CustomerProfileChange change,
        CancellationToken cancellationToken
    );
}
