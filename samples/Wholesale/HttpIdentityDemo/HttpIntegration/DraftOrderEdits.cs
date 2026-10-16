using Microsoft.EntityFrameworkCore;
using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;

namespace ModulithFoundry.Samples.Wholesale.HttpIdentityDemo.HttpIntegration;

// The HTTP consumer owns save/commit; endpoint code still calls the business Contracts.
internal sealed class DraftOrderEdits(SalesDbContext database, IDraftOrderCommands commands)
{
    internal async Task<DraftOrderChangeResult> ChangeAsync(
        ChangeDraftOrderLines request,
        CancellationToken cancellationToken
    )
    {
        await using var transaction = await database.Database.BeginTransactionAsync(
            cancellationToken
        );
        try
        {
            var result = await commands.ChangeLinesAsync(request, cancellationToken);
            if (result is not DraftOrderChangeResult.Changed)
                return result;

            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return result;
        }
        catch (DbUpdateConcurrencyException)
        {
            return new DraftOrderChangeResult.Conflict();
        }
    }
}
