using System.Data;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Gloaming.CommandReceipts.EntityFrameworkCore.Postgres;

internal sealed class ReceiptScope
{
    private static readonly ConditionalWeakTable<DbContext, ReceiptScope> Scopes = new();
    private DbContextId contextId;
    internal IDbContextTransaction? Transaction { get; set; }
    internal bool Acquired { get; set; }
    internal bool Failed { get; set; }
    internal CommandReceiptRecord? Record { get; set; }
    internal Dictionary<string, object?>? Values { get; set; }

    internal static ReceiptScope For(DbContext database)
    {
        var scope = Scopes.GetOrCreateValue(database);
        if (scope.contextId != database.ContextId)
        {
            scope = new ReceiptScope { contextId = database.ContextId };
            Scopes.Remove(database);
            Scopes.Add(database, scope);
        }

        return scope;
    }

    internal void RequireTransaction(DbContext database)
    {
        if (
            Failed
            || contextId != database.ContextId
            || database.Database.CurrentTransaction is not { } current
            || (Transaction is not null && !ReferenceEquals(current, Transaction))
            || current.GetDbTransaction().Connection is null
            || current.GetDbTransaction().IsolationLevel != IsolationLevel.ReadCommitted
            || System.Transactions.Transaction.Current is not null
        )
            throw new InvalidOperationException(
                "Use the original active ReadCommitted transaction and dispose failed receipt scopes."
            );
    }

    internal void Validate(DbContext database)
    {
        if (Failed)
            throw new InvalidOperationException(
                "Dispose the failed receipt scope before another attempt."
            );

        foreach (var entry in database.ChangeTracker.Entries<CommandReceiptRecord>())
        {
            if (
                entry.State is EntityState.Modified or EntityState.Deleted
                || (entry.State == EntityState.Added && !ReferenceEquals(entry.Entity, Record))
            )
                throw new InvalidOperationException(
                    "Tracked receipts are append-only and must originate in an acquired attempt."
                );
        }

        if (Record is null)
            return;

        RequireTransaction(database);
        var proposal = database.Entry(Record);
        if (proposal.State is EntityState.Detached or EntityState.Deleted)
            throw new InvalidOperationException(
                "The completed receipt must remain tracked with its business changes."
            );

        foreach (var value in Values!)
        {
            if (!Equals(proposal.Property(value.Key).CurrentValue, value.Value))
                throw new InvalidOperationException(
                    "A retained receipt proposal was changed outside its attempt."
                );
        }
    }
}
