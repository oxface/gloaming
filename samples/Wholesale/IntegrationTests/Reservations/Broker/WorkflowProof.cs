using System.Text.Json;
using Gloaming.ActorIdentity;
using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Messaging.EntityFrameworkCore.Postgres;
using Gloaming.Samples.Wholesale.Inventory;
using Gloaming.Samples.Wholesale.Inventory.Contracts;
using Gloaming.Samples.Wholesale.Sales;
using Gloaming.Samples.Wholesale.Sales.Contracts;
using Gloaming.Samples.Wholesale.Worker.Broker;
using Gloaming.Tenancy;
using Gloaming.Tests.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;

namespace Gloaming.Samples.Wholesale.IntegrationTests.Reservations.Broker;

internal sealed class WorkflowProof(
    ServiceProvider services,
    WorkflowBroker broker,
    string salesConnection,
    string inventoryConnection,
    string brokerConnection,
    string queuePrefix
) : IAsyncDisposable
{
    internal const string Alpha = "wholesale-alpha";
    internal const string Beta = "wholesale-beta";
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);
    internal ServiceProvider Services => services;
    internal WorkflowBroker Broker => broker;
    internal string SalesConnection => salesConnection;
    internal string InventoryConnection => inventoryConnection;
    internal Dictionary<string, string> Environment =>
        new()
        {
            ["ConnectionStrings__Sales"] = salesConnection,
            ["ConnectionStrings__Inventory"] = inventoryConnection,
            ["ConnectionStrings__RabbitMq"] = brokerConnection,
            ["QueuePrefix"] = queuePrefix,
        };

    internal static async Task<WorkflowProof> CreateAsync(
        PostgreSqlFixture postgres,
        RabbitMqFixture rabbit,
        CancellationToken token
    )
    {
        string sales = await postgres.CreateDatabaseAsync(token);
        string inventory = await postgres.CreateDatabaseAsync(token);
        string prefix = "proof." + Guid.NewGuid().ToString("N");
        var broker = await WorkflowBroker.OpenAsync(rabbit.ConnectionString, prefix, token);
        var services = new ServiceCollection();
        services.AddWorkflowDemo(sales, inventory);
        services.AddSingleton(broker);
        services.AddPostgresOutboxDispatcher<SalesDbContext, SalesCommandPublisher>(
            new(TimeSpan.FromSeconds(1), TimeSpan.Zero)
        );
        services.AddPostgresOutboxDispatcher<InventoryDbContext, InventoryReplyPublisher>(
            new(TimeSpan.FromSeconds(1), TimeSpan.Zero)
        );
        var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }
        );
        var proof = new WorkflowProof(
            provider,
            broker,
            sales,
            inventory,
            rabbit.ConnectionString,
            prefix
        );
        try
        {
            await using var setup = provider.CreateAsyncScope();
            await setup
                .ServiceProvider.GetRequiredService<SalesDbContext>()
                .Database.MigrateAsync(token);
            await setup
                .ServiceProvider.GetRequiredService<InventoryDbContext>()
                .Database.MigrateAsync(token);
            await broker.SetupAsync(token);
            return proof;
        }
        catch
        {
            await proof.DisposeAsync();
            throw;
        }
    }

    internal AsyncServiceScope Scope(string owner = Alpha, bool actor = false)
    {
        var scope = services.CreateAsyncScope();
        scope
            .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
            .Initialize(TenantContext.ForTenant(new TenantId(owner)));
        if (actor)
            scope
                .ServiceProvider.GetRequiredService<IActorContextInitializer>()
                .Initialize(new ActorContext(Actor.System(new ActorId("sales.proof"))));

        return scope;
    }

    internal async Task<Guid> SeedAsync(CancellationToken token, string owner = Alpha)
    {
        Guid stock = Guid.NewGuid();
        await using (var scope = Scope(owner))
        {
            var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(token);
            Assert.IsType<StockPositionChangeResult.Changed>(
                await scope
                    .ServiceProvider.GetRequiredService<IStockPositionCommands>()
                    .OpenAsync(new(stock, Guid.NewGuid(), Guid.NewGuid(), "EA"), token)
            );
            await database.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
        }

        await using (var scope = Scope(owner))
        {
            var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
            await using var transaction = await database.Database.BeginTransactionAsync(token);
            Assert.IsType<StockPositionChangeResult.Changed>(
                await scope
                    .ServiceProvider.GetRequiredService<IStockPositionCommands>()
                    .ReceiveAsync(new(stock, 1, [new StockReceipt(5)]), token)
            );
            await database.SaveChangesAsync(token);
            await transaction.CommitAsync(token);
        }

        return stock;
    }

    internal async Task<StockIssueRequest> StartAsync(
        StartStockIssueRequest request,
        CancellationToken token,
        string owner = Alpha
    )
    {
        await using var scope = Scope(owner, actor: true);
        return await scope
            .ServiceProvider.GetRequiredService<IStockIssueRequests>()
            .StartAsync(request, token);
    }

    internal async Task<StockIssueRequest?> ReadAsync(
        Guid id,
        CancellationToken token,
        string owner = Alpha
    )
    {
        await using var scope = Scope(owner);
        return await scope
            .ServiceProvider.GetRequiredService<IStockIssueRequests>()
            .ReadAsync(id, token);
    }

    internal async Task<int> ExpireAsync(
        CancellationToken token,
        string owner = Alpha,
        int size = 64
    )
    {
        await using var scope = Scope(owner);
        return await scope
            .ServiceProvider.GetRequiredService<StockIssueDeadlines>()
            .MarkOverdueAsync(size, token);
    }

    internal async Task<InboxProcessingResult> ProcessAsync<TDatabase>(
        string subscription,
        CancellationToken token
    )
        where TDatabase : DbContext
    {
        await using var scope = services.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<IInboxProcessor<TDatabase>>()
            .ProcessNextAsync(subscription, token);
    }

    internal async Task<OutboxDispatchResult> DispatchAsync<TDatabase>(CancellationToken token)
        where TDatabase : DbContext
    {
        await using var scope = services.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<IOutboxDispatcher<TDatabase>>()
            .DispatchNextAsync(token);
    }

    internal async Task<IncomingMessage> InventoryReplyAsync(CancellationToken token)
    {
        await using var scope = Scope();
        var outgoing = await scope
            .ServiceProvider.GetRequiredService<InventoryDbContext>()
            .Set<OutboxMessageRecord>()
            .AsNoTracking()
            .SingleAsync(token);
        return new(
            outgoing.MessageId,
            "inventory",
            outgoing.MessageName,
            outgoing.SchemaVersion,
            outgoing.Payload,
            outgoing.TenantKey,
            outgoing.CorrelationId,
            outgoing.CausationId,
            outgoing.TraceParent,
            outgoing.TraceState
        );
    }

    internal async Task<InboxReceiveResult> IntakeAsync(
        IncomingMessage message,
        CancellationToken token
    )
    {
        // Deliberately bypass transport admission in some negative tests to verify handler admission too.
        await using var scope = services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        var result = await scope
            .ServiceProvider.GetRequiredService<IInbox<SalesDbContext>>()
            .ReceiveAsync(StockIssueReplyAdmission.Subscription, message, token);
        await transaction.CommitAsync(token);
        return result;
    }

    internal async Task DeliverCommandAsync(CancellationToken token)
    {
        Assert.Equal(OutboxDispatchResult.Published, await DispatchAsync<SalesDbContext>(token));
        Assert.True(
            await broker.ReceiveCommandAsync(
                services.GetRequiredService<IServiceScopeFactory>(),
                token
            )
        );
        Assert.Equal(
            InboxProcessingResult.Processed,
            await ProcessAsync<InventoryDbContext>(StockIssueMessageAdmission.Subscription, token)
        );
    }

    internal static IncomingMessage Recorded(
        StockIssueRequest progress,
        Guid? id = null,
        string owner = Alpha,
        decimal? remaining = null,
        DateTimeOffset? recordedAt = null
    )
    {
        Guid messageId = id ?? Guid.NewGuid();
        return new(
            messageId,
            "inventory",
            "inventory.stock-issue-recorded",
            1,
            JsonSerializer.SerializeToElement(
                new StockIssueRecordedV1(
                    messageId,
                    owner,
                    progress.StockPositionId,
                    progress.ExpectedStockVersion + 1,
                    progress.Quantity,
                    remaining ?? 5 - progress.Quantity,
                    recordedAt
                        ?? DateTimeOffset
                            .Parse(
                                "2026-10-10T12:00:00Z",
                                System.Globalization.CultureInfo.InvariantCulture
                            )
                            .AddTicks(7)
                ),
                Json
            ),
            owner,
            progress.RequestId.ToString("D"),
            progress.CommandMessageId.ToString("D")
        );
    }

    internal static async Task SqlAsync(string database, string sql, CancellationToken token)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync(token);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(token);
    }

    internal static async Task<long> CountAsync(
        string database,
        string sql,
        CancellationToken token
    )
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync(token);
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync(token))!;
    }

    internal static async Task EventuallyAsync(Func<Task<bool>> condition, CancellationToken token)
    {
        while (!await condition())
            await Task.Delay(50, token);
    }

    public async ValueTask DisposeAsync()
    {
        using var cleanup = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        try
        {
            await broker.DeleteQueuesAsync(cleanup.Token);
        }
        finally
        {
            await services.DisposeAsync();
            await broker.DisposeAsync();
        }
    }
}
