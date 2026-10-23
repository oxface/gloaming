using System.Text.Json;
using Gloaming.Messaging.Workflows;
using Gloaming.Samples.Wholesale.Inventory.Contracts;
using Gloaming.Samples.Wholesale.Sales.Contracts;

namespace Gloaming.Samples.Wholesale.Sales.ReservationOrders;

internal sealed record RetryReservationRelease;

// Explicit continuations: the runner invokes only the saved method, without replaying Start.
internal static class ReservationWorkflow
{
    internal static WorkflowDefinition<StartStockReservationOrder, ReservationState> Define() =>
        new WorkflowBuilder<StartStockReservationOrder, ReservationState>(
            "stock-reservation",
            1,
            new(JsonSerializerDefaults.Web)
        )
            .StartWith(Start)
            .OnReply<StockReservationDecidedV1>(
                "first-reserved",
                "inventory.reservation-decided",
                1,
                FirstReserved
            )
            .OnReply<StockReservationDecidedV1>(
                "second-reserved",
                "inventory.reservation-decided",
                1,
                SecondReserved
            )
            .OnReply<StockReservationReleasedV1>(
                "first-released",
                "inventory.reservation-released",
                1,
                FirstReleased
            )
            .OnSignal<RetryReservationRelease>("retry-release", RetryRelease)
            .Build();

    private static WorkflowTurn<ReservationState> Start(
        WorkflowContext context,
        StartStockReservationOrder input
    )
    {
        var state = ReservationState.Create(input);
        return WorkflowTurn<ReservationState>.Request(
            state,
            ReservationMessages.Reserve(context, state.First),
            "first-reserved"
        );
    }

    private static WorkflowTurn<ReservationState> FirstReserved(
        WorkflowContext context,
        ReservationState state,
        StockReservationDecidedV1 reply
    )
    {
        ReservationState.ValidateReservationReply(state.First, reply);

        if (reply.Decision != StockReservationDecision.Reserved)
            return WorkflowTurn<ReservationState>.Complete(
                state with
                {
                    Status = StockReservationOrderStatus.Declined,
                    Refusal = reply.Decision.ToString(),
                }
            );

        return WorkflowTurn<ReservationState>.Request(
            state with
            {
                Status = StockReservationOrderStatus.AwaitingSecond,
            },
            ReservationMessages.Reserve(context, state.Second),
            "second-reserved"
        );
    }

    private static WorkflowTurn<ReservationState> SecondReserved(
        WorkflowContext context,
        ReservationState state,
        StockReservationDecidedV1 reply
    )
    {
        ReservationState.ValidateReservationReply(state.Second, reply);

        if (reply.Decision == StockReservationDecision.Reserved)
            return WorkflowTurn<ReservationState>.Complete(
                state with
                {
                    Status = StockReservationOrderStatus.Reserved,
                }
            );

        return WorkflowTurn<ReservationState>.Request(
            state with
            {
                Status = StockReservationOrderStatus.Releasing,
                Refusal = reply.Decision.ToString(),
            },
            ReservationMessages.Release(context, state.First.ReservationId),
            "first-released"
        );
    }

    private static WorkflowTurn<ReservationState> FirstReleased(
        WorkflowContext context,
        ReservationState state,
        StockReservationReleasedV1 reply
    )
    {
        ReservationState.ValidateReleaseReply(state.First, reply);

        return reply.Decision == StockReservationReleaseDecision.Released
            ? WorkflowTurn<ReservationState>.Complete(
                state with
                {
                    Status = StockReservationOrderStatus.Compensated,
                    CompensationRefusal = null,
                }
            )
            : WorkflowTurn<ReservationState>.WaitForInput(
                state with
                {
                    Status = StockReservationOrderStatus.NeedsAttention,
                    CompensationRefusal = reply.Decision.ToString(),
                },
                "retry-release"
            );
    }

    private static WorkflowTurn<ReservationState> RetryRelease(
        WorkflowContext context,
        ReservationState state,
        RetryReservationRelease signal
    ) =>
        WorkflowTurn<ReservationState>.Request(
            state with
            {
                Status = StockReservationOrderStatus.Releasing,
                CompensationRefusal = null,
            },
            ReservationMessages.Release(context, state.First.ReservationId),
            "first-released"
        );
}
