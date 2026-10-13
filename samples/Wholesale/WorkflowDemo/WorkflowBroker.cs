using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Inventory;
using ModulithFoundry.Samples.Wholesale.Sales;
using RabbitMQ.Client;
using Rootbolt.Events.Serialization;
using Rootbolt.Messaging;
using Rootbolt.Messaging.EntityFrameworkCore;

namespace ModulithFoundry.Samples.Wholesale.WorkflowDemo;

/// <summary>Native sample routing and acknowledgement. Owns separate channels for concurrent hosted roles.</summary>
public sealed class WorkflowBroker : IAsyncDisposable
{
    private static readonly string[] Routes =
    [
        "inventory.issue-stock",
        "inventory.stock-issues",
        "inventory.reserve-stock",
        "inventory.release-reservation",
        "inventory.reservation-outcomes",
    ];
    private readonly IConnection connection;
    private readonly IChannel commands;
    private readonly IChannel replies;
    private readonly IChannel intake;
    private readonly string prefix;

    private WorkflowBroker(
        IConnection connection,
        IChannel commands,
        IChannel replies,
        IChannel intake,
        string prefix
    ) =>
        (this.connection, this.commands, this.replies, this.intake, this.prefix) = (
            connection,
            commands,
            replies,
            intake,
            prefix
        );

    public static async Task<WorkflowBroker> OpenAsync(
        string connectionString,
        string queuePrefix,
        CancellationToken token
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queuePrefix);
        var connection = await new ConnectionFactory
        {
            Uri = new Uri(connectionString),
        }.CreateConnectionAsync(token);
        try
        {
            var publisherOptions = new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true
            );
            var commands = await connection.CreateChannelAsync(publisherOptions, token);
            var replies = await connection.CreateChannelAsync(publisherOptions, token);
            var intake = await connection.CreateChannelAsync(cancellationToken: token);
            return new(connection, commands, replies, intake, queuePrefix);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    public async Task SetupAsync(CancellationToken token)
    {
        foreach (string route in Routes)
            await intake.QueueDeclareAsync(
                Queue(route),
                durable: true,
                exclusive: false,
                autoDelete: false,
                cancellationToken: token
            );
    }

    /// <summary>Checks existing topology without creating queues during worker startup.</summary>
    public async Task RequireQueuesAsync(CancellationToken token)
    {
        foreach (string route in Routes)
            await intake.QueueDeclarePassiveAsync(Queue(route), token);
    }

    public async Task PublishCommandAsync(OutgoingMessage message, CancellationToken token)
    {
        if (
            message.RouteKey
                is not (
                    "inventory.issue-stock"
                    or "inventory.reserve-stock"
                    or "inventory.release-reservation"
                )
            || message.MessageName != message.RouteKey
            || message.SchemaVersion != 1
        )
            throw new InvalidDataException(
                "The Sales publisher accepts only configured Inventory v1 commands."
            );

        await PublishAsync(commands, message, StockIssueMessageAdmission.SalesProducer, token);
    }

    public async Task PublishReplyAsync(OutgoingMessage message, CancellationToken token)
    {
        if (
            !(
                message.RouteKey == "inventory.stock-issues"
                    && message.MessageName
                        is ("inventory.stock-issue-recorded" or "inventory.stock-issue-declined")
                || message.RouteKey == "inventory.reservation-outcomes"
                    && message.MessageName
                        is ("inventory.reservation-decided" or "inventory.reservation-released")
            )
            || message.SchemaVersion != 1
        )
            throw new InvalidDataException(
                "The Inventory publisher accepts only configured Inventory v1 outcomes."
            );

        await PublishAsync(replies, message, StockIssueReplyAdmission.Producer, token);
    }

    private async Task PublishAsync(
        IChannel channel,
        OutgoingMessage message,
        string producer,
        CancellationToken token
    )
    {
        var current = Activity.Current;
        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            MessageId = message.MessageId.ToString("D"),
            Type = message.MessageName,
            CorrelationId = message.CorrelationId,
            Headers = new Dictionary<string, object?>
            {
                ["producer-module"] = producer,
                ["schema-version"] = message.SchemaVersion,
                ["owner-key"] = message.TenantKey,
                ["causation-id"] = message.CausationId,
                ["traceparent"] = current?.Id ?? message.TraceParent,
                ["tracestate"] = current is null ? message.TraceState : current.TraceStateString,
            },
        };
        // RabbitMQ does not accept null header values. Optional diagnostics remain optional.
        foreach (
            var key in properties
                .Headers.Where(pair => pair.Value is null)
                .Select(pair => pair.Key)
                .ToArray()
        )
            properties.Headers.Remove(key);

