using Gloaming.EventReactions;
using Microsoft.EntityFrameworkCore;

namespace ModulithFoundry.Samples.EventReactions;

internal sealed class ReserveQuoteAllowance(
    MappedQuotesContext database,
    IEventSink<MappedQuotesModule> events
) : IEventReaction<QuoteAccepted>, ISynchronousEventReaction<QuoteAccepted>
{
    public void Handle(QuoteAccepted fact) => Reserve(fact, database.Allowances.Single());

    public async Task HandleAsync(QuoteAccepted fact, CancellationToken cancellationToken) =>
        Reserve(fact, await database.Allowances.SingleAsync(cancellationToken));

    private void Reserve(QuoteAccepted fact, QuoteAllowanceRow row)
    {
        var decision = row.ToDomain().Reserve(fact.Id, fact.Amount);
        row.Apply(decision.State);
        events.Raise(decision.Fact);
    }
}

internal sealed class RecordQuoteReceipt(MappedQuotesContext database)
    : IEventReaction<QuoteAccepted>,
        ISynchronousEventReaction<QuoteAccepted>
{
    public void Handle(QuoteAccepted fact) =>
        database.Receipts.Add(new() { Owner = database.Owner, OrderId = fact.Id });

    public Task HandleAsync(QuoteAccepted fact, CancellationToken cancellationToken)
    {
        Handle(fact);
        return Task.CompletedTask;
    }
}

internal sealed class RecordQuoteAllocation(MappedQuotesContext database)
    : IEventReaction<QuoteAllowanceAllocated>,
        ISynchronousEventReaction<QuoteAllowanceAllocated>
{
    public void Handle(QuoteAllowanceAllocated fact) =>
        database.Allocations.Add(
            new()
            {
                Owner = database.Owner,
                OrderId = fact.Id,
                Amount = fact.Amount,
            }
        );

    public Task HandleAsync(QuoteAllowanceAllocated fact, CancellationToken cancellationToken)
    {
        Handle(fact);
        return Task.CompletedTask;
    }
}

internal sealed class ReleaseQuoteAllowance(MappedQuotesContext database)
    : IEventReaction<QuoteDeleted>,
        ISynchronousEventReaction<QuoteDeleted>
{
    public void Handle(QuoteDeleted fact)
    {
        var allocation = database.Allocations.Single(row => row.OrderId == fact.Id);
        CheckAmount(fact, allocation);
        var row = database.Allowances.Single();
        row.Apply(row.ToDomain().Release(fact.Amount));
        database.Allocations.Remove(allocation);
        database.Receipts.Remove(database.Receipts.Single(row => row.OrderId == fact.Id));
    }

    public async Task HandleAsync(QuoteDeleted fact, CancellationToken cancellationToken)
    {
        var allocation = await database.Allocations.SingleAsync(
            row => row.OrderId == fact.Id,
            cancellationToken
        );
        CheckAmount(fact, allocation);
        var row = await database.Allowances.SingleAsync(cancellationToken);
        row.Apply(row.ToDomain().Release(fact.Amount));
        database.Allocations.Remove(allocation);
        database.Receipts.Remove(
            await database.Receipts.SingleAsync(row => row.OrderId == fact.Id, cancellationToken)
        );
    }

    private static void CheckAmount(QuoteDeleted fact, QuoteAllocation allocation)
    {
        if (allocation.Amount != fact.Amount)
            throw new InvalidOperationException("Deletion differs from the accepted allocation.");
    }
}
