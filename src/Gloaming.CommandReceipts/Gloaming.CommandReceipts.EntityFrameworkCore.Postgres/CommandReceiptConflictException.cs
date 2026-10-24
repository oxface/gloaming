namespace Gloaming.CommandReceipts.EntityFrameworkCore.Postgres;

/// <summary>The retained logical key belongs to different comparison input.</summary>
public sealed class CommandReceiptConflictException : InvalidOperationException
{
    /// <summary>Reports an input conflict without disclosing either payload.</summary>
    public CommandReceiptConflictException()
        : base("The receipt key already belongs to different input.") { }
}