        using var bounded = CancellationTokenSource.CreateLinkedTokenSource(token);
        bounded.CancelAfter(TimeSpan.FromSeconds(5));
        await channel.BasicPublishAsync(
            "",
            Queue(message.RouteKey),
            mandatory: true,
            properties,
            Encoding.UTF8.GetBytes(message.Payload.GetRawText()),
            bounded.Token
        );
    }

    public Task<bool> ReceiveCommandAsync(IServiceScopeFactory scopes, CancellationToken token) =>
        ReceiveAsync<InventoryDbContext>(
            scopes,
            "inventory.issue-stock",
            StockIssueMessageAdmission.SalesProducer,
            StockIssueMessageAdmission.Subscription,
            message => _ = StockIssueMessageAdmission.Validate(message),
            token
        );

    public Task<bool> ReceiveReplyAsync(IServiceScopeFactory scopes, CancellationToken token) =>
        ReceiveAsync<SalesDbContext>(
            scopes,
            "inventory.stock-issues",
            StockIssueReplyAdmission.Producer,
            StockIssueReplyAdmission.Subscription,
            StockIssueReplyAdmission.Validate,
            token
        );

    public Task<bool> ReceiveReservationAsync(
        IServiceScopeFactory scopes,
        CancellationToken token
    ) =>
        ReceiveAsync<InventoryDbContext>(
            scopes,
            "inventory.reserve-stock",
            "sales",
            StockReservationAdmission.Subscription,
            StockReservationAdmission.Validate,
            token
        );

    public Task<bool> ReceiveReleaseAsync(IServiceScopeFactory scopes, CancellationToken token) =>
        ReceiveAsync<InventoryDbContext>(
            scopes,
            "inventory.release-reservation",
            "sales",
            StockReservationAdmission.Subscription,
            StockReservationAdmission.Validate,
            token
        );

    public Task<bool> ReceiveReservationReplyAsync(
        IServiceScopeFactory scopes,
        CancellationToken token
    ) =>
        ReceiveAsync<SalesDbContext>(
            scopes,
            "inventory.reservation-outcomes",
            "inventory",
            StockReservationReplyAdmission.Subscription,
            StockReservationReplyAdmission.Validate,
            token
        );

    private async Task<bool> ReceiveAsync<TDatabase>(
        IServiceScopeFactory scopes,
        string route,
        string producer,
        string subscription,
        Action<IncomingMessage> validate,
        CancellationToken token
    )
        where TDatabase : DbContext
    {
        var delivery = await intake.BasicGetAsync(Queue(route), autoAck: false, token);
        if (delivery is null)
            return false;

        try
        {
            var properties = delivery.BasicProperties;
            if (
                !Guid.TryParseExact(properties.MessageId, "D", out Guid id)
                || id == Guid.Empty
                || string.IsNullOrWhiteSpace(properties.Type)
                || Header(properties, "producer-module") != producer
                || properties.Headers is null
                || !properties.Headers.TryGetValue("schema-version", out var schema)
                || schema is not int version
                || version <= 0
            )
                throw new InvalidDataException("The bound workflow delivery metadata is invalid.");

            using var document = JsonDocument.Parse(delivery.Body);
            var message = new IncomingMessage(
                id,
                producer,
                properties.Type!,
                version,
                document.RootElement,
                Header(properties, "owner-key"),
                properties.CorrelationId,
                Header(properties, "causation-id"),
                Header(properties, "traceparent"),
                Header(properties, "tracestate")
            );
            validate(message);
            await using var scope = scopes.CreateAsyncScope();
            var database = scope.ServiceProvider.GetRequiredService<TDatabase>();
            await using var transaction = await database.Database.BeginTransactionAsync(token);
            await scope
                .ServiceProvider.GetRequiredService<IInbox<TDatabase>>()
                .ReceiveAsync(subscription, message, token);
            await transaction.CommitAsync(token);
            await intake.BasicAckAsync(delivery.DeliveryTag, multiple: false, token);
            return true;
        }
        catch (Exception failure)
            when (failure is InvalidDataException or JsonException or EventDecodingException)
        {
            // This finite sample rejects malformed intake. A deployed consumer should bind a DLQ.
            await intake.BasicNackAsync(
                delivery.DeliveryTag,
                multiple: false,
                requeue: false,
                CancellationToken.None
            );
            throw;
        }
        catch
        {
            // Commit/ack failures can redeliver; inbox delivery identity makes that safe.
            await intake.BasicNackAsync(
                delivery.DeliveryTag,
                multiple: false,
                requeue: true,
                CancellationToken.None
            );
            throw;
        }
    }

    private string Queue(string route) => prefix + "." + route;

    private static string? Header(IReadOnlyBasicProperties properties, string name) =>
        (
            properties.Headers is not null && properties.Headers.TryGetValue(name, out var value)
                ? value
                : null
        ) switch
        {
            byte[] bytes => Encoding.UTF8.GetString(bytes),
            string text => text,
            _ => null,
        };

    public async Task DeleteQueuesAsync(CancellationToken token)
    {
        foreach (string route in Routes)
            await intake.QueueDeleteAsync(Queue(route), cancellationToken: token);
    }

    public async ValueTask DisposeAsync()
    {
        await intake.DisposeAsync();
        await replies.DisposeAsync();
        await commands.DisposeAsync();
        await connection.DisposeAsync();
    }
}

public sealed class SalesCommandPublisher(WorkflowBroker broker) : IMessagePublisher
{
    public Task PublishAsync(OutgoingMessage message, CancellationToken cancellationToken) =>
        broker.PublishCommandAsync(message, cancellationToken);
}

public sealed class InventoryReplyPublisher(WorkflowBroker broker) : IMessagePublisher
{
    public Task PublishAsync(OutgoingMessage message, CancellationToken cancellationToken) =>
        broker.PublishReplyAsync(message, cancellationToken);
}
