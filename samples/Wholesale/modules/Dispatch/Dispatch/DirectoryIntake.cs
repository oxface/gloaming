using System.Text.Json;
using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Dispatch.Contracts;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;

namespace ModulithFoundry.Samples.Wholesale.Dispatch;

internal sealed class DirectoryIntake(IServiceScopeFactory scopes) : ICustomerDirectoryIntake
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public async Task ReceiveAsync(
        Guid messageId,
        string organization,
        CustomerDeliveryState state,
        CancellationToken token
    )
    {
        DirectoryMerge.Validate(organization, state);

        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<DispatchDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        // Failure before admission keeps the producer's durable outbox pending.
        _ = await DirectoryMerge.LockAsync(database, organization, token);
        await scope
            .ServiceProvider.GetRequiredService<IInbox<DispatchDbContext>>()
            .ReceiveAsync(
                DispatchRegistration.DirectorySubscription,
                new IncomingMessage(
                    messageId,
                    "sales",
                    "sales.customer-delivery-state",
                    1,
                    JsonSerializer.SerializeToElement(state, Json),
                    organization
                ),
                token
            );
        await transaction.CommitAsync(token);
    }
}

internal sealed class DirectoryHandler(DispatchDbContext database)
    : IInboxHandler<DispatchDbContext>
{
    public async Task HandleAsync(IncomingMessage message, CancellationToken cancellationToken)
    {
        if (
            message.ProducerKey != "sales"
            || message.MessageName != "sales.customer-delivery-state"
            || message.SchemaVersion != 1
            || message.TenantKey is null
        )
            throw new InvalidDataException("Invalid Sales directory envelope.");

        var state =
            message.Payload.Deserialize<CustomerDeliveryState>(DirectoryIntake.Json)
            ?? throw new InvalidDataException("Missing Sales directory state.");
        _ = await DirectoryMerge.LockAsync(database, message.TenantKey, cancellationToken);
        await DirectoryMerge.ApplyAsync(database, message.TenantKey, state, cancellationToken);
        // Gloaming saves this state and its inbox completion in the same transaction.
    }
}
