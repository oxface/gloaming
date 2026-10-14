using Gloaming.ActorIdentity;
using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Tenancy;
using ModulithFoundry.Samples.Wholesale.Inventory.Contracts;

namespace ModulithFoundry.Samples.Wholesale.Inventory.Messaging;

internal sealed class StockIssueInboxHandler(
    ITenantContextInitializer tenancy,
    IActorContextInitializer actorInitializer,
    InventoryAudit audit,
    InventoryMessageContext metadata,
    IStockPositionCommands commands,
    IOutbox<InventoryDbContext> outbox,
    StockIssueMessages messages
) : IInboxHandler<InventoryDbContext>
{
    public async Task HandleAsync(IncomingMessage message, CancellationToken cancellationToken)
    {
        var command = StockIssueMessageAdmission.Validate(message);
        tenancy.Initialize(TenantContext.ForTenant(new TenantId(message.TenantKey!)));
        // Receiver-owned identity; wire correlation and causation do not establish a human initiator.
        actorInitializer.Initialize(
            new ActorContext(Actor.System(new ActorId("inventory.stock-issue-worker")))
        );
        metadata.Initialize(message);

        var result = await commands.IssueAsync(
            new(
                command.StockPositionId,
                command.ExpectedVersion,
                [new StockIssue(command.Quantity)]
            ),
            cancellationToken
        );
        if (result is StockPositionChangeResult.Changed accepted)
        {
            audit.StockIssued(command.StockPositionId, accepted.Proposed.Version, command.Quantity);
            return;
        }

        var reason = result switch
        {
            StockPositionChangeResult.NotFound => StockIssueDeclineReason.NotFound,
            StockPositionChangeResult.Conflict => StockIssueDeclineReason.Conflict,
            StockPositionChangeResult.InsufficientStock =>
                StockIssueDeclineReason.InsufficientStock,
            _ => throw new InvalidOperationException("Unknown stock issue decision."),
        };
        var shortage = result as StockPositionChangeResult.InsufficientStock;
        outbox.Enqueue(
            messages.StockIssueDeclined(command, reason, shortage?.Available, shortage?.Requested)
        );
    }
}
