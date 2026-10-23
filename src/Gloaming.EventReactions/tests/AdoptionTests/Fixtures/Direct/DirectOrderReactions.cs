using Gloaming.EventReactions;
using Microsoft.EntityFrameworkCore;

namespace Gloaming.Samples.EventReactions;

internal sealed class ReserveOrderAllowance(DirectOrdersContext database)
    : IEventReaction<OrderSubmitted>,
        ISynchronousEventReaction<OrderSubmitted>
{
    public void Handle(OrderSubmitted fact) =>
        database.Allowances.Single().Reserve(fact.Id, fact.Amount);

    public async Task HandleAsync(OrderSubmitted fact, CancellationToken cancellationToken) =>
        (await database.Allowances.SingleAsync(cancellationToken)).Reserve(fact.Id, fact.Amount);
}

internal sealed class RecordOrderReceipt(DirectOrdersContext database)
    : IEventReaction<OrderSubmitted>,
        ISynchronousEventReaction<OrderSubmitted>
{
    public void Handle(OrderSubmitted fact) =>
        database.Receipts.Add(new() { Owner = database.Owner, OrderId = fact.Id });

    public Task HandleAsync(OrderSubmitted fact, CancellationToken cancellationToken)
    {
        Handle(fact);
        return Task.CompletedTask;
    }
}

internal sealed class RecordOrderAllocation(DirectOrdersContext database)
    : IEventReaction<AllowanceAllocated>,
        ISynchronousEventReaction<AllowanceAllocated>
{
    public void Handle(AllowanceAllocated fact) =>
        database.Allocations.Add(
            new()
            {
                Owner = database.Owner,
                OrderId = fact.Id,
                Amount = fact.Amount,
            }
        );

    public Task HandleAsync(AllowanceAllocated fact, CancellationToken cancellationToken)
    {
        Handle(fact);
        return Task.CompletedTask;
    }
}

internal sealed class ReleaseOrderAllowance(DirectOrdersContext database)
    : IEventReaction<OrderDeleted>,
        ISynchronousEventReaction<OrderDeleted>
{
    public void Handle(OrderDeleted fact)
    {
        var allocation = database.Allocations.Single(row => row.OrderId == fact.Id);
        CheckAmount(fact, allocation);
        database.Allowances.Single().Release(fact.Amount);
        database.Allocations.Remove(allocation);
        database.Receipts.Remove(database.Receipts.Single(row => row.OrderId == fact.Id));
    }

    public async Task HandleAsync(OrderDeleted fact, CancellationToken cancellationToken)
    {
        var allocation = await database.Allocations.SingleAsync(
            row => row.OrderId == fact.Id,
            cancellationToken
        );
        CheckAmount(fact, allocation);
        (await database.Allowances.SingleAsync(cancellationToken)).Release(fact.Amount);
        database.Allocations.Remove(allocation);
        database.Receipts.Remove(
            await database.Receipts.SingleAsync(row => row.OrderId == fact.Id, cancellationToken)
        );
    }

    private static void CheckAmount(OrderDeleted fact, DirectAllocation allocation)
    {
        if (allocation.Amount != fact.Amount)
            throw new InvalidOperationException("Deletion differs from the accepted allocation.");
    }
}
