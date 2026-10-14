using System.Text.Json;
using Gloaming.Events.Serialization;

namespace ModulithFoundry.Samples.Wholesale.EventCodecDemo.Purchasing;

internal static class PurchaseOrderExample
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

    internal static PurchaseOrderSummary Read(IEnumerable<IPurchaseOrderEvent> events)
    {
        PurchaseOrderDrafted? draft = null;
        var lines = new Dictionary<string, PurchaseOrderLineSet>(StringComparer.Ordinal);
        foreach (IPurchaseOrderEvent @event in events)
        {
            switch (@event)
            {
                case PurchaseOrderDrafted opened when draft is null:
                    draft = opened;
                    break;
                case PurchaseOrderLineSet line when draft is not null:
                    lines[line.ItemCode] = line;
                    break;
                default:
                    throw new InvalidOperationException(
                        "Unsupported purchase-order example sequence."
                    );
            }
        }
        if (draft is null)
            throw new InvalidOperationException("The example has no drafted purchase order.");
        return new PurchaseOrderSummary(
            draft.Code,
            draft.SupplierReference,
            draft.Currency,
            lines.Values.ToArray()
        );
    }
}

internal sealed record PurchaseOrderSummary(
    string Code,
    string SupplierReference,
    string Currency,
    IReadOnlyList<PurchaseOrderLineSet> Lines
)
{
    internal decimal Total => Lines.Sum(line => line.Quantity * line.UnitPrice);
}
