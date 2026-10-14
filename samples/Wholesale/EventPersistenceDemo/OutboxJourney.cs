using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Gloaming.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Inventory;
using ModulithFoundry.Samples.Wholesale.Inventory.Contracts;
using RabbitMQ.Client;

namespace ModulithFoundry.Samples.Wholesale.EventPersistenceDemo;

public static class OutboxJourney
{
    public static async Task RunAsync(
        string databaseConnection,
        Uri broker,
        TextWriter output,
        CancellationToken cancellationToken
    )
    {
        var factory = new ConnectionFactory { Uri = broker, AutomaticRecoveryEnabled = false };
        await using var connection = await factory.CreateConnectionAsync(cancellationToken);
        await using var channel = await connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true
            ),
            cancellationToken
        );
        // Consumer-owned topology, not library registration or a startup migration.
        await channel.QueueDeclareAsync(
            "inventory.stock-issues",
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: cancellationToken
        );
        var services = DemoComposition.CreateServices(databaseConnection);
        services.AddScoped(_ => new InventoryRabbitMqPublisher(channel));
        services.AddPostgresOutboxDispatcher<InventoryDbContext, InventoryRabbitMqPublisher>(
            new(TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(5))
        );
        await using var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true }
        );
        Guid id = Guid.NewGuid();
        await using (var scope = Scope(provider))
        {
            var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            await database.Database.MigrateAsync(cancellationToken);
            await using var transaction = await database.Database.BeginTransactionAsync(
                cancellationToken
            );
            var commands = scope.ServiceProvider.GetRequiredService<IStockPositionCommands>();
            RequireChanged(
                await commands.OpenAsync(
                    new(id, Guid.NewGuid(), Guid.NewGuid(), "EA"),
                    cancellationToken
                )
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
                    .ReceiveAsync(new(id, 1, [new StockReceipt(5)]), cancellationToken)
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
                    .IssueAsync(new(id, 2, [new StockIssue(2)]), cancellationToken)
            );
            await database.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
        }
        // Privileged table-wide dispatch does not impersonate the owner of every message it drains.
        await using var dispatch = provider.CreateAsyncScope();
        var result = await dispatch
            .ServiceProvider.GetRequiredService<IOutboxDispatcher<InventoryDbContext>>()
            .DispatchNextAsync(cancellationToken);
        output.WriteLine(
            $"inventory stock issue: state/events/outbox committed; RabbitMQ dispatch={result}"
        );
    }

    private static void RequireChanged(StockPositionChangeResult result)
    {
        if (result is not StockPositionChangeResult.Changed)
            throw new InvalidOperationException(
                "The finite outbox journey expected an accepted stock command."
            );
    }

    private static AsyncServiceScope Scope(ServiceProvider provider)
    {
        var scope = provider.CreateAsyncScope();
        scope
            .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
            .Initialize(TenantContext.ForTenant(new TenantId("wholesale-alpha")));
        return scope;
    }
}
