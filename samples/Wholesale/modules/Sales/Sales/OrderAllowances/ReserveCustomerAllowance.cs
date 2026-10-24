using Gloaming.EventReactions;
using Gloaming.Samples.Wholesale.Sales.DraftOrders;
using Microsoft.EntityFrameworkCore;

namespace Gloaming.Samples.Wholesale.Sales.OrderAllowances;

internal sealed class ReserveCustomerAllowance(SalesDbContext database)
    : IEventReaction<DraftSubmitted>,
        ISynchronousEventReaction<DraftSubmitted>
{
    public void Handle(DraftSubmitted fact) =>
        Reserve(
            fact,
            database.OrderAllowances.Single(row =>
                row.CustomerId == fact.CustomerId && row.Currency == fact.Total.Currency
            )
        );

    public async Task HandleAsync(DraftSubmitted fact, CancellationToken cancellationToken) =>
        Reserve(
            fact,
            await database.OrderAllowances.SingleAsync(
                row => row.CustomerId == fact.CustomerId && row.Currency == fact.Total.Currency,
                cancellationToken
            )
        );

    private static void Reserve(DraftSubmitted fact, OrderAllowanceRow row)
    {
        var candidate = row.ToAggregate().Reserve(fact.Total);
        row.Reserved = candidate.Reserved.Amount;
        row.Version = candidate.Version;
    }
}
