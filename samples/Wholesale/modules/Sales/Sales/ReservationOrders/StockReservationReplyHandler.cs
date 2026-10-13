using ModulithFoundry.Samples.Wholesale.Sales.Contracts;
using Rootbolt.ActorIdentity;
using Rootbolt.Messaging;
using Rootbolt.Messaging.EntityFrameworkCore;
using Rootbolt.Messaging.Workflows;
using Rootbolt.Tenancy;

namespace ModulithFoundry.Samples.Wholesale.Sales.ReservationOrders;

internal sealed class StockReservationReplyHandler(
    IWorkflow<StartStockReservationOrder, ReservationState> workflow,
    ITenantContextInitializer tenancy,
    IActorContextInitializer actor
) : IInboxHandler<SalesDbContext>
{
    public async Task HandleAsync(IncomingMessage message, CancellationToken cancellationToken)
    {
        StockReservationReplyAdmission.Validate(message);
        tenancy.Initialize(TenantContext.ForTenant(new TenantId(message.TenantKey!)));
        actor.Initialize(new ActorContext(Actor.System(new ActorId("sales.reservation-worker"))));

        await workflow.HandleReplyAsync(
            new(Guid.Parse(message.CorrelationId!), message.TenantKey),
            message,
            cancellationToken
        );
        // Existing inbox processing saves process, next request and completion in one transaction.
    }
}
