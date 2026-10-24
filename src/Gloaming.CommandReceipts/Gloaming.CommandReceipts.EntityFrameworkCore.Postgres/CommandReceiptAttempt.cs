using Microsoft.EntityFrameworkCore;

namespace Gloaming.CommandReceipts.EntityFrameworkCore.Postgres;

/// <summary>One operation bound to the acquiring context and native transaction. Its lock lives until transaction completion.</summary>
public sealed class CommandReceiptAttempt
{
    private readonly DbContext database;
    private readonly ReceiptScope scope;
    private readonly CommandReceiptKey key;
    private readonly string input;

    internal CommandReceiptAttempt(
        DbContext database,
        ReceiptScope scope,
        CommandReceiptKey key,
        string input,
        CommandReceiptContent? result
    )
    {
        this.database = database;
        this.scope = scope;
        this.key = key;
        this.input = input;
        Result = result;
    }

    /// <summary>Original retained result, or null when the caller may make a new decision. Never execute again because a result reader is incompatible.</summary>
    public CommandReceiptContent? Result { get; }

    /// <summary>Stages one immutable completed receipt. Caller must save/commit it with the accepted business changes; rejection may leave no receipt.</summary>
    public void Retain(CommandReceiptContent result)
    {
        ArgumentNullException.ThrowIfNull(result);

        try
        {
            scope.RequireTransaction(database);
            if (
                Result is not null
                || scope.Record is not null
                || !ReferenceEquals(ReceiptScope.For(database), scope)
            )
                throw new InvalidOperationException(
                    "A receipt can be retained only once for a new attempt in its original scope."
                );

            var row = new CommandReceiptRecord(key, input, result);
            database.Set<CommandReceiptRecord>().Add(row);
            scope.Record = row;
            scope.Values = database
                .Entry(row)
                .Properties.ToDictionary(
                    property => property.Metadata.Name,
                    property => property.CurrentValue
                );
        }
        catch
        {
            scope.Failed = true;
            throw;
        }
    }
}
