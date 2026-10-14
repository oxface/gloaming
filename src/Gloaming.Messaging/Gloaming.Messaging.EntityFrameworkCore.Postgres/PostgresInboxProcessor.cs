using System.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Gloaming.Messaging.EntityFrameworkCore.Postgres;

internal sealed partial class PostgresInboxProcessor<TDbContext>(
    TDbContext database,
    IServiceProvider services,
    InboxProcessingOptions options,
    ILogger<PostgresInboxProcessor<TDbContext>>? logger = null
) : IInboxProcessor<TDbContext>
    where TDbContext : DbContext
{
    private readonly PostgresInboxStorage storage = new(database);

    public async Task<InboxProcessingResult> ProcessNextAsync(
        string subscriptionKey,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriptionKey);
        cancellationToken.ThrowIfCancellationRequested();

        if (
            database.Database.CurrentTransaction is not null
            || Transaction.Current is not null
            || database.Database.GetEnlistedTransaction() is not null
            || database.ChangeTracker.Entries().Any()
        )
            throw new InvalidOperationException(
                "Inbox processing requires a fresh context without tracked entities or native/ambient/enlisted transactions."
            );

        var handler = services.GetRequiredKeyedService<IInboxHandler<TDbContext>>(subscriptionKey);
        IncomingMessage? delivery = null;
        MessagingTelemetry.Attempt? attempt = null;
        try
        {
            await using var transaction = await database.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.ReadCommitted,
                cancellationToken
            );
            delivery = await storage.LockNextAsync(transaction, subscriptionKey, cancellationToken);
            if (delivery is null)
                return InboxProcessingResult.NoWork;

            attempt = new MessagingTelemetry.Attempt(
                "process",
                delivery.MessageId,
                delivery.CorrelationId,
                delivery.CausationId,
                delivery.TraceParent,
                delivery.TraceState
            );

            // Local effects and outgoing work remain inside the row lock's transaction.
            await handler.HandleAsync(delivery, cancellationToken);
            if (!ReferenceEquals(transaction, database.Database.CurrentTransaction))
                throw new InvalidOperationException(
                    "Inbox handlers must retain the processor's native transaction."
                );

            await database.SaveChangesAsync(cancellationToken);
            await storage.CompleteAsync(transaction, subscriptionKey, delivery, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            attempt.Complete("processed");
            return InboxProcessingResult.Processed;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            attempt?.Cancel();
            // Disposal rolls back pending local work. A commit response can still be ambiguous.
            throw;
        }
        catch (Exception processingFailure) when (delivery is not null)
        {
            // The processing transaction has been disposed before this catch. Do not save its
            // tracked proposal again: only conditional retry metadata uses a new small transaction.
            try
            {
                await storage.DeferAsync(
                    subscriptionKey,
                    delivery,
                    options.RetryDelay,
                    cancellationToken
                );
            }
            catch (Exception schedulingFailure)
            {
                var combined = new AggregateException(
                    "Inbox processing and retry scheduling both failed.",
                    processingFailure,
                    schedulingFailure
                );
                attempt?.Fail(combined);
                if (logger is not null)
                    AttemptFailed(logger, "process", delivery.MessageId, combined);

                throw combined;
            }

            attempt?.Fail(processingFailure);
            if (logger is not null)
                AttemptFailed(logger, "process", delivery.MessageId, processingFailure);

            throw;
        }
        finally
        {
            attempt?.Dispose();
        }
    }

    [LoggerMessage(
        Level = LogLevel.Error,
        Message = "Message {MessageId} {Operation} attempt failed."
    )]
    private static partial void AttemptFailed(
        ILogger logger,
        string operation,
        Guid messageId,
        Exception failure
    );
}
