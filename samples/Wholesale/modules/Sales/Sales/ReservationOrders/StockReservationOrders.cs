using Gloaming.ActorIdentity;
using Gloaming.Messaging.Workflows;
using ModulithFoundry.Samples.Wholesale.Sales.Contracts;
using OrderView = ModulithFoundry.Samples.Wholesale.Sales.Contracts.StockReservationOrder;

namespace ModulithFoundry.Samples.Wholesale.Sales.ReservationOrders;

internal sealed class StockReservationOrders(
    SalesDbContext database,
    IActorContextAccessor actor,
    IWorkflow<StartStockReservationOrder, ReservationState> workflow
) : IStockReservationOrders
{
    public async Task<OrderView> StartAsync(
        StartStockReservationOrder request,
        CancellationToken cancellationToken
    )
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentOutOfRangeException.ThrowIfEqual(request.OrderId, Guid.Empty);
        ValidateLine(request.First);
        ValidateLine(request.Second);
        ArgumentOutOfRangeException.ThrowIfEqual(
            request.First.StockPositionId,
            request.Second.StockPositionId
        );

        RequireCaller();
        await using var transaction = await database.Database.BeginTransactionAsync(
            cancellationToken
        );
        var snapshot = await workflow.StartAsync(
            new(request.OrderId, database.RequiredOrganizationKey),
            request,
            cancellationToken
        );
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ReservationState.Read(snapshot);
    }

    public async Task<OrderView?> ReadAsync(Guid orderId, CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfEqual(orderId, Guid.Empty);
        _ = database.RequiredOrganizationKey;

        var snapshot = await workflow.ReadAsync(
            new(orderId, database.RequiredOrganizationKey),
            cancellationToken
        );
        return snapshot is null ? null : ReservationState.Read(snapshot);
    }

    public async Task<OrderView> RetryCompensationAsync(
        Guid orderId,
        CancellationToken cancellationToken
    )
    {
        ArgumentOutOfRangeException.ThrowIfEqual(orderId, Guid.Empty);

        RequireCaller();
        await using var transaction = await database.Database.BeginTransactionAsync(
            cancellationToken
        );
        var snapshot = await workflow.SignalAsync(
            new(orderId, database.RequiredOrganizationKey),
            new RetryReservationRelease(),
            cancellationToken
        );
        await database.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return ReservationState.Read(snapshot);
    }

    private void RequireCaller()
    {
        if (
            database.RequiredOrganizationKey is not ("wholesale-alpha" or "wholesale-beta")
            || actor.Current.Actor.Kind == ActorKind.Anonymous
        )
            throw new InvalidOperationException(
                "Reservation orders require an admitted Organization and actor."
            );
    }

    private static void ValidateLine(StockReservationLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        ArgumentOutOfRangeException.ThrowIfEqual(line.StockPositionId, Guid.Empty);
        ArgumentOutOfRangeException.ThrowIfLessThan(line.ExpectedVersion, 1);
        ArgumentOutOfRangeException.ThrowIfEqual(line.ExpectedVersion, long.MaxValue);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(line.Quantity);
    }
}
