using Gloaming.ActorIdentity;
using Gloaming.Samples.Wholesale.Sales.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gloaming.Samples.Wholesale.Sales;

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

        var details = CustomerProfileDetails.Create(change.DisplayName, change.AddressLine);

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

            if (customer.Version != change.ExpectedVersion)
            {
                await transaction.RollbackAsync(CancellationToken.None);
                return new ProfileChangeResult.Conflict();
            }

            var observed = CustomerProfileAggregate.FromState(
                new CustomerProfile(
                    customer.Id,
                    address.Id,
                    customer.Code,
                    customer.DisplayName,
                    address.AddressLine,
                    customer.Version
                )
            );
            var candidate = observed.Change(details);

            // A concurrent commit after loading is still checked by the native UPDATE predicate.
            database.Entry(customer).Property(row => row.Version).OriginalValue =
                change.ExpectedVersion;
            customer.Version = candidate.Version;
            customer.DisplayName = candidate.Details.DisplayName;
            // Every accepted edit advances the version, including an unchanged text submission.
            database.Entry(customer).Property(row => row.Version).IsModified = true;
            await database.SaveChangesAsync(cancellationToken);
            address.AddressLine = candidate.Details.AddressLine;
            audit.ProfileChanged(customer.Id, address.Id, change.ExpectedVersion, customer.Version);
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new ProfileChangeResult.Updated(candidate.ToProfile());
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
