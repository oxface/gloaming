namespace Gloaming.CommandReceipts.EntityFrameworkCore.Postgres;

/// <summary>A consumer-selected logical action, scoped independently of transport or entity identity.</summary>
public sealed record CommandReceiptKey
{
    /// <summary>Constructs an immutable key. Scope is attribution, not authentication or admission.</summary>
    public CommandReceiptKey(string scopeKey, string operation, Guid operationId)
    {
        ValidateText(scopeKey, 256);
        ValidateText(operation, 128);
        ArgumentOutOfRangeException.ThrowIfEqual(operationId, Guid.Empty);

        ScopeKey = scopeKey;
        Operation = operation;
        OperationId = operationId;
    }

    /// <summary>Consumer-selected tenant or other admitted namespace.</summary>
    public string ScopeKey { get; }

    /// <summary>Stable command kind within that namespace.</summary>
    public string Operation { get; }

    /// <summary>Stable identity of one action; equal retries reuse it.</summary>
    public Guid OperationId { get; }

    internal static void ValidateText(string text, int maximum)
    {
        ArgumentException.ThrowIfNullOrEmpty(text);

        if (text.Length > maximum || text.Contains('\0', StringComparison.Ordinal))
            throw new ArgumentException(
                "Receipt key text exceeds its limit or contains NUL.",
                nameof(text)
            );
    }
}
