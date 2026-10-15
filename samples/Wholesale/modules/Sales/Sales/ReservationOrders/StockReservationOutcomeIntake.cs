using System.Text.Json;
using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Inventory.Contracts;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;

namespace ModulithFoundry.Samples.Wholesale.Sales.ReservationOrders;

internal sealed class StockReservationOutcomeIntake(IServiceScopeFactory scopes)
    : IStockReservationOutcomeIntake
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task ReceiveAsync(
        ReservationDelivery delivery,
        StockReservationDecidedV1 outcome,
        CancellationToken cancellationToken
    ) => Receive(delivery, outcome, "inventory.reservation-decided", cancellationToken);

    public Task ReceiveAsync(
        ReservationDelivery delivery,
        StockReservationReleasedV1 outcome,
        CancellationToken cancellationToken
    ) => Receive(delivery, outcome, "inventory.reservation-released", cancellationToken);

    private async Task Receive<T>(
        ReservationDelivery delivery,
        T outcome,
        string name,
        CancellationToken token
    )
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(outcome);

        var message = new IncomingMessage(
            delivery.MessageId,
            "inventory",
            name,
            1,
            JsonSerializer.SerializeToElement(outcome, Json),
            delivery.OrganizationKey,
            delivery.OrderId.ToString("D"),
            delivery.CauseMessageId?.ToString("D"),
            delivery.TraceParent,
            delivery.TraceState
        );
        StockReservationReplyAdmission.Validate(message);
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        await scope
            .ServiceProvider.GetRequiredService<IInbox<SalesDbContext>>()
            .ReceiveAsync(StockReservationReplyAdmission.Subscription, message, token);
        await transaction.CommitAsync(token);
    }
}
