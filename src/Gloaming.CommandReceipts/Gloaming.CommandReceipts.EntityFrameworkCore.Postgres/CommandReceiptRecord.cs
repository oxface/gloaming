namespace Gloaming.CommandReceipts.EntityFrameworkCore.Postgres;

/// <summary>Provided append-only technical record. Application code writes through an acquired attempt.</summary>
public sealed class CommandReceiptRecord
{
    private CommandReceiptRecord() { }

    internal CommandReceiptRecord(CommandReceiptKey key, string input, CommandReceiptContent result)
    {
        ScopeKey = key.ScopeKey;
        Operation = key.Operation;
        OperationId = key.OperationId;
        Input = input;
        FormatVersion = result.FormatVersion;
        Result = result.Payload;
    }

    /// <summary>Consumer-selected scope.</summary>
    public string ScopeKey { get; private set; } = null!;

    /// <summary>Stable command kind.</summary>
    public string Operation { get; private set; } = null!;

    /// <summary>Logical action identity.</summary>
    public Guid OperationId { get; private set; }

    /// <summary>Consumer result reader version.</summary>
    public int FormatVersion { get; private set; }

    /// <summary>Ordinal comparison evidence selected by the consumer.</summary>
    public string Input { get; private set; } = null!;

    /// <summary>Opaque retained result text.</summary>
    public string Result { get; private set; } = null!;
}
