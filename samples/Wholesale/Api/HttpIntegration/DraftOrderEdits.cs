using Gloaming.Samples.Wholesale.Sales;
using Gloaming.Samples.Wholesale.Sales.Contracts;
using Microsoft.EntityFrameworkCore;

namespace Gloaming.Samples.Wholesale.Api.HttpIntegration;

// The HTTP consumer owns save/commit; endpoint code still calls the business Contracts.
internal sealed class DraftOrderEdits(SalesDbContext database, IDraftOrderCommands commands)
{
    internal Task<DraftOrderChangeResult> CreateAsync(
        CreateDraftOrder request,
        CancellationToken cancellationToken
    ) => SaveAsync(() => commands.CreateAsync(request, cancellationToken), cancellationToken);

    internal Task<DraftOrderChangeResult> SubmitAsync(
        SubmitDraftOrder request,
        CancellationToken cancellationToken
    ) => SaveAsync(() => commands.SubmitAsync(request, cancellationToken), cancellationToken);

    internal Task<DraftOrderChangeResult> ChangeAsync(
        ChangeDraftOrderLines request,
        CancellationToken cancellationToken
    ) => SaveAsync(() => commands.ChangeLinesAsync(request, cancellationToken), cancellationToken);

    private async Task<DraftOrderChangeResult> SaveAsync(
        Func<Task<DraftOrderChangeResult>> operation,
        CancellationToken cancellationToken
    )
    {
        await using var transaction = await database.Database.BeginTransactionAsync(
            cancellationToken
        );
        try
        {
            var result = await operation();
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
        catch (CommandReceiptConflictException)
        {
            return new DraftOrderChangeResult.Conflict();
        }
        catch (OrderAllowanceExceededException failure)
        {
            return new DraftOrderChangeResult.LimitExceeded(failure.Ceiling, failure.Requested);
        }
    }
}
