using System.Text.Json;
using Gloaming.EventSourcing.EntityFrameworkCore;

namespace ModulithFoundry.Samples.Wholesale.Purchasing.PurchaseOrders;

internal sealed class PurchaseOrderStateRow : IInlineStateRecord
{
    private static readonly JsonSerializerOptions StateJson = new(JsonSerializerDefaults.Web)
    {
        RespectNullableAnnotations = true,
        RespectRequiredConstructorParameters = true,
    };

    public string OrganizationKey { get; set; } = null!;
    public Guid StreamId { get; set; }
    public long Version { get; set; }
    public DateTimeOffset RecordedAt { get; set; }
    public JsonElement State { get; set; }

    internal PurchaseOrderState ReadState()
    {
        var state =
            State.Deserialize<PurchaseOrderState>(StateJson)
            ?? throw new InvalidDataException("The purchase-order write view is unreadable.");
        if (
            string.IsNullOrWhiteSpace(state.Code)
            || string.IsNullOrWhiteSpace(state.SupplierReference)
            || string.IsNullOrWhiteSpace(state.Currency)
            || state.Lines.Any(line =>
                string.IsNullOrWhiteSpace(line.ItemCode) || line.Quantity <= 0 || line.UnitPrice < 0
            )
            || state.Lines.Select(line => line.ItemCode).Distinct(StringComparer.Ordinal).Count()
                != state.Lines.Count
        )
            throw new InvalidDataException("The purchase-order write view has invalid state.");
        return state;
    }

    internal static PurchaseOrderStateRow FromState(PurchaseOrderState state) =>
        new() { State = JsonSerializer.SerializeToElement(state, StateJson) };

    internal static PurchaseOrderStateRow FromState(
        string owner,
        Guid id,
        long version,
        DateTimeOffset recordedAt,
        PurchaseOrderState state
    ) =>
        new()
        {
            OrganizationKey = owner,
            StreamId = id,
            Version = version,
            RecordedAt = recordedAt,
            State = JsonSerializer.SerializeToElement(state, StateJson),
        };
}
