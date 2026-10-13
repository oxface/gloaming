using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using ModulithFoundry.Samples.Wholesale.Inventory;
using ModulithFoundry.Samples.Wholesale.Inventory.Contracts;
using ModulithFoundry.Samples.Wholesale.Sales;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;
using Rootbolt.Messaging;
using Rootbolt.Messaging.EntityFrameworkCore;

namespace ModulithFoundry.Samples.Wholesale.WorkflowDemo.Tests;

internal sealed class ReservationProof(WorkflowProof workflow) : IAsyncDisposable
{
    internal WorkflowProof Workflow => workflow;

    internal async Task<StockReservationOrder> StartAsync(
        StartStockReservationOrder request,
        CancellationToken token,
        string organization = WorkflowProof.Alpha
    )
    {
        await using var scope = workflow.Scope(organization, actor: true);
        return await scope
            .ServiceProvider.GetRequiredService<IStockReservationOrders>()
            .StartAsync(request, token);
    }

    internal async Task<StockReservationOrder?> ReadAsync(
        Guid id,
        CancellationToken token,
        string organization = WorkflowProof.Alpha
    )
    {
        await using var scope = workflow.Scope(organization);
        return await scope
            .ServiceProvider.GetRequiredService<IStockReservationOrders>()
            .ReadAsync(id, token);
    }

    internal async Task<StockReservationOrder> RetryAsync(Guid id, CancellationToken token)
    {
        await using var scope = workflow.Scope(actor: true);
        return await scope
            .ServiceProvider.GetRequiredService<IStockReservationOrders>()
            .RetryCompensationAsync(id, token);
    }

    internal async Task<StockPositionHistory> StockAsync(
        Guid id,
        CancellationToken token,
        bool history = false
    )
    {
        await using var scope = workflow.Scope();
        return (
            history
                ? await scope
                    .ServiceProvider.GetRequiredService<IStockPositionHistory>()
                    .ReadCurrentAsync(id, token)
                : await scope
                    .ServiceProvider.GetRequiredService<IStockPositionQueries>()
                    .ReadCurrentAsync(id, token)
        )!;
    }

    internal async Task<IncomingMessage> StepAsync(CancellationToken token, bool release = false)
    {
        await SendCommandAsync(token, release);
        return await ReplyAsync(token);
    }

    internal async Task SendCommandAsync(CancellationToken token, bool release = false)
    {
        Assert.Equal(
            OutboxDispatchResult.Published,
            await workflow.DispatchAsync<SalesDbContext>(token)
        );
        var scopes = workflow.Services.GetRequiredService<IServiceScopeFactory>();
        Assert.True(
            release
                ? await workflow.Broker.ReceiveReleaseAsync(scopes, token)
                : await workflow.Broker.ReceiveReservationAsync(scopes, token)
        );
        Assert.Equal(
            InboxProcessingResult.Processed,
            await workflow.ProcessAsync<InventoryDbContext>(
                StockReservationAdmission.Subscription,
                token
            )
        );
    }

    internal async Task<IncomingMessage> PendingReplyAsync(CancellationToken token)
    {
        await using var scope = workflow.Scope();
        var record = await scope
            .ServiceProvider.GetRequiredService<InventoryDbContext>()
            .Set<OutboxMessageRecord>()
            .AsNoTracking()
            .SingleAsync(row => row.DispatchedAt == null, token);
        return new(
            record.MessageId,
            "inventory",
            record.MessageName,
            record.SchemaVersion,
            record.Payload,
            record.TenantKey,
            record.CorrelationId,
            record.CausationId
        );
    }

    internal async Task<IncomingMessage> ReplyAsync(CancellationToken token)
    {
        var message = await PendingReplyAsync(token);
        Assert.Equal(
            OutboxDispatchResult.Published,
            await workflow.DispatchAsync<InventoryDbContext>(token)
        );
        Assert.True(
            await workflow.Broker.ReceiveReservationReplyAsync(
                workflow.Services.GetRequiredService<IServiceScopeFactory>(),
                token
            )
        );
        Assert.Equal(
            InboxProcessingResult.Processed,
            await workflow.ProcessAsync<SalesDbContext>(
                StockReservationReplyAdmission.Subscription,
                token
            )
        );
        return message;
    }

    internal async Task<ReserveStockV1> PendingReserveAsync(CancellationToken token)
    {
        await using var scope = workflow.Scope();
        var record = await scope
            .ServiceProvider.GetRequiredService<SalesDbContext>()
            .Set<OutboxMessageRecord>()
            .AsNoTracking()
            .SingleAsync(row => row.DispatchedAt == null, token);
        return record.Payload.Deserialize<ReserveStockV1>(WorkflowProof.Json)!;
    }

    internal async Task EnqueueAsync<TPayload>(
        string name,
        TPayload payload,
        Guid correlation,
        CancellationToken token
    )
        where TPayload : notnull
    {
        await using var scope = workflow.Scope();
        var database = scope.ServiceProvider.GetRequiredService<SalesDbContext>();
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        scope
            .ServiceProvider.GetRequiredService<IOutbox<SalesDbContext>>()
            .Enqueue(
                OutgoingMessage.FromPayload(
                    Guid.NewGuid(),
                    name,
                    name,
                    1,
                    payload,
                    WorkflowProof.Json,
                    WorkflowProof.Alpha,
                    correlation.ToString("D"),
                    Guid.NewGuid().ToString("D")
                )
            );
        await database.SaveChangesAsync(token);
        await transaction.CommitAsync(token);
    }

    internal async Task IntakeAsync<TDatabase>(
        string subscription,
        IncomingMessage message,
        CancellationToken token
    )
        where TDatabase : DbContext
    {
        // Focused negative/race tests deliberately bypass broker admission, retaining handler checks.
        await using var scope = workflow.Services.CreateAsyncScope();
        var database = scope.ServiceProvider.GetRequiredService<TDatabase>();
        await using var transaction = await database.Database.BeginTransactionAsync(token);
        await scope
            .ServiceProvider.GetRequiredService<IInbox<TDatabase>>()
            .ReceiveAsync(subscription, message, token);
        await transaction.CommitAsync(token);
    }

    internal static IncomingMessage Redelivery(
        IncomingMessage original,
        Guid? cause = null,
        object? payload = null
    ) =>
        new(
            Guid.NewGuid(),
            original.ProducerKey,
            original.MessageName,
            original.SchemaVersion,
            payload is null
                ? original.Payload
                : JsonSerializer.SerializeToElement(payload, WorkflowProof.Json),
            original.TenantKey,
            original.CorrelationId,
            cause?.ToString("D") ?? original.CausationId
        );

    public ValueTask DisposeAsync() => workflow.DisposeAsync();
}
