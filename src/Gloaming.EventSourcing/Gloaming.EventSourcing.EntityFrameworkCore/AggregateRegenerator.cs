using System.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using IsolationLevel = System.Data.IsolationLevel;

namespace Gloaming.EventSourcing.EntityFrameworkCore;

/// <summary>
/// Executes one complete regeneration using fresh scopes and native read-committed transactions.
/// The supplied scope factory establishes any required authorized context before returning a scope.
/// </summary>
public sealed class AggregateRegenerator<TAggregate, TContext>
    where TAggregate : class
    where TContext : DbContext
{
    private readonly IServiceScopeFactory scopes;

    /// <summary>Uses native scope creation for independent regeneration attempts with consumer-established context.</summary>
    public AggregateRegenerator(IServiceScopeFactory scopes)
    {
        ArgumentNullException.ThrowIfNull(scopes);

        this.scopes = scopes;
    }

    /// <summary>Executes bounded fresh-scope replay/save/commit attempts and reports terminal success, conflict, cancellation or commit uncertainty.</summary>
    public async Task<AggregateRegenerationResult> RegenerateAsync(
        Guid id,
        AggregateRegenerationOptions options,
        IProgress<AggregateRegenerationProgress>? progress = null,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentOutOfRangeException.ThrowIfEqual(id, Guid.Empty);
        ArgumentNullException.ThrowIfNull(options);

        int maxAttempts = options.MaxAttempts;
        TimeSpan retryDelay = options.RetryDelay;

        ArgumentOutOfRangeException.ThrowIfLessThan(maxAttempts, 1);
        if (retryDelay < TimeSpan.Zero || retryDelay.TotalMilliseconds > uint.MaxValue - 1L)
            throw new ArgumentOutOfRangeException(
                nameof(options),
                "Use a supported nonnegative delay."
            );

        int attempts = 0;
        AggregateRebuildResult? capturedHead = null;

        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                attempts++;
                var (result, retryable) = await AttemptAsync(
                    id,
                    attempts,
                    progress,
                    cancellationToken
                );

                capturedHead = result.CapturedHead;
                if (!retryable || attempts == maxAttempts)
                    return result;

                progress?.Report(
                    new(attempts, AggregateRegenerationPhase.Retrying, capturedHead, result.Error)
                );

                await Task.Delay(retryDelay, cancellationToken);
            }
        }
        catch (OperationCanceledException failure) when (cancellationToken.IsCancellationRequested)
        {
            return new(AggregateRegenerationStatus.Cancelled, attempts, capturedHead, failure);
        }
        catch (Exception failure)
        {
            return new(AggregateRegenerationStatus.Failed, attempts, capturedHead, failure);
        }
    }

    private async Task<(AggregateRegenerationResult Result, bool Retryable)> AttemptAsync(
        Guid id,
        int attempt,
        IProgress<AggregateRegenerationProgress>? progress,
        CancellationToken cancellationToken
    )
    {
        AsyncServiceScope? scope = null;
        IDbContextTransaction? transaction = null;
        TContext? database = null;
        AggregateRebuildResult? capturedHead = null;
        AggregateRegenerationResult result;

        bool retryable = false;
        bool replaying = false;
        bool saving = false;
        bool committing = false;

        try
        {
            if (Transaction.Current is not null)
                throw new InvalidOperationException(
                    "Regeneration owns its transaction; ambient transactions are unsupported."
                );

            scope = scopes.CreateAsyncScope();
            progress?.Report(new(attempt, AggregateRegenerationPhase.Started));

            database = scope.Value.ServiceProvider.GetRequiredService<TContext>();
            var rebuilder = scope.Value.ServiceProvider.GetRequiredService<
                IAggregateRebuilder<TAggregate>
            >();

            if (
                database.Database.CurrentTransaction is not null
                || database.ChangeTracker.Entries().Any()
            )
                throw new InvalidOperationException(
                    "Regeneration requires a fresh native operation context."
                );

            transaction = await database.Database.BeginTransactionAsync(
                IsolationLevel.ReadCommitted,
                cancellationToken
            );

            replaying = true;
            capturedHead = await rebuilder.RebuildAsync(id, cancellationToken);
            replaying = false;

            if (capturedHead is null)
                result = new(AggregateRegenerationStatus.NotFound, attempt, null, null);
            else
            {
                await MaintenanceWriteRegistry.RequireAsync(
                    database,
                    id,
                    transaction,
                    cancellationToken
                );
                progress?.Report(new(attempt, AggregateRegenerationPhase.Replayed, capturedHead));

                saving = true;
                await database.SaveChangesAsync(cancellationToken);
                saving = false;

                cancellationToken.ThrowIfCancellationRequested();

                committing = true;
                await transaction.CommitAsync(cancellationToken);

                result = new(AggregateRegenerationStatus.Completed, attempt, capturedHead, null);
            }
        }
        catch (Exception failure)
        {
            retryable =
                !committing
                && database is not null
                && transaction is not null
                && (
                    replaying
                        && failure is AggregateCaptureConflict capture
                        && capture.Matches(database, id, transaction)
                    || saving
                        && failure is DbUpdateConcurrencyException concurrency
                        && MaintenanceWriteRegistry.IsConflict(database, transaction, concurrency)
                );

            var status =
                committing ? AggregateRegenerationStatus.Indeterminate
                : retryable ? AggregateRegenerationStatus.Conflict
                : failure is OperationCanceledException && cancellationToken.IsCancellationRequested
                    ? AggregateRegenerationStatus.Cancelled
                : AggregateRegenerationStatus.Failed;

            result = new(status, attempt, capturedHead, failure);
        }

        // Attempt both disposals, even if transaction disposal fails. Never erase a known commit.
        Exception? cleanup = await DisposeAsync(transaction, null);

        if (scope is { } createdScope)
            cleanup = await DisposeAsync(createdScope, cleanup);

        if (cleanup is not null)
        {
            result = result with
            {
                Status =
                    result.Status == AggregateRegenerationStatus.Conflict
                        ? AggregateRegenerationStatus.Failed
                        : result.Status,
                Error = Combine(result.Error, cleanup),
            };

            retryable = false;
        }

        return (result, retryable);
    }

    private static async Task<Exception?> DisposeAsync(
        IAsyncDisposable? disposable,
        Exception? previous
    )
    {
        try
        {
            if (disposable is not null)
                await disposable.DisposeAsync();

            return previous;
        }
        catch (Exception failure)
        {
            return Combine(previous, failure);
        }
    }

    private static Exception Combine(Exception? previous, Exception failure) =>
        previous is null ? failure : new AggregateException(previous, failure);
}
