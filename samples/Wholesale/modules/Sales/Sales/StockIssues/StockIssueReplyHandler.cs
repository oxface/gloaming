using Gloaming.ActorIdentity;
using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Tenancy;
using Microsoft.EntityFrameworkCore;
using ModulithFoundry.Samples.Wholesale.Inventory.Contracts;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;

namespace ModulithFoundry.Samples.Wholesale.Sales.StockIssues;

internal sealed class StockIssueReplyHandler(
    SalesDbContext database,
    ITenantContextInitializer tenancy,
    IActorContextInitializer actor
) : IInboxHandler<SalesDbContext>
{
    public async Task HandleAsync(IncomingMessage message, CancellationToken cancellationToken)
    {
        var reply = StockIssueReplyAdmission.Decode(message);
        tenancy.Initialize(TenantContext.ForTenant(new TenantId(message.TenantKey!)));
        actor.Initialize(new ActorContext(Actor.System(new ActorId("sales.stock-issue-worker"))));

        Guid requestId = Guid.Parse(message.CorrelationId!);
        var progress =
            await database
                .Set<StockIssueRequestRow>()
                .SingleOrDefaultAsync(row => row.Id == requestId, cancellationToken)
            ?? throw new InvalidDataException(
                "The stock reply does not identify an admitted Sales request."
            );
        if (progress.CommandMessageId != Guid.Parse(message.CausationId!))
            throw new InvalidDataException(
                "The stock reply does not identify this request's command."
            );

        StockIssueOutcome outcome = reply switch
        {
            StockIssueRecordedV1 recorded
                when recorded.StockPositionId == progress.StockPositionId
                    && recorded.IssuedQuantity == progress.Quantity
                    && recorded.Version == checked(progress.ExpectedStockVersion + 1) => new(
                recorded.Version,
                recorded.RemainingQuantity,
                StockIssueRequestRow.AtDatabasePrecision(recorded.RecordedAt),
                null,
                null,
                null
            ),
            StockIssueDeclinedV1 declined
                when declined.StockPositionId == progress.StockPositionId
                    && declined.ExpectedVersion == progress.ExpectedStockVersion
                    && (declined.Requested is null || declined.Requested == progress.Quantity) =>
                new(
                    null,
                    null,
                    null,
                    declined.Reason switch
                    {
                        StockIssueDeclineReason.NotFound => StockIssueRefusal.NotFound,
                        StockIssueDeclineReason.Conflict => StockIssueRefusal.Conflict,
                        StockIssueDeclineReason.InsufficientStock =>
                            StockIssueRefusal.InsufficientStock,
                        _ => throw new InvalidDataException("Unknown stock refusal."),
                    },
                    declined.Available,
                    declined.Requested
                ),
            _ => throw new InvalidDataException(
                "The stock reply does not match the requested decision."
            ),
        };
        progress.Resolve(outcome);
        // The inbox processor owns SaveChanges, completion and commit. A concurrency failure
        // rolls back all three and is retried from current state in a fresh processing scope.
    }
}
