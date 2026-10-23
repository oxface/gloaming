using Gloaming.ActorIdentity;
using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.Workflows;
using Gloaming.Samples.Wholesale.Sales.Contracts;
using Gloaming.Tenancy;

namespace Gloaming.Samples.Wholesale.Sales.ReservationOrders;

internal sealed class StockReservationReplyHandler(
    IWorkflow<StartStockReservationOrder, ReservationState> workflow,
    ITenantContextInitializer tenancy,
    IActorContextInitializer actor,
    ITenantContextAccessor tenantContext,
    IActorContextAccessor actorContext
) : IInboxHandler<SalesDbContext>
{
    public async Task HandleAsync(IncomingMessage message, CancellationToken cancellationToken)
    {
        StockReservationReplyAdmission.Validate(message);
        tenancy.Initialize(TenantContext.ForTenant(new TenantId(message.TenantKey!)));
        actor.Initialize(
            new ActorContext(Actor.System(new ActorId(StockReservationReplyAdmission.WorkerActor)))
        );

        await HandleAdmittedAsync(message, cancellationToken);
    }

    internal async Task HandleAdmittedAsync(
        IncomingMessage message,
        CancellationToken cancellationToken
    )
    {
        StockReservationReplyAdmission.Validate(message);
        if (
            tenantContext.Current.RequireTenant().Value != message.TenantKey
            || actorContext.Current.Actor
                != Actor.System(new ActorId(StockReservationReplyAdmission.WorkerActor))
        )
            throw new InvalidDataException(
                "The reservation reply does not match the admitted worker operation."
            );

        await workflow.HandleReplyAsync(
            new(Guid.Parse(message.CorrelationId!), message.TenantKey),
            message,
            cancellationToken
        );
        // Existing inbox processing saves process, next request and completion in one transaction.
    }
}

// The host established this immutable operation before the processor began its RLS transaction.
internal sealed class AdmittedStockReservationReplyHandler(StockReservationReplyHandler handler)
    : IInboxHandler<SalesDbContext>
{
    public Task HandleAsync(IncomingMessage message, CancellationToken cancellationToken) =>
        handler.HandleAdmittedAsync(message, cancellationToken);
}
