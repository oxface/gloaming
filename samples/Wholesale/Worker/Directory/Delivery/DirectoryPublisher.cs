using System.Text.Json;
using Gloaming.Messaging;
using ModulithFoundry.Samples.Wholesale.Dispatch.Contracts;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;

namespace ModulithFoundry.Samples.Wholesale.Worker.Directory.Delivery;

// The relay knows only Dispatch's public boundary and Sales's public replacement contract.
public sealed class DirectoryPublisher(ICustomerDirectoryIntake intake) : IMessagePublisher
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public Task PublishAsync(OutgoingMessage message, CancellationToken cancellationToken)
    {
        if (
            message.RouteKey != "dispatch.customer-directory"
            || message.MessageName != "sales.customer-delivery-state"
            || message.SchemaVersion != 1
            || message.TenantKey is null
        )
            throw new InvalidDataException("Unsupported directory route or envelope.");

        var state =
            message.Payload.Deserialize<CustomerDeliveryState>(Json)
            ?? throw new InvalidDataException("Missing directory state.");
        return intake.ReceiveAsync(message.MessageId, message.TenantKey, state, cancellationToken);
    }
}
