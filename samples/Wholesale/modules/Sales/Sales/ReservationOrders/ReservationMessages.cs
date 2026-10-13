using System.Diagnostics;
using System.Text.Json;
using ModulithFoundry.Samples.Wholesale.Inventory.Contracts;
using Rootbolt.Messaging;
using Rootbolt.Messaging.Workflows;

namespace ModulithFoundry.Samples.Wholesale.Sales.ReservationOrders;

// Wire aliases, transport routes, tracing and business payloads stay with the module.
internal static class ReservationMessages
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    internal static OutgoingMessage Reserve(WorkflowContext context, ReserveStockV1 command) =>
        OutgoingMessage.FromPayload(
            context.CommandId,
            "inventory.reserve-stock",
            "inventory.reserve-stock",
            1,
            command,
            Json,
            context.Key.TenantKey,
            context.Key.Id.ToString("D"),
            context.CauseMessageId?.ToString("D"),
            Activity.Current?.Id,
            Activity.Current?.TraceStateString
        );

    internal static OutgoingMessage Release(WorkflowContext context, Guid reservationId) =>
        OutgoingMessage.FromPayload(
            context.CommandId,
            "inventory.release-reservation",
            "inventory.release-reservation",
            1,
            new ReleaseStockReservationV1(reservationId),
            Json,
            context.Key.TenantKey,
            context.Key.Id.ToString("D"),
            (context.CauseMessageId ?? context.Key.Id).ToString("D"),
            Activity.Current?.Id,
            Activity.Current?.TraceStateString
        );
}
