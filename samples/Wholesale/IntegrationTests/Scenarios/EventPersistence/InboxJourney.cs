using System.Text;
using System.Text.Json;
using Gloaming.Auditing.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Samples.Wholesale.Inventory;
using Gloaming.Samples.Wholesale.Inventory.Contracts;
using Gloaming.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;

namespace Gloaming.Samples.Wholesale.IntegrationTests.Scenarios.EventPersistence;

public static class InboxJourney
{
    private static readonly JsonSerializerOptions WireJson = new(JsonSerializerDefaults.Web);

    public static async Task RunAsync(
        string databaseConnection,
        Uri broker,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        var services = DemoComposition.CreateServices(databaseConnection);
        services.AddStockIssueInbox();
        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
        );
        Guid stock = Guid.NewGuid();
        await using (var scope = Scope(provider))
        {
            var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            await database.Database.MigrateAsync(cancellationToken);
            await using var transaction = await database.Database.BeginTransactionAsync(
                cancellationToken
            );
            RequireChanged(
                await scope
                    .ServiceProvider.GetRequiredService<IStockPositionCommands>()
                    .OpenAsync(new(stock, Guid.NewGuid(), Guid.NewGuid(), "EA"), cancellationToken)
            );
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        await using (var scope = Scope(provider))
        {
            var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(
                cancellationToken
            );
            RequireChanged(
                await scope
                    .ServiceProvider.GetRequiredService<IStockPositionCommands>()
                    .ReceiveAsync(new(stock, 1, [new StockReceipt(5)]), cancellationToken)
            );
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }

        var factory = new ConnectionFactory { Uri = broker, AutomaticRecoveryEnabled = false };
        await using var connection = await factory.CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true
            ),
            cancellationToken
        );
        string queue = StockIssueMessageAdmission.Subscription + "." + Guid.NewGuid().ToString("N");
        await channel.QueueDeclareAsync(
            queue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: cancellationToken
        );
        Guid messageId = Guid.NewGuid();
        string correlationId = Guid.CreateVersion7().ToString("D");
        var properties = new BasicProperties
        {
            Persistent = true,
            ContentType = "application/json",
            MessageId = messageId.ToString(),
            Type = StockIssueMessageAdmission.Subscription,
            CorrelationId = correlationId,
            Headers = new Dictionary<string, object?>
            {
                ["producer-module"] = StockIssueMessageAdmission.Producer,
                ["schema-version"] = 1,
                ["owner-key"] = "wholesale-alpha",
            },
        };
        await channel.BasicPublishAsync(
            "",
            queue,
            mandatory: true,
            properties,
            Encoding.UTF8.GetBytes(
                JsonSerializer.Serialize(new IssueStockV1(stock, 2, 2), WireJson)
            ),
            cancellationToken
        );
        var delivery =
            await channel.BasicGetAsync(queue, autoAck: false, cancellationToken)
            ?? throw new InvalidOperationException("The confirmed command was not available.");
        var intake = await new InventoryRabbitMqReceiver(
            channel,
            provider.GetRequiredService<IServiceScopeFactory>()
        ).ReceiveAsync(delivery, cancellationToken);
        await using var processing = provider.CreateAsyncScope();
        var result = await processing
            .ServiceProvider.GetRequiredService<IInboxProcessor<InventoryDbContext>>()
            .ProcessNextAsync(StockIssueMessageAdmission.Subscription, cancellationToken);
        await using var read = Scope(provider);
        var audit = await read
            .ServiceProvider.GetRequiredService<InventoryDbContext>()
            .Set<AuditRecord>()
            .SingleAsync(
                row => row.SubjectType == "stock-position" && row.SubjectKey == stock.ToString("D"),
                cancellationToken
            );
        var reply = await read
            .ServiceProvider.GetRequiredService<InventoryDbContext>()
            .Set<OutboxMessageRecord>()
            .SingleAsync(row => row.CausationId == messageId.ToString("D"), cancellationToken);
        output.WriteLine(
            $"command {messageId}: intake={intake}; processing={result}; audit={audit.Action} actor={audit.ActorKey}; reply={reply.MessageId} reply-cause={reply.CausationId}; stock events/state/audit/reply saved together"
        );
        await channel.QueueDeleteAsync(queue, ifUnused: false, ifEmpty: true, cancellationToken);
    }

    private static AsyncServiceScope Scope(ServiceProvider provider)
    {
        var scope = provider.CreateAsyncScope();
        scope
            .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
            .Initialize(TenantContext.ForTenant(new TenantId("wholesale-alpha")));
        return scope;
    }

    private static void RequireChanged(StockPositionChangeResult result)
    {
        if (result is not StockPositionChangeResult.Changed)
            throw new InvalidOperationException(
                "The finite inbox journey expected accepted seed commands."
            );
    }
}
