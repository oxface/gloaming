using System.Text.Json;
using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Samples.Wholesale.Inventory.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Gloaming.Samples.Wholesale.Inventory.Reservations;

internal sealed class StockReservationIntake(IServiceScopeFactory scopes) : IStockReservationIntake
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task ReceiveAsync(
        ReservationDelivery delivery,
        ReserveStockV1 command,
        CancellationToken cancellationToken
    ) => Receive(delivery, command, "inventory.reserve-stock", cancellationToken);

    public Task ReceiveAsync(
        ReservationDelivery delivery,
        ReleaseStockReservationV1 command,
        CancellationToken cancellationToken
    ) => Receive(delivery, command, "inventory.release-reservation", cancellationToken);

    private async Task Receive<T>(
        ReservationDelivery delivery,
        T command,
        string name,
        CancellationToken token
    )
        where T : notnull
    {
        ArgumentNullException.ThrowIfNull(delivery);
        ArgumentNullException.ThrowIfNull(command);

        var message = new IncomingMessage(
            delivery.MessageId,
            "sales",
            name,
            1,
            JsonSerializer.SerializeToElement(command, Json),
            delivery.OrganizationKey,
            delivery.OrderId.ToString("D"),
            delivery.CauseMessageId?.ToString("D"),
            delivery.TraceParent,
            delivery.TraceState
        );
        StockReservationAdmission.Validate(message);
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        await scope
            .ServiceProvider.GetRequiredService<IInbox<InventoryDbContext>>()
            .ReceiveAsync(StockReservationAdmission.Subscription, message, token);
        await transaction.CommitAsync(token);
    }
}
