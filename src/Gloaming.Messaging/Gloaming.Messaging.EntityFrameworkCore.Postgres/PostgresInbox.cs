using System.Data;
using System.Transactions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;

namespace Gloaming.Messaging.EntityFrameworkCore.Postgres;

internal sealed class PostgresInbox<TDbContext>(TDbContext database) : IInbox<TDbContext>
    where TDbContext : DbContext
{
    private readonly PostgresInboxStorage storage = new(database);

    public async Task<InboxReceiveResult> ReceiveAsync(
        string subscriptionKey,
        IncomingMessage message,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subscriptionKey);
        ArgumentNullException.ThrowIfNull(message);
        cancellationToken.ThrowIfCancellationRequested();

        var transaction = database.Database.CurrentTransaction;
        if (
            transaction is null
            || Transaction.Current is not null
            || database.Database.GetEnlistedTransaction() is not null
            || transaction.GetDbTransaction().IsolationLevel
                != System.Data.IsolationLevel.ReadCommitted
        )
            throw new InvalidOperationException(
                "Inbox intake requires an explicit native ReadCommitted transaction without ambient/enlisted work."
            );

        // A missing comparison row can be concurrent retention or an RLS-hidden key. Recheck
        // finitely: command timeouts alone cannot bound repeated successful empty statements.
        for (int observation = 0; observation < 3; observation++)
        {
            await using var insert = storage.Command(
                transaction,
                $"""
                INSERT INTO {storage.Table}
                  (subscription_key, producer_key, message_id, message_name, schema_version, payload,
                   tenant_key, correlation_id, causation_id, trace_parent, trace_state)
                VALUES (@subscription, @producer, @id, @name, @schema, CAST(@payload AS jsonb),
                        @tenant, @correlation, @causation, @traceParent, @traceState)
                ON CONFLICT (subscription_key, producer_key, message_id) DO NOTHING
                RETURNING message_id
                """
            );
            PostgresInboxStorage.EnvelopeParameters(insert, subscriptionKey, message);
            if (await insert.ExecuteScalarAsync(cancellationToken) is not null)
                return InboxReceiveResult.Queued;

            // Compare business content only: a retry may carry a different transport span.
            // The first committed intake retains its diagnostic context.
            // INSERT can wait for another writer to commit, then do nothing even though that row
            // was not visible when INSERT started. A separate ReadCommitted statement sees the
            // committed winner; a SELECT inside the same statement could still miss it.
            await using var compare = storage.Command(
                transaction,
                $"""
                SELECT message_name = @name AND schema_version = @schema AND payload = CAST(@payload AS jsonb)
                  AND tenant_key IS NOT DISTINCT FROM @tenant
                  AND correlation_id IS NOT DISTINCT FROM @correlation
                  AND causation_id IS NOT DISTINCT FROM @causation
                FROM {storage.Table}
                WHERE subscription_key = @subscription AND producer_key = @producer AND message_id = @id
                """
            );

            PostgresInboxStorage.EnvelopeParameters(compare, subscriptionKey, message);
            var sameEnvelope = await compare.ExecuteScalarAsync(cancellationToken);
            if (sameEnvelope is null)
                continue; // Retention removed the conflicting receipt; attempt a new intake.

            if (sameEnvelope is true)
                return InboxReceiveResult.AlreadyReceived;

            throw new InboxMessageConflictException(
                subscriptionKey,
                message.ProducerKey,
                message.MessageId
            );
        }

        throw new DbUpdateConcurrencyException(
            "Inbox intake could not observe the conflicting receipt after three insert/compare observations. Review native SELECT visibility or retry the caller-owned transaction after concurrent retention."
        );
    }
}
