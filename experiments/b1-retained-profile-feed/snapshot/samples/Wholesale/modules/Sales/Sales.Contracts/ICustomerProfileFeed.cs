namespace ModulithFoundry.Samples.Wholesale.Sales.Contracts;

/// <summary>Retained Sales profile edits for the already admitted Organization.</summary>
public interface ICustomerProfileFeed
{
    /// <summary>Describes retained history; does not certify a complete customer catalog.</summary>
    Task<CustomerProfileFeedBounds> DescribeAsync(CancellationToken cancellationToken);

    /// <summary>Reads 1–100 changes from one committed snapshot, strictly after the cursor.</summary>
    Task<CustomerProfileFeedRead> ReadAsync(
        CustomerProfileFeedCursor after,
        int pageSize,
        CancellationToken cancellationToken
    );
}
