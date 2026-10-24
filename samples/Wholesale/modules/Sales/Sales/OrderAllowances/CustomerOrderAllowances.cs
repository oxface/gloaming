using Gloaming.ActorIdentity;
using Gloaming.Samples.Wholesale.Sales.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gloaming.Samples.Wholesale.Sales.OrderAllowances;

internal sealed class CustomerOrderAllowances(SalesDbContext database, IActorContextAccessor actor)
    : ICustomerOrderAllowances
{
    public async Task OpenAsync(
        Guid customerId,
        string currency,
        decimal ceiling,
        CancellationToken cancellationToken
    )
    {
        string owner = database.RequiredOrganizationKey;
        if (
            actor.Current.Actor.Kind != ActorKind.Human
            || database.Database.CurrentTransaction is null
        )
            throw new InvalidOperationException(
                "Opening an allowance requires an admitted human and active Sales transaction."
            );

        var candidate = new OrderAllowanceAggregate(
            customerId,
            Money.Create(ceiling, currency),
            Money.Create(0, currency),
            1
        );
        if (!await database.Customers.AnyAsync(row => row.Id == customerId, cancellationToken))
            throw new InvalidOperationException(
                "The customer does not exist in this Organization."
            );

        database.OrderAllowances.Add(
            new OrderAllowanceRow
            {
                OrganizationKey = owner,
                CustomerId = customerId,
                Currency = currency,
                Ceiling = candidate.Ceiling.Amount,
                Reserved = 0,
                Version = 1,
            }
        );
    }

    public async Task<CustomerOrderAllowance?> ReadAsync(
        Guid customerId,
        string currency,
        CancellationToken cancellationToken
    )
    {
        _ = database.RequiredOrganizationKey;
        return await database
            .OrderAllowances.AsNoTracking()
            .Where(row => row.CustomerId == customerId && row.Currency == currency)
            .Select(row => new CustomerOrderAllowance(
                row.CustomerId,
                row.Currency,
                row.Ceiling,
                row.Reserved,
                row.Version
            ))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
