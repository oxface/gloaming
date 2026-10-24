using System.Text;
using System.Text.Json;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Gloaming.Samples.Wholesale.IntegrationTests.Scenarios.EventPersistence;
using Gloaming.Samples.Wholesale.Inventory;
using Gloaming.Samples.Wholesale.Inventory.Contracts;
using Gloaming.Tenancy;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace Gloaming.Samples.Wholesale.IntegrationTests.EventPersistence;

public sealed class OutboxDispatchTests(PostgreSqlFixture postgres, RabbitMqFixture broker)
    : IClassFixture<PostgreSqlFixture>,
        IClassFixture<RabbitMqFixture>
{
    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ConfirmedBrokerAcceptanceThenDatabaseFailureRepeatsIdentityOwnerAndPayload()
    {
        string connectionString = await SeedIssueAsync();
        await using var brokerConnection = await ConnectAsync();
        await using var channel = await ChannelAsync(brokerConnection);
        await DeclareAsync(channel);
        await SqlAsync(
            connectionString,
            "ALTER TABLE inventory.outbox_messages ADD CONSTRAINT test_completion CHECK (dispatched_at IS NULL)"
        );
        await using var provider = DemoComposition
            .CreateServices(connectionString)
            .BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
            await Assert.ThrowsAsync<OutboxCompletionException>(() =>
                Dispatcher(scope, channel).DispatchNextAsync(Token)
            );
        var first = await ReceiveAsync(channel);
        await SqlAsync(
            connectionString,
            "ALTER TABLE inventory.outbox_messages DROP CONSTRAINT test_completion; UPDATE inventory.outbox_messages SET lease_until = clock_timestamp() - interval '1 second'"
        );
        await using (var recovery = provider.CreateAsyncScope())
            Assert.Equal(
                OutboxDispatchResult.Published,
                await Dispatcher(recovery, channel).DispatchNextAsync(Token)
            );
        var second = await ReceiveAsync(channel);
        Assert.Equal(first.MessageId, second.MessageId);
        Assert.Equal(first.CorrelationId, second.CorrelationId);
        Assert.Equal("broker-tenant", first.Owner);
        Assert.Equal(first.Owner, second.Owner);
        Assert.Equal(first.Payload.GetRawText(), second.Payload.GetRawText());
        Assert.Equal(first.MessageId, first.Payload.GetProperty("messageId").GetGuid().ToString());
        Assert.True(Guid.TryParse(first.CorrelationId, out var conversation));
        Assert.NotEqual(Guid.Empty, conversation);
        Assert.NotEqual(
            first.CorrelationId,
            first.Payload.GetProperty("stockPositionId").GetGuid().ToString()
        );
        Assert.Equal(2m, first.Payload.GetProperty("issuedQuantity").GetDecimal());
        await using var read = provider.CreateAsyncScope();
        read.ServiceProvider.GetRequiredService<ITenantContextInitializer>()
            .Initialize(TenantContext.ForTenant(new TenantId("broker-tenant")));
        var row = await read
            .ServiceProvider.GetRequiredService<InventoryDbContext>()
            .Set<OutboxMessageRecord>()
            .SingleAsync(Token);
        Assert.NotNull(row.DispatchedAt);
        Assert.Equal(2, row.Attempts);
    }

    [Fact]
    public async Task MandatoryUnroutablePublicationDoesNotCompleteTheOutboxAndLaterRouteRecovers()
    {
        string connectionString = await SeedIssueAsync();
        await using var connection = await ConnectAsync();
        await using var channel = await ChannelAsync(connection);
        await DeclareAsync(channel);
        await channel.QueueDeleteAsync(
            "inventory.stock-issues",
            ifUnused: false,
            ifEmpty: false,
            cancellationToken: Token
        );
        await using var provider = DemoComposition
            .CreateServices(connectionString)
            .BuildServiceProvider();
        await using (var scope = provider.CreateAsyncScope())
            await Assert.ThrowsAsync<PublishReturnException>(() =>
                Dispatcher(scope, channel).DispatchNextAsync(Token)
            );
        await DeclareAsync(channel);
        await using (var scope = provider.CreateAsyncScope())
            Assert.Equal(
                OutboxDispatchResult.Published,
                await Dispatcher(scope, channel).DispatchNextAsync(Token)
            );
        Assert.NotNull((await ReceiveAsync(channel)).MessageId);
    }

    [Fact]
    public async Task ExecutableOutboxJourneyKeepsProducerAndBrokerSetupExplicit()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using var brokerConnection = await ConnectAsync();
        await using var channel = await ChannelAsync(brokerConnection);
        await DeclareAsync(channel);
        using var output = new StringWriter();
        await OutboxJourney.RunAsync(connection, new Uri(broker.ConnectionString), output, Token);
        Assert.Contains("RabbitMQ dispatch=Published", output.ToString(), StringComparison.Ordinal);
        Assert.Equal(
            2m,
            (await ReceiveAsync(channel)).Payload.GetProperty("issuedQuantity").GetDecimal()
        );
    }

    private async Task<string> SeedIssueAsync()
    {
        string connection = await postgres.CreateDatabaseAsync(Token);
        await using var provider = DemoComposition
            .CreateServices(connection)
            .BuildServiceProvider();
        Guid id = Guid.NewGuid();
        await CommitAsync(async commands =>
            await commands.OpenAsync(new(id, Guid.NewGuid(), Guid.NewGuid(), "EA"), Token)
        );
        await CommitAsync(async commands =>
            await commands.ReceiveAsync(new(id, 1, [new StockReceipt(5)]), Token)
        );
        await CommitAsync(async commands =>
            await commands.IssueAsync(new(id, 2, [new StockIssue(2)]), Token)
        );
        return connection;

        async Task CommitAsync(
            Func<IStockPositionCommands, Task<StockPositionChangeResult>> command
        )
        {
            await using var scope = provider.CreateAsyncScope();
            scope
                .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
                .Initialize(TenantContext.ForTenant(new TenantId("broker-tenant")));
            var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            await database.Database.MigrateAsync(Token);
            await using var transaction = await database.Database.BeginTransactionAsync(Token);
            Assert.IsType<StockPositionChangeResult.Changed>(
                await command(scope.ServiceProvider.GetRequiredService<IStockPositionCommands>())
            );
            await database.SaveChangesAsync(Token);
            await transaction.CommitAsync(Token);
        }
    }

    private Task<IConnection> ConnectAsync() =>
        new ConnectionFactory
        {
            Uri = new Uri(broker.ConnectionString),
            AutomaticRecoveryEnabled = false,
        }.CreateConnectionAsync(Token);

    private static Task<IChannel> ChannelAsync(IConnection connection) =>
        connection.CreateChannelAsync(
            new CreateChannelOptions(
                publisherConfirmationsEnabled: true,
                publisherConfirmationTrackingEnabled: true
            ),
            Token
        );

    private static PostgresOutboxDispatcher<InventoryDbContext> Dispatcher(
        AsyncServiceScope scope,
        IChannel channel
    ) =>
        new(
            scope.ServiceProvider.GetRequiredService<InventoryDbContext>(),
            new InventoryRabbitMqPublisher(channel),
            new(TimeSpan.FromSeconds(30), TimeSpan.Zero)
        );

    private static async Task DeclareAsync(IChannel channel)
    {
        await channel.QueueDeclareAsync(
            "inventory.stock-issues",
            durable: true,
            exclusive: false,
            autoDelete: false,
            cancellationToken: Token
        );
        await channel.QueuePurgeAsync("inventory.stock-issues", Token);
    }

    private static async Task<(
        string MessageId,
        string? CorrelationId,
        string Owner,
        JsonElement Payload
    )> ReceiveAsync(IChannel channel)
    {
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(Token);
        timeout.CancelAfter(TimeSpan.FromSeconds(10));
        BasicGetResult? delivery;
        while (
            (
                delivery = await channel.BasicGetAsync(
                    "inventory.stock-issues",
                    autoAck: false,
                    timeout.Token
                )
            )
                is null
        )
            await Task.Delay(10, timeout.Token);
        using var payload = JsonDocument.Parse(delivery.Body);
        var result = (
            delivery.BasicProperties.MessageId!,
            delivery.BasicProperties.CorrelationId,
            Encoding.UTF8.GetString((byte[])delivery.BasicProperties.Headers!["owner-key"]!),
            payload.RootElement.Clone()
        );
        // A transport observer only. The inbox slice will ACK after committed durable intake/business effects.
        await channel.BasicAckAsync(delivery.DeliveryTag, multiple: false, timeout.Token);
        return result;
    }

    private static async Task SqlAsync(string connection, string sql)
    {
        await using var database = new NpgsqlConnection(connection);
        await database.OpenAsync(Token);
        await using var command = new NpgsqlCommand(sql, database);
        await command.ExecuteNonQueryAsync(Token);
    }
}
