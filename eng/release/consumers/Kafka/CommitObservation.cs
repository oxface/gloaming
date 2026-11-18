using System.Data.Common;
using Gloaming.Messaging;
using Microsoft.EntityFrameworkCore.Diagnostics;

// Fault injection belongs to this proof, not the native transport adapter.
sealed class CommitObservation(string phase) : DbTransactionInterceptor
{
    public override async ValueTask<InterceptionResult> TransactionCommittingAsync(
        DbTransaction transaction,
        TransactionEventData eventData,
        InterceptionResult result,
        CancellationToken cancellationToken = default
    )
    {
        if (phase == "before-commit")
            await KafkaJourney.BarrierAsync(0);

        return result;
    }

    public override async Task TransactionCommittedAsync(
        DbTransaction transaction,
        TransactionEndEventData eventData,
        CancellationToken cancellationToken = default
    )
    {
        if (phase is "after-commit" or "rebalance")
            await KafkaJourney.BarrierAsync(0);

        if (phase == "lose-observation")
            throw new CommitObservationLost();
    }
}

sealed class CommitObservationLost : Exception;

sealed class PublicationBarrier(IMessagePublisher publisher) : IMessagePublisher
{
    public async Task PublishAsync(OutgoingMessage message, CancellationToken token)
    {
        await publisher.PublishAsync(message, token);
        await KafkaJourney.BarrierAsync(0);
    }
}
