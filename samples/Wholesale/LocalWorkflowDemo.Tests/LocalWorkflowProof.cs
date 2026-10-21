using System.Data;
using Gloaming.ActorIdentity;
using Gloaming.Messaging;
using Gloaming.Messaging.EntityFrameworkCore;
using Gloaming.Tenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Inventory;
using ModulithFoundry.Samples.Wholesale.Inventory.Contracts;
using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;
using ModulithFoundry.Tests.Infrastructure;
using Npgsql;

namespace ModulithFoundry.Samples.Wholesale.LocalWorkflowDemo.Tests;

internal sealed class LocalWorkflowProof(ServiceProvider services, string sales, string inventory)
    : IAsyncDisposable
{
    internal const string Alpha = "wholesale-alpha";
    internal const string Beta = "wholesale-beta";
    internal ServiceProvider Services => services;
    internal string Sales => sales;
    internal string Inventory => inventory;
    internal Dictionary<string, string> Environment =>
        new()
        {
            ["ConnectionStrings__Sales"] = sales,
            ["ConnectionStrings__Inventory"] = inventory,
            ["LeaseSeconds"] = "2",
        };

    internal static async Task<LocalWorkflowProof> CreateAsync(
        PostgreSqlFixture postgres,
        CancellationToken token,
        DbCommandInterceptor? interceptor = null
    )
    {
        string sales = await postgres.CreateDatabaseAsync(token);
        string inventory = await postgres.CreateDatabaseAsync(token);
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddLocalReservationDemo(sales, inventory, TimeSpan.FromSeconds(2));
        if (interceptor is not null)
            services.AddDbContext<InventoryDbContext>(options =>
                options.AddInterceptors(interceptor)
            );

        var provider = services.BuildServiceProvider(
            new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true }
        );
        try
        {
            await LocalWorkflowSetup.MigrateAsync(provider, token);
            return new(provider, sales, inventory);
        }
        catch
        {
            await provider.DisposeAsync();
            throw;
        }
    }

    internal AsyncServiceScope Scope(string organization = Alpha, bool actor = false)
    {
        var scope = services.CreateAsyncScope();
        scope
            .ServiceProvider.GetRequiredService<ITenantContextInitializer>()
            .Initialize(TenantContext.ForTenant(new TenantId(organization)));
        if (actor)
            scope
                .ServiceProvider.GetRequiredService<IActorContextInitializer>()
                .Initialize(new ActorContext(Actor.System(new ActorId("sales.local-proof"))));

        return scope;
    }

    internal async Task<Guid> SeedAsync(CancellationToken token, string organization = Alpha)
    {
        Guid id = Guid.NewGuid();
        await LocalWorkflowSetup.SeedAsync(services, id, organization, token);
        return id;
    }

    internal static StartStockReservationOrder Request(
        Guid first,
        Guid second,
        decimal secondQuantity = 2
    ) => new(Guid.NewGuid(), new(first, 2, 2), new(second, 2, secondQuantity));

    internal async Task<StockReservationOrder> StartAsync(
        StartStockReservationOrder request,
        CancellationToken token
    )
    {
        await using var scope = Scope(actor: true);
        return await scope
            .ServiceProvider.GetRequiredService<IStockReservationOrders>()
            .StartAsync(request, token);
    }

    internal async Task<StockReservationOrder> ReadAsync(Guid order, CancellationToken token)
    {
        await using var scope = Scope();
        return (
            await scope
                .ServiceProvider.GetRequiredService<IStockReservationOrders>()
                .ReadAsync(order, token)
        )!;
    }

    internal async Task<StockPositionHistory> StockAsync(Guid stock, CancellationToken token)
    {
        await using var scope = Scope();
        var database = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        // Poll one snapshot while the real workers can commit between the stream and state reads.
        await using var transaction = await database.Database.BeginTransactionAsync(
            IsolationLevel.RepeatableRead,
            token
        );
        return (
            await scope
                .ServiceProvider.GetRequiredService<IStockPositionQueries>()
                .ReadCurrentAsync(stock, token)
        )!;
    }

    internal async Task<OutgoingMessage> PendingAsync<TContext>(CancellationToken token)
        where TContext : DbContext
    {
        await using var scope = Scope();
        var row = await scope
            .ServiceProvider.GetRequiredService<TContext>()
            .Set<OutboxMessageRecord>()
            .IgnoreQueryFilters()
            .AsNoTracking()
            .SingleAsync(row => row.DispatchedAt == null, token);
        return new(
            row.MessageId,
            row.RouteKey,
            row.MessageName,
            row.SchemaVersion,
            row.Payload,
            row.TenantKey,
            row.CorrelationId,
            row.CausationId,
            row.TraceParent,
            row.TraceState
        );
    }

    internal async Task<OutboxDispatchResult> DispatchAsync<TContext>(CancellationToken token)
        where TContext : DbContext
    {
        await using var scope = services.CreateAsyncScope();
        return await scope
            .ServiceProvider.GetRequiredService<IOutboxDispatcher<TContext>>()
            .DispatchNextAsync(token);
    }

    internal async Task<InboxProcessingResult> ProcessAsync<TContext>(CancellationToken token)
        where TContext : DbContext
    {
        await using var scope = services.CreateAsyncScope();
        string subscription =
            typeof(TContext) == typeof(SalesDbContext)
                ? StockReservationReplyAdmission.Subscription
                : StockReservationAdmission.Subscription;
        return await scope
            .ServiceProvider.GetRequiredService<IInboxProcessor<TContext>>()
            .ProcessNextAsync(subscription, token);
    }

    internal async Task StepAsync(CancellationToken token)
    {
        Assert.Equal(OutboxDispatchResult.Published, await DispatchAsync<SalesDbContext>(token));
        Assert.Equal(
            InboxProcessingResult.Processed,
            await ProcessAsync<InventoryDbContext>(token)
        );
        Assert.Equal(
            OutboxDispatchResult.Published,
            await DispatchAsync<InventoryDbContext>(token)
        );
        Assert.Equal(InboxProcessingResult.Processed, await ProcessAsync<SalesDbContext>(token));
    }

    internal static ReservationDelivery Delivery(OutgoingMessage message, Guid? id = null) =>
        new(
            id ?? message.MessageId,
            message.TenantKey!,
            Guid.Parse(message.CorrelationId!),
            message.CausationId is null ? null : Guid.Parse(message.CausationId),
            message.TraceParent,
            message.TraceState
        );

    internal static async Task SqlAsync(
        string connectionString,
        string sql,
        CancellationToken token
    )
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(token);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(token);
    }

    internal static async Task<long> CountAsync(
        string connectionString,
        string sql,
        CancellationToken token
    )
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync(token);
        await using var command = new NpgsqlCommand(sql, connection);
        return (long)(await command.ExecuteScalarAsync(token))!;
    }

    internal static async Task EventuallyAsync(Func<Task<bool>> condition, CancellationToken token)
    {
        while (!await condition())
            await Task.Delay(50, token);
    }

    public ValueTask DisposeAsync() => services.DisposeAsync();
}
