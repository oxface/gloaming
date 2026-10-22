using Microsoft.EntityFrameworkCore;

namespace ModulithFoundry.Samples.Wholesale.PersistenceDemo.Sales;

public static class CustomerProfileChanges
{
    public static async Task<long> ApplyAsync(
        SalesDbContext context,
        CustomerProfileChange change,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(change);
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException(
                "Begin a caller-owned Sales transaction before applying a profile change."
            );
        ArgumentOutOfRangeException.ThrowIfLessThan(change.ExpectedVersion, 1L);
        long nextVersion = checked(change.ExpectedVersion + 1);

        CustomerReference customer = await context.Customers.SingleAsync(
            row => row.Id == change.CustomerId,
            cancellationToken
        );
        CustomerAddressReference address = await context.CustomerAddresses.SingleAsync(
            row => row.Id == change.AddressId && row.CustomerId == customer.Id,
            cancellationToken
        );

        context.Entry(customer).Property(row => row.Version).OriginalValue = change.ExpectedVersion;
        customer.DisplayName = change.DisplayName;
        customer.Version = nextVersion;
        await context.SaveChangesAsync(cancellationToken);

        address.AddressLine = change.AddressLine;
        await context.SaveChangesAsync(cancellationToken);
        return nextVersion;
    }
}
