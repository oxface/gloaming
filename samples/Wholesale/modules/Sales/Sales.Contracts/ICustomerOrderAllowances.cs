namespace Gloaming.Samples.Wholesale.Sales.Contracts;

// Opening is proposed setup; the caller owns the native Sales transaction and commit.
public interface ICustomerOrderAllowances
{
    Task OpenAsync(
        Guid customerId,
        string currency,
        decimal ceiling,
        CancellationToken cancellationToken
    );

    Task<CustomerOrderAllowance?> ReadAsync(
        Guid customerId,
        string currency,
        CancellationToken cancellationToken
    );
}

public sealed record CustomerOrderAllowance(
    Guid CustomerId,
    string Currency,
    decimal Ceiling,
    decimal Reserved,
    long Version
);
