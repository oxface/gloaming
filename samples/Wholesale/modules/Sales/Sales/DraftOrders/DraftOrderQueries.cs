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
        return Summaries(ReadView().Where(row => row.Id == id))
            .SingleOrDefaultAsync(cancellationToken);
    }

    public async Task<DraftOrderPage> SearchAsync(
        SearchDraftOrders request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfNegative(request.Offset);
        ArgumentOutOfRangeException.ThrowIfLessThan(request.Limit, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(request.Limit, 100);

        _ = database.RequiredOrganizationKey;
        var filtered = ReadView();
        if (request.CustomerNameContains is not null)
            filtered = filtered.WithCustomerNameContaining(request.CustomerNameContains);

        if (request.Currency is not null)
            filtered = filtered.InCurrency(request.Currency);

        if (request.MinimumTotal is { } minimum)
            filtered = filtered.WithTotalAtLeast(minimum);

        int count = await filtered.CountAsync(cancellationToken);
        var items = await Summaries(
                filtered
                    .OrderBy(row => row.Total)
                    .ThenBy(row => row.Id)
                    .Skip(request.Offset)
                    .Take(request.Limit)
            )
            .ToArrayAsync(cancellationToken);
        return new DraftOrderPage(items, count);
    }

    private IQueryable<DraftOrderReadView> ReadView() =>
        (
            from draft in database.DraftOrders.AsNoTracking()
            join customer in database.Customers.AsNoTracking()
                on new { draft.OrganizationKey, Id = draft.CustomerId } equals new
                {
                    customer.OrganizationKey,
                    customer.Id,
                }
            select new DraftOrderReadView
            {
                Id = draft.Id,
                Code = draft.Code,
                Version = draft.Version,
                CustomerId = customer.Id,
                CustomerName = customer.DisplayName,
                Currency = draft.Currency,
                LineCount = draft.Lines.Count,
                Total = draft.Lines.Sum(line => line.Quantity * line.UnitPrice),
            }
        );

    private static IQueryable<DraftOrderSummary> Summaries(IQueryable<DraftOrderReadView> rows) =>
        rows.Select(row => new DraftOrderSummary(
            row.Id,
            row.Code,
            row.Version,
            row.CustomerId,
            row.CustomerName,
            row.Currency,
            row.LineCount,
            row.Total
        ));
}
