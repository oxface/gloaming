using System.Text;
using System.Text.Json;
using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Inventory;
using RabbitMQ.Client;

namespace ModulithFoundry.Samples.Wholesale.EventPersistenceDemo;

/// <summary>Consumer-owned native admission, retained intake and acknowledgement after database commit.</summary>
public sealed class InventoryRabbitMqReceiver(IChannel channel, IServiceScopeFactory scopes)
{
    public static IncomingMessage Parse(BasicGetResult delivery)
    {
        var properties = delivery.BasicProperties;
        if (
            !Guid.TryParse(properties.MessageId, out Guid id)
            || Header(properties, "producer-module") != StockIssueMessageAdmission.Producer
            || properties.Headers is null
            || !properties.Headers.TryGetValue("schema-version", out var schema)
            || schema is not int version
        )
            throw new InvalidDataException("Stock command transport metadata is invalid.");

        using var document = JsonDocument.Parse(delivery.Body);
        var message = new IncomingMessage(
            id,
            StockIssueMessageAdmission.Producer,
            properties.Type!,
            version,
            document.RootElement,
            Header(properties, "owner-key"),
            properties.CorrelationId,
            Header(properties, "causation-id"),
            DiagnosticHeader(properties, "traceparent"),
            DiagnosticHeader(properties, "tracestate")
        );
        StockIssueMessageAdmission.Validate(message);
        return message;
    }

    public async Task<InboxReceiveResult> ReceiveAsync(
        BasicGetResult delivery,
        CancellationToken cancellationToken
    )
    {
        var message = Parse(delivery);
        await using var scope = scopes.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(
            cancellationToken
        );
        var result = await scope
            .ServiceProvider.GetRequiredService<IInbox<InventoryDbContext>>()
            .ReceiveAsync(StockIssueMessageAdmission.Subscription, message, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        // Delivery tags/channels remain native and visible; a failure here can redeliver
        // the committed intake, which will compare as AlreadyReceived.
        await channel.BasicAckAsync(
            delivery.DeliveryTag,
            multiple: false,
            cancellationToken: cancellationToken
        );
        return result;
    }

    // Diagnostic metadata must not reject an otherwise valid business delivery.
    private static string? DiagnosticHeader(IReadOnlyBasicProperties properties, string key)
    {
        if (properties.Headers is null || !properties.Headers.TryGetValue(key, out var value))
            return null;

        return value switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            string text => text,
            _ => null,
        };
    }

    private static string? Header(IReadOnlyBasicProperties properties, string key)
    {
        if (properties.Headers is null || !properties.Headers.TryGetValue(key, out var value))
            return null;

        return value switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            string text => text,
            _ => throw new InvalidDataException("A textual stock command header was expected."),
        };
    }
}
