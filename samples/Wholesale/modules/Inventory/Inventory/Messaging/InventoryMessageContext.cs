using Gloaming.Messaging;

namespace Gloaming.Samples.Wholesale.Inventory.Messaging;

// Consumer-owned reply metadata. A fresh processing scope admits one incoming message;
// ordinary native commands leave this unset and do not invent an incoming cause.
internal sealed class InventoryMessageContext
{
    internal IncomingMessage? Message { get; private set; }

    internal void Initialize(IncomingMessage message)
    {
        if (Message is not null)
            throw new InvalidOperationException(
                "Use a fresh Inventory scope for each incoming command."
            );
        Message = message;
    }
}
