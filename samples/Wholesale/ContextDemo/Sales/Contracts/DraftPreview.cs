using Gloaming.ActorIdentity;

namespace ModulithFoundry.Samples.Wholesale.ContextDemo.Sales.Contracts;

public sealed record DraftPreview(
    string Sku,
    int RequestedQuantity,
    int AvailableQuantity,
    bool CanFulfil,
    Actor RequestedBy,
    Actor? Initiator
);
