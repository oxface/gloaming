using Microsoft.EntityFrameworkCore;
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

        storage.RequireFreshContext();

        var handler = services.GetRequiredKeyedService<IInboxHandler<TDbContext>>(subscriptionKey);
        IncomingMessage? delivery = null;
        MessagingTelemetry.Attempt? attempt = null;
        try
        {
            await using var transaction = await database.Database.BeginTransactionAsync(
                System.Data.IsolationLevel.ReadCommitted,
                cancellationToken
            );
            var locked = await storage.LockNextAsync(
                transaction,
                subscriptionKey,
                cancellationToken
            );
            if (locked is null)
                return InboxProcessingResult.NoWork;

            delivery = locked.Message;
            attempt = new MessagingTelemetry.Attempt(
                "process",
                delivery.MessageId,
                delivery.CorrelationId,
                delivery.CausationId,
                delivery.TraceParent,
                delivery.TraceState
            );

            // The delivery lock predates this savepoint, so rollback of local effects retains
            // exclusion through failure accounting. Never schedule through a later transaction.
            const string savepoint = "gloaming_inbox_work";
            await transaction.CreateSavepointAsync(savepoint, cancellationToken);
            try
            {
                await handler.HandleAsync(delivery, cancellationToken);
                if (!ReferenceEquals(transaction, database.Database.CurrentTransaction))
                    throw new InvalidOperationException(
                        "Inbox handlers must retain the processor's native transaction."
                    );

                await database.SaveChangesAsync(cancellationToken);
                await storage.CompleteAsync(
                    transaction,
                    subscriptionKey,
                    delivery,
                    cancellationToken
                );
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception processingFailure)
            {
                try
                {
                    await transaction.RollbackToSavepointAsync(savepoint, cancellationToken);
                    // Failed tracked proposals are discarded with the scope, never saved again.
                    var policy = services.GetKeyedService<IInboxFailurePolicy<TDbContext>>(
                        subscriptionKey
                    );
                    var decision =
                        policy?.Decide(
                            new InboxFailure(
                                subscriptionKey,
                                delivery,
                                processingFailure,
                                checked(locked.FailureCount + 1),
                                checked(locked.FailuresSinceRedrive + 1)
                            )
                        )
                        ?? (
                            policy is null
                                ? new InboxFailureDecision.RetryAfter(
                                    options.RetryDelay,
                                    "processing-failed"
                                )
                                : throw new InvalidOperationException(
                                    "An inbox failure policy returned no decision."
                                )
                        );
                    await storage.RecordFailureAsync(
                        transaction,
                        subscriptionKey,
                        delivery,
                        decision,
                        cancellationToken
                    );
                    await transaction.CommitAsync(cancellationToken);
                }
                catch (Exception recoveryFailure)
                {
                    throw new InboxFailureAccountingException(
                        subscriptionKey,
                        delivery.ProducerKey,
                        delivery.MessageId,
                        processingFailure,
                        recoveryFailure
                    );
                }

                throw;
            }

            // Commit errors are ambiguous. They must never trigger a compensating failure write.
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
        catch (Exception failure) when (delivery is not null)
        {
            attempt?.Fail(failure);
            if (logger is not null)
                AttemptFailed(logger, "process", delivery.MessageId, failure);

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
