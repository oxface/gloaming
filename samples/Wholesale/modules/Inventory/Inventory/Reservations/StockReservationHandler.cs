using System.Diagnostics;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using ModulithFoundry.Samples.Wholesale.Inventory.Contracts;
using ModulithFoundry.Samples.Wholesale.Inventory.StockPositions;
using Rootbolt.ActorIdentity;
using Rootbolt.EventSourcing.EntityFrameworkCore;
using Rootbolt.Messaging;
using Rootbolt.Messaging.EntityFrameworkCore;
using Rootbolt.Tenancy;

namespace ModulithFoundry.Samples.Wholesale.Inventory.Reservations;

internal sealed class StockReservationHandler(
    InventoryDbContext database,
    IEventStore<StockPositionAggregate> store,
    IOutbox<InventoryDbContext> outbox,
    ITenantContextInitializer tenancy,
    IActorContextInitializer actor
) : IInboxHandler<InventoryDbContext>
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task HandleAsync(IncomingMessage message, CancellationToken cancellationToken)
    {
        var command = StockReservationAdmission.Decode(message);
        tenancy.Initialize(TenantContext.ForTenant(new TenantId(message.TenantKey!)));
        actor.Initialize(
            new ActorContext(Actor.System(new ActorId("inventory.reservation-worker")))
        );

        switch (command)
        {
            case ReserveStockV1 reserve:
                var outcome = await ReserveAsync(reserve, cancellationToken);
                Reply(message, "inventory.reservation-decided", outcome);
                break;
            case ReleaseStockReservationV1 release:
                var decision = await ReleaseAsync(release.ReservationId, cancellationToken);
                Reply(
                    message,
                    "inventory.reservation-released",
                    new StockReservationReleasedV1(release.ReservationId, decision)
                );
                break;
        }
    }

    private async Task<StockReservationDecidedV1> ReserveAsync(
        ReserveStockV1 command,
        CancellationToken token
    )
    {
        var existing = await database
            .Set<StockReservation>()
            .SingleOrDefaultAsync(row => row.Id == command.ReservationId, token);
        if (existing is not null)
        {
            existing.ValidateRetry(command);
            return existing.Outcome();
        }

        StockReservationDecision decision;
        try
        {
            var stock = await store.GetForWritingAsync(
                command.StockPositionId,
                command.ExpectedVersion,
                token
            );
            if (stock is null)
                decision = StockReservationDecision.NotFound;
            else if (!stock.TryReserve(command.ReservationId, command.Quantity))
                decision = StockReservationDecision.InsufficientStock;
            else
            {
                await store.AppendAsync(stock, token);
                decision = StockReservationDecision.Reserved;
            }
        }
        catch (DbUpdateConcurrencyException)
        {
            // Only a stale loaded version is a business refusal. A save race escapes to the
            // inbox processor, which rolls back and retries the entire attempt in a fresh scope.
            decision = StockReservationDecision.Conflict;
        }

        var reservation = StockReservation.Create(
            database.RequiredOrganizationKey,
            command,
            decision
        );
        database.Add(reservation);
        return reservation.Outcome();
    }

    private async Task<StockReservationReleaseDecision> ReleaseAsync(
        Guid id,
        CancellationToken token
    )
    {
        var reservation = await database
            .Set<StockReservation>()
            .SingleOrDefaultAsync(row => row.Id == id, token);
        if (reservation is null)
            return StockReservationReleaseDecision.ReservationNotFound;

        if (reservation.Released || reservation.Decision != StockReservationDecision.Reserved)
            return StockReservationReleaseDecision.Released;

        // Compensation uses the current version; intervening receipts/issues are preserved.
        var stock = await store.GetForWritingAsync(
            reservation.StockPositionId,
            cancellationToken: token
        );
        if (stock is null)
            return StockReservationReleaseDecision.StockPositionNotFound;

        stock.Release(reservation.Id, reservation.Quantity);
        await store.AppendAsync(stock, token);
        reservation.Release();
        return StockReservationReleaseDecision.Released;
    }

    private void Reply<TPayload>(IncomingMessage cause, string name, TPayload payload)
        where TPayload : notnull =>
        outbox.Enqueue(
            OutgoingMessage.FromPayload(
                Guid.NewGuid(),
                "inventory.reservation-outcomes",
                name,
                1,
                payload,
                Json,
                cause.TenantKey,
                cause.CorrelationId,
                cause.MessageId.ToString("D"),
                Activity.Current?.Id,
                Activity.Current?.TraceStateString
            )
        );
}
