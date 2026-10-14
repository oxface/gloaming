using Gloaming.ActorIdentity;
using Microsoft.EntityFrameworkCore;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;

namespace ModulithFoundry.Samples.Wholesale.Sales;

internal sealed class CustomerProfiles(
    SalesDbContext database,
    IActorContextAccessor actor,
    SalesAudit audit
) : ICustomerProfiles
{
    public async Task<CustomerProfile?> ReadAsync(
        Guid customerId,
        CancellationToken cancellationToken
    )
    {
        ArgumentOutOfRangeException.ThrowIfEqual(customerId, Guid.Empty);
        _ = database.RequiredOrganizationKey;
        return await (
            from customer in database.Customers.AsNoTracking()
            join address in database.Addresses.AsNoTracking()
                on customer.Id equals address.CustomerId
            where customer.Id == customerId
            select new CustomerProfile(
                customer.Id,
                address.Id,
                customer.Code,
                customer.DisplayName,
                address.AddressLine,
                customer.Version
            )
        ).SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<ProfileChangeResult> ChangeAsync(
        CustomerProfileChange change,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(change);
        ArgumentOutOfRangeException.ThrowIfEqual(change.CustomerId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfEqual(change.AddressId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfLessThan(change.ExpectedVersion, 1);
        ArgumentOutOfRangeException.ThrowIfEqual(change.ExpectedVersion, long.MaxValue);
        ArgumentException.ThrowIfNullOrWhiteSpace(change.DisplayName);
        ArgumentException.ThrowIfNullOrWhiteSpace(change.AddressLine);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(change.DisplayName.Length, 256);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(change.AddressLine.Length, 512);
        _ = database.RequiredOrganizationKey;
        var attribution = actor.Current;
        if (attribution.Actor.Kind != ActorKind.Human)
            throw new InvalidOperationException(
                "Sales profile changes require an established human actor."
            );

        await using var transaction = await database.Database.BeginTransactionAsync(
            cancellationToken
        );
        try
        {
            var customer = await database.Customers.SingleOrDefaultAsync(
                row => row.Id == change.CustomerId,
                cancellationToken
            );
            var address = await database.Addresses.SingleOrDefaultAsync(
                row => row.Id == change.AddressId && row.CustomerId == change.CustomerId,
                cancellationToken
            );
            if (customer is null || address is null)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return new ProfileChangeResult.NotFound();
            }
            // Use the caller's version even when this request reloaded a newer database row.
            database.Entry(customer).Property(row => row.Version).OriginalValue =
                change.ExpectedVersion;
            customer.Version = checked(change.ExpectedVersion + 1);
            customer.DisplayName = change.DisplayName;
            // Force a versioned UPDATE even if a stale caller submitted the current text/version.
            database.Entry(customer).Property(row => row.Version).IsModified = true;
            await database.SaveChangesAsync(cancellationToken);
            address.AddressLine = change.AddressLine;
            audit.ProfileChanged(customer.Id, address.Id, change.ExpectedVersion, customer.Version);
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new ProfileChangeResult.Updated(
                new CustomerProfile(
                    customer.Id,
                    address.Id,
                    customer.Code,
                    customer.DisplayName,
                    address.AddressLine,
                    customer.Version
                )
            );
        }
        catch (DbUpdateConcurrencyException)
        {
            await transaction.RollbackAsync(CancellationToken.None);
            return new ProfileChangeResult.Conflict();
        }
        catch
        {
            await transaction.RollbackAsync(CancellationToken.None);
            throw;
        }
    }
}
