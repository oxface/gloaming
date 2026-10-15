using Microsoft.EntityFrameworkCore;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;

namespace ModulithFoundry.Samples.Wholesale.Sales.DraftOrders;

internal sealed class DraftOrderQueries(SalesDbContext database) : IDraftOrderQueries
{
    public async Task<DraftOrder?> ReadAsync(Guid id, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);

        _ = database.RequiredOrganizationKey;
        var row = await database
            .DraftOrders.AsNoTracking()
            .Include(value => value.Lines)
            .AsSingleQuery()
            .SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        return row?.ToAggregate().ToView();
    }

    public Task<DraftOrderSummary?> ReadSummaryAsync(Guid id, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);

        _ = database.RequiredOrganizationKey;
        return (
            from draft in database.DraftOrders.AsNoTracking()
            join customer in database.Customers.AsNoTracking()
                on new { draft.OrganizationKey, Id = draft.CustomerId } equals new
                {
                    customer.OrganizationKey,
                    customer.Id,
                }
            where draft.Id == id
            select new DraftOrderSummary(
                draft.Id,
                draft.Code,
                draft.Version,
                customer.Id,
                customer.DisplayName,
                draft.Currency,
                draft.Lines.Count,
                draft.Lines.Sum(line => line.Quantity * line.UnitPrice)
            )
        ).SingleOrDefaultAsync(cancellationToken);
    }
}
