using System.Text.Json;
using Gloaming.Events.Serialization;

namespace ModulithFoundry.Samples.Wholesale.Purchasing.PurchaseOrders;

internal static class PurchaseOrderCodec
{
    internal static JsonEventCodec<IPurchaseOrderEvent> CreateCodec() =>
        new(
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                RespectNullableAnnotations = true,
                RespectRequiredConstructorParameters = true,
            },
            [
                EventRegistration<IPurchaseOrderEvent>.For<PurchaseOrderDrafted>(
                    "purchasing.purchase-order.drafted",
                    1
                ),
                EventRegistration<IPurchaseOrderEvent>.For<PurchaseOrderLineSet>(
                    "purchasing.purchase-order.line-set",
                    1
                ),
            ]
        );
}
